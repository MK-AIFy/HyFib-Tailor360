using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
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
/// The single-use dispatch exception (#164): approved by the Owner under
/// <c>billing.approve_dispatch_exception</c>, step-up and a reason. Custody's dispatch scan (#37) is not
/// built yet, so this has no live caller — <c>IDispatchEligibilityQuery</c> in
/// <c>Tailor360.Modules.Billing.Contracts</c> is what #37 consumes it through, and the exception itself
/// has no separate read route because nothing outside this module reads it directly. The order balance
/// the approver needs in order to set the maximum allowance does have a narrow read below, added for
/// #220 under the approving permission itself.
/// </summary>
internal static class DispatchExceptionEndpoints
{
    public static RouteGroupBuilder MapDispatchExceptionEndpoints(this RouteGroupBuilder billing)
    {
        MapBalanceRead(billing);
        MapApprove(billing);

        return billing;
    }

    /// <summary>
    /// Reads what an order still owes, for the approver about to set a dispatch exception's maximum
    /// outstanding allowance (#220). Exactly <c>PaymentEndpoints</c>'s own <c>GetOrderBalance</c> route,
    /// exposed a second time under <see cref="BillingPermissions.ApproveDispatchException"/> alone: that
    /// permission is Owner-only and the Owner does not hold <see cref="BillingPermissions.RecordPayment"/>,
    /// so without this route the Owner would set a safety-critical maximum blind. It reaches exactly the
    /// orders the approve route already reaches — an order Billing knows at the caller's own branch — and
    /// nothing wider.
    /// </summary>
    private static void MapBalanceRead(RouteGroupBuilder billing)
        => billing.MapGet("/orders/{orderId:guid}/dispatch-exception-balance", async Task<IResult> (
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
            .WithName("GetOrderBalanceForDispatchException")
            .WithSummary("Read what an order still owes, for the approver about to set a dispatch exception's maximum allowance.")
            .WithDescription(
                "The same figures `GetOrderBalance` reads, reachable here under `billing.approve_dispatch_exception` "
                + "alone (#220), because setting a safety-critical maximum without seeing the real balance is worse "
                + "than not asking, and the Owner who approves does not hold `payments.record`. Step-up, because it "
                + "is the reading half of an operation whose permission demands it and the catalogue's flag is per "
                + "permission, not per route. An order Billing has not heard of, or one at another branch, reads as 404.")
            .RequirePermission(BillingPermissions.ApproveDispatchException, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.Order, "orderId")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapApprove(RouteGroupBuilder billing)
        => billing.MapPost("/dispatch-exceptions", async Task<IResult> (
                CreateDispatchExceptionRequest? request,
                HttpContext context,
                DispatchExceptionHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                var result = await handler.ApproveAsync(
                    new ApproveDispatchExceptionCommand(
                        caller.Context.OrganisationId, branchId, request?.OrderId ?? Guid.Empty, request?.JobIds,
                        request?.MaxOutstandingAmount ?? 0m, request?.ReasonCode, request?.ReasonText,
                        request?.ExpiresAt ?? default, caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Created($"/api/v1/billing/dispatch-exceptions/{result.Value.Id}", DispatchExceptionPayload.From(result.Value));
            })
            .Produces<DispatchExceptionPayload>(StatusCodes.Status201Created)
            .WithName("ApproveDispatchException")
            .WithSummary("Approve a single-use dispatch exception for named jobs of an order.")
            .WithDescription(
                "Bound to the order, exactly these garment jobs, a maximum outstanding amount, the dispatch policy version "
                + "in force and an expiry of at most 72 hours. Refused where the order is not Billing's, is at another "
                + "branch, or names a job that is not a live job of the order. Consumed exactly once through "
                + "IDispatchEligibilityQuery, which re-validates the balance, the job set, the policy version, the expiry "
                + "and that the dispatcher differs from the approver — none of that happens here. Step-up and a reason.")
            .RequirePermission(BillingPermissions.ApproveDispatchException, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource(
                "The exception is approved for the caller's own branch against an order the handler checks is the branch's; there is no resource yet.",
                "#164")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(DispatchExceptionHandler.ApprovedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
}
