using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Payments;

/// <summary>
/// The cashier session: opened with a float, closed against a count. One permission gates both,
/// <c>payments.session</c>, flagged for a second factor because the session is what the day's takings
/// reconcile to; the two audit actions are the ones the matrix names.
/// </summary>
internal static class CashierSessionEndpoints
{
    /// <summary>The most sessions a listing returns: a branch's recent history, not an archive.</summary>
    private const int ListLimit = 100;

    public static RouteGroupBuilder MapCashierSessionEndpoints(this RouteGroupBuilder billing)
    {
        MapList(billing);
        MapOpen(billing);
        MapRead(billing);
        MapClose(billing);

        return billing;
    }

    private static void MapList(RouteGroupBuilder billing)
        => billing.MapGet("/cashier-sessions", async Task<IResult> (
                HttpContext context,
                ICashierSessionStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                CashierSessionStatus? status = null;
                if (context.Request.Query.TryGetValue("status", out var wanted) && wanted.Count > 0)
                {
                    // By name only, as the invoice list does: Enum.TryParse would take "1" or "open,closed" as a status.
                    var name = Enum.GetNames<CashierSessionStatus>().FirstOrDefault(candidate => string.Equals(candidate, wanted[0], StringComparison.OrdinalIgnoreCase));
                    if (name is null)
                    {
                        return Problems.From(BillingErrors.Required("status"), context);
                    }

                    status = Enum.Parse<CashierSessionStatus>(name);
                }

                var sessions = await store.ListAsync(caller.Context.OrganisationId, branchId, status, ListLimit, cancellationToken);

                return Results.Ok(sessions.Select(CashierSessionPayload.From).ToArray());
            })
            .Produces<CashierSessionPayload[]>(StatusCodes.Status200OK)
            .WithName("ListCashierSessions")
            .WithSummary("List the branch's cashier sessions, newest first, optionally one status only.")
            .WithDescription("`status=open` is how a cashier finds the session they have open; at most one hundred are returned.")
            .RequirePermission(BillingPermissions.Session, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource("The branch is the caller's own, from the session; a listing names no resource.", "#161")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapOpen(RouteGroupBuilder billing)
        => billing.MapPost("/cashier-sessions", async Task<IResult> (
                OpenCashierSessionRequest? request,
                HttpContext context,
                CashierSessionHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (caller.Context.BranchId is not { } branchId)
                {
                    return Problems.From(BillingErrors.Required("branch"), context);
                }

                var result = await handler.OpenAsync(
                    new OpenCashierSessionCommand(caller.Context.OrganisationId, branchId, caller.UserId, request?.OpeningFloat ?? 0m),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Created($"/api/v1/billing/cashier-sessions/{result.Value.Session.Id}", CashierSessionPayload.From(result.Value.Session));
            })
            .Produces<CashierSessionPayload>(StatusCodes.Status201Created)
            .WithName("OpenCashierSession")
            .WithSummary("Open a cashier session at the caller's branch with the float put in the drawer.")
            .WithDescription(
                "One open session per cashier per branch: a second is refused with 409. Until it is closed, "
                + "every payment the cashier records is recorded in it.")
            .RequirePermission(BillingPermissions.Session, BranchScope.CurrentBranch)
            .TouchesNoBranchOwnedResource("The session is created at the caller's own branch; there is no resource yet.", "#161")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CashierSessionHandler.OpenedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

    private static void MapRead(RouteGroupBuilder billing)
        => billing.MapGet("/cashier-sessions/{sessionId:guid}", async Task<IResult> (
                Guid sessionId,
                HttpContext context,
                ICashierSessionStore store,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var session = await store.FindAsync(sessionId, caller.Context.OrganisationId, cancellationToken);
                if (session is null)
                {
                    return Problems.From(BillingErrors.CashierSessionNotFound, context);
                }

                context.Response.SetEntityTag(store.EntityTagOf(session));

                return Results.Ok(CashierSessionPayload.From(session));
            })
            .Produces<CashierSessionPayload>(StatusCodes.Status200OK)
            .WithName("GetCashierSession")
            .WithSummary("Read one cashier session with its count sheet, once it has one.")
            .WithDescription("A session at another branch reads as 404.")
            .RequirePermission(BillingPermissions.Session, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.CashierSession, "sessionId")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithRequestTimeout(RequestTimeoutPolicies.Read);

    private static void MapClose(RouteGroupBuilder billing)
        => billing.MapPost("/cashier-sessions/{sessionId:guid}/close", async Task<IResult> (
                Guid sessionId,
                CloseCashierSessionRequest? request,
                HttpContext context,
                CashierSessionHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CloseAsync(
                    new CloseCashierSessionCommand(
                        sessionId,
                        caller.Context.OrganisationId,
                        (request?.Denominations ?? []).Select(line => new DenominationCount(line.Denomination, line.Quantity)).ToList(),
                        (request?.ModeTotals ?? []).Select(line => new ModeCount(line.ModeCode ?? string.Empty, line.Counted)).ToList(),
                        request?.Reason,
                        caller.UserId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Tag);

                return Results.Ok(CashierSessionPayload.From(result.Value.Session));
            })
            .Produces<CashierSessionPayload>(StatusCodes.Status200OK)
            .WithName("CloseCashierSession")
            .WithSummary("Close a cashier session against its denomination count sheet and the counted totals by mode.")
            .WithDescription(
                "Only the cashier who opened it closes it. The expected total per mode is computed from the session's "
                + "own records, the float counted into cash; the cash counted is the sheet's sum. A variance beyond the "
                + "configured threshold needs a reason. No If-Match: the close is a conditional update, so a second "
                + "close of the same session is a 409, never a second record.")
            .RequirePermission(BillingPermissions.Session, BranchScope.CurrentBranch)
            .ScopedToResource(BillingResourceKinds.CashierSession, "sessionId")
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CashierSessionHandler.ClosedAction)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);
}
