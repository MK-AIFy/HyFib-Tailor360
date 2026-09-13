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
/// The approval of a closed session's variance (E09-F03-3b), by someone other than the cashier who
/// closed it (INV-CSH-04). The batch itself is read back on the session's own payload from the approve
/// route; the session it belongs to has its own narrow read below, added for #220 so the approving
/// permission can see what it is approving without the general-purpose <c>payments.session</c> grant.
/// </summary>
internal static class ReconciliationEndpoints
{
    public static RouteGroupBuilder MapReconciliationEndpoints(this RouteGroupBuilder billing)
    {
        MapRead(billing);
        MapApprove(billing);

        return billing;
    }

    /// <summary>
    /// Reads the closed cashier session a reconciliation approval is being decided on (#220). Exactly
    /// <see cref="CashierSessionEndpoints"/>'s own <c>GetCashierSession</c> route, exposed a second time
    /// under <see cref="BillingPermissions.ApproveReconciliation"/> alone: the Owner holds that permission
    /// and not <see cref="BillingPermissions.Session"/>, and without this route they could approve a
    /// variance they could never read. It reaches exactly the sessions the approve route already reaches
    /// — any closed session at the caller's own branch — and nothing wider, so it grants no more than
    /// approving already implied.
    /// </summary>
    private static void MapRead(RouteGroupBuilder billing)
        => billing.MapGet("/cashier-sessions/{sessionId:guid}/reconciliation", async Task<IResult> (
                Guid sessionId,
                HttpContext context,
                ICashierSessionStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var session = await store.FindAsync(sessionId, caller.Context.OrganisationId, cancellationToken);
                if (session is null || session.IsOpen)
                {
                    return Problems.From(BillingErrors.CashierSessionNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(session));

                return Results.Ok(CashierSessionPayload.From(session));
            })
            .Produces<CashierSessionPayload>(StatusCodes.Status200OK)
            .WithName("GetCashierSessionForReconciliation")
            .WithSummary("Read the cashier session a reconciliation approval is about to decide on, with its count sheet.")
            .WithDescription(
                "The same record `GetCashierSession` reads, reachable here under `payments.approve_reconciliation` "
                + "alone (#220), because approving a variance needs to see the count sheet it was raised against and "
                + "the Owner who approves does not hold `payments.session`. Step-up, because it is the reading half "
                + "of an operation whose permission demands it and the catalogue's flag is per permission, not per "
                + "route. Restricted to closed sessions: an approver never needs to see one still open, and "
                + "`payments.approve_reconciliation` grants no view into a live drawer. A session at another "
                + "branch, or one still open, reads as 404.")
            .RequirePermission(BillingPermissions.ApproveReconciliation, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.CashierSession, "sessionId")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapApprove(RouteGroupBuilder billing)
        => billing.MapPost("/cashier-sessions/{sessionId:guid}/reconciliation/approve", async Task<IResult> (
                Guid sessionId,
                ApproveReconciliationRequest? request,
                HttpContext context,
                ReconciliationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ApproveAsync(
                    new ApproveReconciliationCommand(sessionId, caller.Context.OrganisationId, request?.Reason, caller.UserId),
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(ReconciliationBatchPayload.From(result.Value));
            })
            .Produces<ReconciliationBatchPayload>(StatusCodes.Status200OK)
            .WithName("ApproveReconciliation")
            .WithSummary("Approve a closed cashier session's variance, by someone other than the cashier who closed it.")
            .WithDescription(
                "Refused for the cashier who closed the session (INV-CSH-04), refused twice, and refused where no "
                + "variance on the session's batch was ever beyond the configured threshold — the same number the "
                + "close asked a reason for (OD-24). Step-up and a reason.")
            .RequirePermission(BillingPermissions.ApproveReconciliation, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.CashierSession, "sessionId")
            .RequireStepUp()
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(ReconciliationHandler.ApprovedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
}
