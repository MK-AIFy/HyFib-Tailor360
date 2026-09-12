using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The compensating records (E09-F03-3): a reversal under <c>payments.reverse</c>, a refund under
/// <c>payments.refund</c>, each with its step-up and its reason (raci row 5), each idempotent, neither
/// touching the row it compensates.
/// </summary>
internal static class RefundEndpoints
{
    public static RouteGroupBuilder MapRefundEndpoints(this RouteGroupBuilder billing)
    {
        billing.MapPost("/payments/{paymentId:guid}/reversal", async Task<IResult> (
                Guid paymentId,
                ReversePaymentRequest? request,
                HttpContext context,
                RefundHandler handler,
                IPaymentStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReverseAsync(new ReversePaymentCommand(paymentId, caller.Context.OrganisationId, request?.Reason, caller.UserId), cancellationToken);
                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                var receipt = await store.FindReceiptForPaymentAsync(paymentId, caller.Context.OrganisationId, cancellationToken);
                return Results.Ok(PaymentPayload.From(result.Value, receipt));
            })
            .Produces<PaymentPayload>(StatusCodes.Status200OK)
            .WithName("ReversePayment")
            .WithSummary("Reverse a payment recorded in error: a compensating record, the original untouched.")
            .WithDescription(
                "For money that never cleared. The payment's allocations and its advance count for nothing from here on and the "
                + "invoices' paid status is recomputed from the rows that remain. Once per payment (409 on a second); refused "
                + "where money has already been paid back from the payment. Step-up and a reason.")
            .RequirePermission(BillingPermissions.Reverse, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Payment, "paymentId")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RefundHandler.ReversedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapPost("/refunds", async Task<IResult> (
                RecordRefundRequest? request,
                HttpContext context,
                RefundHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                var clientKey = context.Request.Headers.TryGetValue(IdempotencyHeaders.Key, out var key) ? key.ToString() : null;
                var result = await handler.RefundAsync(
                    new RecordRefundCommand(
                        caller.Context.OrganisationId, branchId, caller.UserId,
                        request?.PaymentId, request?.InvoiceId, request?.ModeCode, request?.Amount ?? 0m, request?.Reference, clientKey, request?.Reason, caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created($"/api/v1/billing/refunds/{result.Value.Id}", RefundPayload.From(result.Value));
            })
            .Produces<RefundPayload>(StatusCodes.Status201Created)
            .WithName("RecordRefund")
            .WithSummary("Pay money back to the customer in the caller's open cashier session, through a mode allowed for refunds.")
            .WithDescription(
                "Against exactly one source: a payment's advance not applied and not yet paid back (paymentId), or a posted "
                + "invoice that holds more than it charges — a credit note's value, an over-payment (invoiceId). Never more than "
                + "the source still holds. Refused without an open session (409), through a mode not allowed for refunds, or "
                + "without the reference the mode requires. Step-up and a reason. Whether an advance is paid back on a "
                + "cancellation is the Owner's policy (OD-04, OD-05); this is the mechanism.")
            .RequirePermission(BillingPermissions.Refund, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource("The refund is created at the caller's own branch against a source the handler checks is the branch's; there is no resource yet.", "#163")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RefundHandler.RefundedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        billing.MapGet("/refunds/{refundId:guid}", async Task<IResult> (
                Guid refundId,
                HttpContext context,
                IPaymentStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var refund = await store.FindRefundAsync(refundId, caller.Context.OrganisationId, cancellationToken);
                return refund is null
                    ? Problems.From(BillingErrors.RefundNotFound, context)
                    : Results.Ok(RefundPayload.From(refund));
            })
            .Produces<RefundPayload>(StatusCodes.Status200OK)
            .WithName("GetRefund")
            .WithSummary("Read one refund.")
            .WithDescription("A refund paid at another branch reads as 404.")
            .RequirePermission(BillingPermissions.RecordPayment, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Refund, "refundId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

        return billing;
    }
}
