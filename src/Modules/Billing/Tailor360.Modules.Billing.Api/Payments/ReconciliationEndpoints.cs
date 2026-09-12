using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
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
/// closed it (INV-CSH-04). The batch itself is read back on the session's own payload — there is no
/// separate read route for it.
/// </summary>
internal static class ReconciliationEndpoints
{
    public static RouteGroupBuilder MapReconciliationEndpoints(this RouteGroupBuilder billing)
    {
        billing.MapPost("/cashier-sessions/{sessionId:guid}/reconciliation/approve", async Task<IResult> (
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

        return billing;
    }
}
