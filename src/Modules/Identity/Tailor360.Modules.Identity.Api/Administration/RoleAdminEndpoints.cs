using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// The role register: which permissions each role bundles, and the custom roles a shop adds.
/// </summary>
/// <remarks>
/// <para>
/// The screens behind <c>admin.roles</c>. Everything else on this surface changes who holds what;
/// this changes what holding it <em>means</em>, which is why <c>docs/security/permission-matrix.md</c>
/// calls it "the one action that must never be possible from a merely-remembered session" and why
/// every route here declares step-up.
/// </para>
/// <para>
/// <b>The permission catalogue is published as a read.</b> A screen that offers checkboxes has to know
/// what the boxes are, what each one allows in operator language, and which of them carry the second
/// factor, the step-up and the reason — otherwise an administrator is picking from a list of dotted
/// keys and guessing. The catalogue is code and the same list the endpoints enforce against, so
/// publishing it cannot drift from what a grant will be checked against.
/// </para>
/// <para>
/// <b>There is no route that edits a permission.</b> Roles are data; the catalogue is not. An
/// administrator re-cuts which permissions a role bundles and cannot invent one, which is the line the
/// whole authorisation model rests on.
/// </para>
/// </remarks>
public static class RoleAdminEndpoints
{
    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "A role is organisation-wide configuration. It belongs to no branch, grants nothing that is "
        + "narrowed by the branch it was edited from, and the route parameter is the role's own "
        + "identifier — so there is no branch-owned row for a resource scope to be evaluated against. "
        + "The organisation reach the permission declares is the whole of the decision.";

    /// <summary>Maps the role endpoints.</summary>
    /// <param name="roles">The <c>/api/v1/admin/roles</c> group.</param>
    public static RouteGroupBuilder MapRoleAdminEndpoints(this RouteGroupBuilder roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        roles.MapGet("/", async Task<IResult> (
                RoleAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                Results.Ok((await handler.ListAsync(caller.Context.OrganisationId, cancellationToken))
                    .Select(RolePayload.From).ToArray()))
            .Produces<IReadOnlyList<RolePayload>>(StatusCodes.Status200OK)
            .WithName("ListRoles")
            .WithSummary("List the organisation's roles with their grants and holder counts.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        roles.MapGet("/{roleId:guid}", async Task<IResult> (
                Guid roleId,
                HttpContext context,
                RoleAdministrationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReadAsync(roleId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);

                return Results.Ok(RolePayload.From(result.Value));
            })
            .Produces<RolePayload>(StatusCodes.Status200OK)
            .WithName("GetRole")
            .WithSummary("Read one role, with the version an edit must be made against.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        roles.MapPost("/", async Task<IResult> (
                DefineRolePayload request,
                HttpContext context,
                RoleAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (Reason.Of(request?.Reason, context) is { } refusal)
                {
                    return refusal;
                }

                if (!Enum.TryParse<RoleReach>(request!.Reach, ignoreCase: true, out var reach))
                {
                    return Problems.From(IdentityApiErrors.RoleReachNotRecognised, context);
                }

                var result = await handler.DefineAsync(
                    caller.Context.OrganisationId,
                    request.Key,
                    request.Name,
                    request.Description,
                    reach,
                    request.Reason!.Trim(),
                    caller.UserId,
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                context.Response.SetEntityTag(result.Value.Version);

                return Results.Created(
                    $"{IdentityRoutes.AdminRoles}/{result.Value.RoleId}", RolePayload.From(result.Value));
            })
            .Produces<RolePayload>(StatusCodes.Status201Created)
            .WithName("DefineRole")
            .WithSummary("Define a custom role, which starts granting nothing.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RoleAdministrationHandler.DefinedAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        roles.MapPut("/{roleId:guid}", async Task<IResult> (
                Guid roleId,
                DescribeRolePayload request,
                HttpContext context,
                RoleAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (Reason.Of(request?.Reason, context) is { } refusal)
                {
                    return refusal;
                }

                var (refused, current) =
                    await Precondition.CheckAsync(roleId, context, handler, cancellationToken);

                if (refused is not null)
                {
                    return refused;
                }

                var result = await handler.DescribeAsync(
                    roleId, request!.Name, request.Description, request.Reason!.Trim(),
                    caller.UserId, cancellationToken);

                return Answer(result, context, current);
            })
            .Produces<RolePayload>(StatusCodes.Status200OK)
            .WithName("DescribeRole")
            .WithSummary("Rename a role and rewrite its description. The key never changes.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RoleAdministrationHandler.DescribedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        roles.MapPut("/{roleId:guid}/permissions", async Task<IResult> (
                Guid roleId,
                ReplaceRolePermissionsPayload request,
                HttpContext context,
                RoleAdministrationHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (Reason.Of(request?.Reason, context) is { } refusal)
                {
                    return refusal;
                }

                var (refused, current) =
                    await Precondition.CheckAsync(roleId, context, handler, cancellationToken);

                if (refused is not null)
                {
                    return refused;
                }

                var result = await handler.ReplacePermissionsAsync(
                    roleId,
                    request!.PermissionKeys ?? [],
                    request.Reason!.Trim(),
                    caller.UserId,
                    cancellationToken);

                return Answer(result, context, current);
            })
            .Produces<RolePayload>(StatusCodes.Status200OK)
            .WithName("ReplaceRolePermissions")
            .WithSummary("Replace what a role grants with exactly the permissions given.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RoleAdministrationHandler.PermissionsReplacedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        roles.MapPost("/{roleId:guid}/delete", async Task<IResult> (
                Guid roleId,
                ReasonPayload request,
                HttpContext context,
                RoleAdministrationHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (Reason.Of(request?.Reason, context) is { } refusal)
                {
                    return refusal;
                }

                var (refused, current) =
                    await Precondition.CheckAsync(roleId, context, handler, cancellationToken);

                if (refused is not null)
                {
                    return refused;
                }

                var result = await handler.DeleteAsync(roleId, request!.Reason!.Trim(), cancellationToken);

                if (result.IsFailure)
                {
                    return result.Error == IdentityErrors.ConcurrentChange
                        ? ConcurrencyResults.VersionConflict(
                            context,
                            AdminRequests.VersionConflict,
                            AdminRequests.VersionConflictDetail,
                            current)
                        : Problems.From(result.Error, context);
                }

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithName("DeleteRole")
            // A POST to a /delete segment rather than a DELETE verb, because the operation carries a
            // body: it demands a written reason, and a reason sent as a query parameter would end up in
            // the access log of every proxy between the browser and the server.
            .WithSummary("Delete a custom role that nobody holds. A system role is refused.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(RoleAdministrationHandler.DeletedAction, reasonRequired: true)
            .RequireIdempotency()
            .RequireIfMatch()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return roles;
    }

    /// <summary>Maps the permission catalogue, which the role screens pick from.</summary>
    /// <param name="permissions">The <c>/api/v1/admin/permissions</c> group.</param>
    public static RouteGroupBuilder MapPermissionCatalogueEndpoints(this RouteGroupBuilder permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        permissions.MapGet("/", (PermissionCatalogue catalogue) =>
                Results.Ok(catalogue.All.Select(PermissionPayload.From).ToArray()))
            .Produces<IReadOnlyList<PermissionPayload>>(StatusCodes.Status200OK)
            .WithName("ListPermissions")
            .WithSummary("List every permission the application declares, with its flags.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(IdentityPermissions.Roles, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(
                "The catalogue is the application's own list of permissions. It names no row at all, "
                + "belongs to no branch, and the route carries no parameter.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        return permissions;
    }

    private static IResult Answer(
        Tailor360.Platform.Abstractions.Results.Result<AdministeredRole> result,
        HttpContext context,
        EntityTag current)
    {
        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.ConcurrentChange
                ? ConcurrencyResults.VersionConflict(
                    context,
                    AdminRequests.VersionConflict,
                    AdminRequests.VersionConflictDetail,
                    current)
                : Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Version);

        return Results.Ok(RolePayload.From(result.Value));
    }

    /// <summary>Reads the role and compares the version the caller presented against the one it carries.</summary>
    /// <remarks>
    /// A read before the write, for the reason <c>UserAdminEndpoints</c> gives: the precondition is
    /// meaningful only against the version the row has now, not the one the caller was sent.
    /// </remarks>
    private static class Precondition
    {
        public static async Task<(IResult? Refused, EntityTag Current)> CheckAsync(
            Guid roleId,
            HttpContext context,
            RoleAdministrationHandler handler,
            CancellationToken cancellationToken)
        {
            var role = await handler.ReadAsync(roleId, cancellationToken);

            if (role.IsFailure)
            {
                return (Problems.From(role.Error, context), default);
            }

            var refused = ConcurrencyResults.CheckIfMatch(
                context,
                role.Value.Version,
                AdminRequests.VersionConflict,
                AdminRequests.VersionConflictDetail);

            return (refused, role.Value.Version);
        }
    }

    /// <summary>Validates the written reason every command on this surface demands.</summary>
    private static class Reason
    {
        public static IResult? Of(string? given, HttpContext context)
        {
            if (given is null || string.IsNullOrWhiteSpace(given))
            {
                return Problems.From(IdentityApiErrors.ReasonRequired, context);
            }

            return given.Trim().Length > AdminRequests.MaximumReasonLength
                ? Problems.From(IdentityApiErrors.ReasonTooLong, context)
                : null;
        }
    }
}
