using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain;
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

        users.MapPost("/{userId:guid}/suspend", async Task<IResult> (
                Guid userId,
                ReasonPayload request,
                HttpContext context,
                UserAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
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

                var result = await handler.SuspendAsync(
                    userId, reason, caller.UserId, cancellationToken);

                if (result.IsFailure)
                {
                    // A concurrent change between the precondition and the write is the same answer the
                    // precondition would have given, so the caller sees one behaviour rather than two.
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
            })
            .Produces<StaffUserPayload>(StatusCodes.Status200OK)
            .WithName("SuspendStaffUser")
            .WithSummary("Suspend a staff account and end its sessions.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(UserAdministrationHandler.SuspendedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return users;
    }
}
