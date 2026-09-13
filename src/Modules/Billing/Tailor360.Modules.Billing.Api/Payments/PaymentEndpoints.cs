using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Contracts.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The counter's money: the modes it may take money in, the payment it records, the advance it applies by
/// hand, and the balance it reads before either. Recording and reading are under <c>payments.record</c>,
/// which Reception holds as well as the Cashier; applying an advance by hand is under
/// <c>payments.allocate_manual</c> with its step-up and its reason.
/// </summary>
internal static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder billing)
    {
        MapAvailableModes(billing);
        MapRecord(billing);
        MapRead(billing);
        MapAllocate(billing);
        MapOrderBalance(billing);

        return billing;
    }

    private static void MapAvailableModes(RouteGroupBuilder billing)
        => billing.MapGet("/payment-modes/available", async Task<IResult> (
                HttpContext context,
                IPaymentModeStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                var modes = await store.ListAsync(caller.Context.OrganisationId, cancellationToken);

                // A mode through a provider is left off the buttons until a provider is configured (OD-03).
                return Results.Ok(modes
                    .Where(mode => mode.IsActive && !mode.RequiresProvider && mode.IsAvailableAt(branchId))
                    .OrderBy(mode => mode.Code, StringComparer.Ordinal)
                    .Select(AvailablePaymentModePayload.From)
                    .ToArray());
            })
            .Produces<AvailablePaymentModePayload[]>(StatusCodes.Status200OK)
            .WithName("ListAvailablePaymentModes")
            .WithSummary("List the active payment modes the caller's branch may take money in.")
            .WithDescription("Only the code, the name and whether a reference is required: what the counter needs to record a payment.")
            .RequirePermission(BillingPermissions.RecordPayment, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource("The branch is the caller's own, from the session; a listing names no resource.", "#162")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapRecord(RouteGroupBuilder billing)
        => billing.MapPost("/payments", async Task<IResult> (
                RecordPaymentRequest? request,
                HttpContext context,
                PaymentHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                // The idempotency key rides onto the row as its own guard against a twin (INV-PAY-02).
                var clientKey = context.Request.Headers.TryGetValue(IdempotencyHeaders.Key, out var key) ? key.ToString() : null;
                var result = await handler.RecordAsync(
                    new RecordPaymentCommand(
                        caller.Context.OrganisationId, branchId, caller.UserId,
                        request?.OrderId ?? Guid.Empty, request?.ModeCode, request?.Amount ?? 0m, request?.Reference, clientKey, caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created($"/api/v1/billing/payments/{result.Value.Payment.Id}", PaymentPayload.From(result.Value.Payment, result.Value.Receipt));
            })
            .Produces<PaymentPayload>(StatusCodes.Status201Created)
            .WithName("RecordPayment")
            .WithSummary("Record a payment against an order in the caller's open cashier session, allocate it at once and issue its receipt.")
            .WithDescription(
                "Refused without an open session (409). The money goes to the order's posted invoices oldest first; what "
                + "is left is held as an advance and applied when the order posts its next invoice. The mode must be one "
                + "the branch takes; a mode that requires a reference is refused without one, and a reference that reads "
                + "as a card number is refused always. The same mode and reference twice is a 409. The receipt is numbered "
                + "and issued in the same transaction; its document is rendered by the worker afterwards.")
            .RequirePermission(BillingPermissions.RecordPayment, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource("The payment is created at the caller's own branch against an order the handler checks is the branch's; there is no resource yet.", "#162")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PaymentHandler.RecordedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapRead(RouteGroupBuilder billing)
        => billing.MapGet("/payments/{paymentId:guid}", async Task<IResult> (
                Guid paymentId,
                HttpContext context,
                IPaymentStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var payment = await store.FindAsync(paymentId, caller.Context.OrganisationId, cancellationToken);
                if (payment is null)
                {
                    return Problems.From(BillingErrors.PaymentNotFound, context);
                }

                var receipt = await store.FindReceiptForPaymentAsync(paymentId, caller.Context.OrganisationId, cancellationToken);
                return Results.Ok(PaymentPayload.From(payment, receipt));
            })
            .Produces<PaymentPayload>(StatusCodes.Status200OK)
            .WithName("GetPayment")
            .WithSummary("Read one payment with its allocations and what of it is still held.")
            .WithDescription("A payment taken at another branch reads as 404.")
            .RequirePermission(BillingPermissions.RecordPayment, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Payment, "paymentId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapAllocate(RouteGroupBuilder billing)
        => billing.MapPost("/payments/{paymentId:guid}/allocations", async Task<IResult> (
                Guid paymentId,
                AllocateAdvanceRequest? request,
                HttpContext context,
                PaymentHandler handler,
                IPaymentStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.AllocateManuallyAsync(
                    new AllocateAdvanceCommand(paymentId, caller.Context.OrganisationId, request?.InvoiceId ?? Guid.Empty, request?.Amount ?? 0m, request?.Reason, caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                var receipt = await store.FindReceiptForPaymentAsync(paymentId, caller.Context.OrganisationId, cancellationToken);
                return Results.Ok(PaymentPayload.From(result.Value, receipt));
            })
            .Produces<PaymentPayload>(StatusCodes.Status200OK)
            .WithName("AllocateAdvance")
            .WithSummary("Apply part of a payment's held advance to a posted invoice of the same order, by hand.")
            .WithDescription(
                "Against the automatic rule, so under step-up and with a reason. Never more than the advance still holds, "
                + "never more than the invoice still owes; the invoice must be a posted invoice of the order the payment was taken against.")
            .RequirePermission(BillingPermissions.AllocateManual, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Payment, "paymentId")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PaymentHandler.AllocatedManuallyAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapOrderBalance(RouteGroupBuilder billing)
        => billing.MapGet("/orders/{orderId:guid}/balance", async Task<IResult> (
                Guid orderId,
                HttpContext context,
                IFinancialTotalsQuery totals,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var balance = await totals.GetOrderBalanceAsync(orderId, caller.Context.OrganisationId, cancellationToken);

                return balance is null
                    ? Problems.From(BillingErrors.OrderNotKnown, context)
                    : Results.Ok(OrderBalancePayload.From(balance));
            })
            .Produces<OrderBalancePayload>(StatusCodes.Status200OK)
            .WithName("GetOrderBalance")
            .WithSummary("Read what an order still owes across its posted invoices, and what is held against it.")
            .WithDescription(
                "Computed from rows on every read: posted charges minus credit notes plus debit notes minus allocations plus "
                + "refunds, per invoice and in all, and the advances held against the order and not yet applied. An order "
                + "Billing has not heard of, or one at another branch, reads as 404.")
            .RequirePermission(BillingPermissions.RecordPayment, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Order, "orderId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);
}
