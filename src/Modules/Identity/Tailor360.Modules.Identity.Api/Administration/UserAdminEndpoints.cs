using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// Administering staff accounts: who may sign in, and on what standing.
/// </summary>
/// <remarks>
/// <para>
/// This is the first permissioned surface in the application, so the declarations on each route are
/// worth reading as a set rather than as boilerplate. <c>admin.users</c> is catalogued as requiring a
/// second factor, a recent re-authentication and a reason, and each of those three obligations shows up
/// here as something the route declares: <c>RequireStepUp</c>, and a reason field the handler refuses
/// to proceed without. A permission that demands a reason and a route that never asks for one would be
/// a control that exists only on paper, which is why the two are reconciled by a test rather than by
/// convention.
/// </para>
/// <para>
/// <c>If-Match</c> is required on the mutation because an administrator works from a list that may be
/// minutes old, and the account they are looking at may have been changed by somebody else in the
/// meantime. Refusing a stale edit and showing them the current state is the difference between two
/// administrators cooperating and one of them silently undoing the other.
/// </para>
/// <para>
/// The route parameter names an account, and the permission is organisation-scoped, so there is no
/// branch-owned resource to scope to: every account in the organisation is within an administrator's
/// reach by construction. That is what the recorded exposure below says, rather than leaving ARCH-023
/// to guess.
/// </para>
/// <para>
/// <b>The read demands step-up as well, and that is not an oversight.</b> Step-up is a property of the
/// permission rather than of the verb, and <c>admin.users</c> carries it — so an administrator
/// re-authenticates to open the screen, not only to change something on it. That is defensible: this
/// screen shows every account in the organisation and their standing, and an unlocked machine left at a
/// counter is the threat the freshness window exists for. It is also a cost, paid once per window
/// rather than per action. Splitting the permission into a read and a write is the alternative, and it
/// is a change to the approved catalogue and to the role grants — a decision for the security review
/// rather than for this route, which is why this follows the catalogue instead of quietly diverging
/// from it.
/// </para>
/// </remarks>
public static class UserAdminEndpoints
{
    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "The route names a staff account, which belongs to the organisation rather than to a branch: "
        + "an account can be assigned to several branches at once and to none. The permission is "
        + "organisation-scoped, so an administrator who holds it reaches every account by construction "
        + "and there is no narrower resource for a scope check to evaluate.";

    /// <summary>Maps the staff-account administration endpoints.</summary>
    /// <param name="users">The <c>/api/v1/admin/users</c> group.</param>
    public static RouteGroupBuilder MapUserAdminEndpoints(this RouteGroupBuilder users)
    {
        ArgumentNullException.ThrowIfNull(users);

        users.MapGet("/{userId:guid}", async Task<IResult> (
                Guid userId,
                HttpContext context,
                UserAdministrationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(userId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                // The tag the caller must present to change this account. Sending it on the read is what
                // makes the write conditional on the state they actually saw.
                context.Response.SetEntityTag(result.Value.Version);

                // The account carries a sign-in name and an address, which are personal data shown to an
                // administrator and to nobody else. A shared counter device must not keep a copy.
                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(StaffUserPayload.From(result.Value));
            })
            .Produces<StaffUserPayload>(StatusCodes.Status200OK)
            .WithName("GetStaffUser")
            .WithSummary("Read one staff account for administration.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        users.MapGet("/", async Task<IResult> (
                HttpContext context,
                IStaffDirectory directory,
                ICurrentUser caller,
                CancellationToken cancellationToken,
                string? status = null,
                string? role = null,
                Guid? branch = null,
                string? q = null,
                string? cursor = null,
                int limit = StaffQuery.DefaultLimit) =>
            {
                if (status is { Length: > 0 } && !Enum.TryParse<UserStatus>(status, ignoreCase: true, out _))
                {
                    return Problems.From(IdentityApiErrors.StatusNotRecognised, context);
                }

                var page = await directory.SearchAsync(
                    new StaffQuery(
                        caller.Context.OrganisationId,
                        status is { Length: > 0 } ? Enum.Parse<UserStatus>(status, ignoreCase: true) : null,
                        role,
                        branch,
                        q,
                        cursor,
                        limit),
                    cancellationToken);

                // Names and standing for every member of staff, which is personal data shown to an
                // administrator and to nobody else. A shared counter device must not keep a copy.
                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(StaffPagePayload.From(page));
            })
            .Produces<StaffPagePayload>(StatusCodes.Status200OK)
            .WithName("ListStaffUsers")
            .WithSummary("List staff accounts, filtered and paged.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        // Every command on this surface is the same shape — a reason, a precondition, a domain
        // transition and an audit entry — so they are mapped from one place. A route added by hand
        // would be one missing .RequireStepUp() or one missing .Audited(...) away from being a hole
        // that no reviewer would see, and this way the declarations cannot drift apart.
        foreach (var command in Commands)
        {
            users.MapPost($"/{{userId:guid}}/{command.Segment}", async Task<IResult> (
                    Guid userId,
                    ReasonPayload request,
                    HttpContext context,
                    UserAdministrationHandler handler,
                    ICurrentUser caller,
                    CancellationToken cancellationToken) =>
                    await ApplyAsync(userId, request, context, handler, caller, command, cancellationToken))
                .Produces<StaffUserPayload>(StatusCodes.Status200OK)
                .WithName(command.OperationId)
                .WithSummary(command.Summary)
                .WithTags(IdentityRoutes.AdminTag)
                .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
                .RequireStepUp()
                .TouchesNoBranchOwnedResource(NoBranchResource, Review)
                .RequireRateLimiting(RateLimitPolicyNames.Write)
                .Audited(command.AuditAction, reasonRequired: true)
                .RequireIdempotency()
                .RequireIfMatch()
                .WithRequestTimeout(RequestTimeoutPolicies.Command);
        }

        return users;
    }

    /// <summary>
    /// Reads the account, checks the precondition, applies the command and answers.
    /// </summary>
    /// <remarks>
    /// The read before the precondition is what makes <c>If-Match</c> mean anything: the version the
    /// caller presented is compared against the version the row carries now, not against the one they
    /// were sent. A change slipping in between that check and the write is answered the same way,
    /// because to the caller the two are the same event.
    /// </remarks>
    private static async Task<IResult> ApplyAsync(
        Guid userId,
        ReasonPayload request,
        HttpContext context,
        UserAdministrationHandler handler,
        ICurrentUser caller,
        AdminCommand command,
        CancellationToken cancellationToken)
    {
        if (request?.Reason is not { } given || string.IsNullOrWhiteSpace(given))
        {
            return Problems.From(IdentityApiErrors.ReasonRequired, context);
        }

        var reason = given.Trim();

        if (reason.Length > AdminRequests.MaximumReasonLength)
        {
            return Problems.From(IdentityApiErrors.ReasonTooLong, context);
        }

        var current = await handler.ReadAsync(userId, cancellationToken);
        if (current.IsFailure)
        {
            return Problems.From(current.Error, context);
        }

        var precondition = ConcurrencyResults.CheckIfMatch(
            context,
            current.Value.Version,
            AdminRequests.VersionConflict,
            AdminRequests.VersionConflictDetail);

        if (precondition is not null)
        {
            return precondition;
        }

        var result = await command.Apply(handler, userId, reason, caller.UserId, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.ConcurrentChange
                ? ConcurrencyResults.VersionConflict(
                    context,
                    AdminRequests.VersionConflict,
                    AdminRequests.VersionConflictDetail,
                    current.Value.Version)
                : Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Version);

        return Results.Ok(StaffUserPayload.From(result.Value));
    }

    /// <summary>One administrative command, and everything the route needs to declare about it.</summary>
    /// <param name="Segment">The path segment after the account identifier.</param>
    /// <param name="OperationId">The published operation name.</param>
    /// <param name="AuditAction">The stable audit action the endpoint declares and the handler writes.</param>
    /// <param name="Summary">What the operation does, for the API document.</param>
    /// <param name="Apply">The handler method that performs it.</param>
    private sealed record AdminCommand(
        string Segment,
        string OperationId,
        string AuditAction,
        string Summary,
        Func<UserAdministrationHandler, Guid, string, Guid, CancellationToken, Task<Result<AdministeredUser>>> Apply);

    private static IReadOnlyList<AdminCommand> Commands { get; } =
    [
        new("suspend", "SuspendStaffUser", UserAdministrationHandler.SuspendedAction,
            "Suspend a staff account and end its sessions.",
            (handler, id, reason, actor, token) => handler.SuspendAsync(id, reason, actor, token)),
        new("reinstate", "ReinstateStaffUser", UserAdministrationHandler.ReinstatedAction,
            "Lift a suspension so the account may sign in again.",
            (handler, id, reason, actor, token) => handler.ReinstateAsync(id, reason, actor, token)),
        new("deactivate", "DeactivateStaffUser", UserAdministrationHandler.DeactivatedAction,
            "Close a staff account, ending its sessions and remembered devices.",
            (handler, id, reason, actor, token) => handler.DeactivateAsync(id, reason, actor, token)),
        new("reactivate", "ReactivateStaffUser", UserAdministrationHandler.ReactivatedAction,
            "Reopen a closed account as an invitation, with no password and no second factor.",
            (handler, id, reason, actor, token) => handler.ReactivateAsync(id, reason, actor, token)),
        new("reset-mfa", "ResetStaffUserMfa", UserAdministrationHandler.MfaResetAction,
            "Clear the account's second factor and end its sessions.",
            (handler, id, reason, actor, token) => handler.ResetMfaAsync(id, reason, actor, token)),
        new("revoke-sessions", "RevokeStaffUserSessions", UserAdministrationHandler.SessionsRevokedAction,
            "End every session the account holds, leaving its standing unchanged.",
            (handler, id, reason, actor, token) => handler.RevokeSessionsAsync(id, reason, actor, token)),
    ];
}
