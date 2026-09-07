using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// What one account may do, and where it may do it.
/// </summary>
/// <remarks>
/// Both replacements are conditional on the account's own version rather than on the assignment rows,
/// which carry none. That is not a shortcut: it is the only way two administrators editing the same
/// person's access at the same moment can be made to contend at all, and without it the second edit
/// would quietly discard the first.
/// </remarks>
public static class UserAccessEndpoints
{
    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "The route names a staff account, which belongs to the organisation rather than to a branch, "
        + "and the permission is organisation-scoped — so an administrator who holds it reaches every "
        + "account by construction and there is no narrower resource for a scope check to evaluate. "
        + "Which branches the account may work in is the subject of the change, not a check on it.";

    /// <summary>Maps the access endpoints.</summary>
    /// <param name="users">The <c>/api/v1/admin/users</c> group.</param>
    public static RouteGroupBuilder MapUserAccessEndpoints(this RouteGroupBuilder users)
    {
        ArgumentNullException.ThrowIfNull(users);

        users.MapGet("/{userId:guid}/access", async Task<IResult> (
                Guid userId,
                HttpContext context,
                UserAssignmentHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(userId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);

                return Results.Ok(AssignedAccessPayload.From(result.Value));
            })
            .Produces<AssignedAccessPayload>(StatusCodes.Status200OK)
            .WithName("GetStaffUserAccess")
            .WithSummary("Read the roles and branches one staff account holds.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        users.MapPut("/{userId:guid}/roles", async Task<IResult> (
                Guid userId,
                ReplaceRolesPayload request,
                HttpContext context,
                UserAssignmentHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await ApplyAsync(
                    userId,
                    request?.Reason,
                    context,
                    handler,
                    (reason, token) => handler.ReplaceRolesAsync(
                        userId, request?.RoleKeys ?? [], reason, caller.UserId, token),
                    cancellationToken))
            .Produces<AssignedAccessPayload>(StatusCodes.Status200OK)
            .WithName("ReplaceStaffUserRoles")
            .WithSummary("Replace the roles a staff account holds.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(UserAssignmentHandler.RolesReplacedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        users.MapPut("/{userId:guid}/branches", async Task<IResult> (
                Guid userId,
                ReplaceBranchesPayload request,
                HttpContext context,
                UserAssignmentHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await ApplyAsync(
                    userId,
                    request?.Reason,
                    context,
                    handler,
                    (reason, token) => handler.ReplaceBranchesAsync(
                        userId,
                        [.. (request?.Branches ?? []).Select(
                            branch => new BranchAssignment(branch.BranchId, branch.IsPrimary))],
                        reason,
                        caller.UserId,
                        token),
                    cancellationToken))
            .Produces<AssignedAccessPayload>(StatusCodes.Status200OK)
            .WithName("ReplaceStaffUserBranches")
            .WithSummary("Replace the branches a staff account works in.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Users, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(UserAssignmentHandler.BranchesReplacedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return users;
    }

    private static async Task<IResult> ApplyAsync(
        Guid userId,
        string? given,
        HttpContext context,
        UserAssignmentHandler handler,
        Func<string, CancellationToken, Task<Result<AssignedAccess>>> replace,
        CancellationToken cancellationToken)
    {
        if (given is null || string.IsNullOrWhiteSpace(given))
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

        var result = await replace(reason, cancellationToken);

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

        return Results.Ok(AssignedAccessPayload.From(result.Value));
    }
}
