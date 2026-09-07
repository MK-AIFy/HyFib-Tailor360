using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.FeatureFlags;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// Feature flags and the module toggles built on them.
/// </summary>
/// <remarks>
/// <para>
/// The rows belong to Platform and are reached through <see cref="IFeatureFlagAdministration"/> rather
/// than by mapping <c>platform.feature_flags</c> from this module — which is what module-ownership
/// prescribes and what keeps ARCH-005 true. Identity publishes the screens because administration is
/// one surface to the person using it, not because it owns the table.
/// </para>
/// <para>
/// A module toggle is a feature flag under a reserved <c>module.</c> prefix rather than a second store.
/// "Is the inventory module switched on" and "is this feature switched on" are the same question about
/// the same kind of value, and giving them separate tables would mean two propagation rules, two
/// caches and two places to look when something is unexpectedly off.
/// </para>
/// <para>
/// These are the endpoints that made <see cref="BranchScope.NotBranchOwned"/> necessary. The vendor
/// super-user role holds <c>admin.feature_flags</c> and nothing else, deliberately, so declaring
/// organisation reach here would have made the one role approved to change a flag unable to reach the
/// endpoint that changes it.
/// </para>
/// </remarks>
public static class FeatureFlagAdminEndpoints
{
    /// <summary>The prefix a module toggle's key carries.</summary>
    public const string ModuleKeyPrefix = "module.";

    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "A feature flag is a setting for the whole installation. It names no branch, is owned by no "
        + "branch, and the route parameter is its key rather than an identifier of anybody's row — so "
        + "there is nothing for a resource scope to be evaluated against.";

    /// <summary>Maps the flag endpoints.</summary>
    /// <param name="flags">The <c>/api/v1/admin/feature-flags</c> group.</param>
    public static RouteGroupBuilder MapFeatureFlagAdminEndpoints(this RouteGroupBuilder flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        flags.MapGet("/", async Task<IResult> (
                IFeatureFlagAdministration administration,
                CancellationToken cancellationToken) =>
                Results.Ok((await administration.ListAsync(cancellationToken))
                    .Select(FeatureFlagPayload.From).ToArray()))
            .Produces<IReadOnlyList<FeatureFlagPayload>>(StatusCodes.Status200OK)
            .WithName("ListFeatureFlags")
            .WithSummary("List the configured feature flags and module toggles.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.FeatureFlags, BranchScope.NotBranchOwned)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        flags.MapGet("/{key}", async Task<IResult> (
                string key,
                HttpContext context,
                IFeatureFlagAdministration administration,
                CancellationToken cancellationToken) =>
            {
                var flag = await administration.FindAsync(key, cancellationToken);

                if (flag is null)
                {
                    return Problems.From(FeatureFlagErrors.NotConfigured(key), context);
                }

                context.Response.SetEntityTag(flag.Version);

                return Results.Ok(FeatureFlagPayload.From(flag));
            })
            .Produces<FeatureFlagPayload>(StatusCodes.Status200OK)
            .WithName("GetFeatureFlag")
            .WithSummary("Read one feature flag, with the version an edit must be made against.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.FeatureFlags, BranchScope.NotBranchOwned)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        flags.MapPut("/{key}", async Task<IResult> (
                string key,
                SetFeatureFlagPayload request,
                HttpContext context,
                IFeatureFlagAdministration administration,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
                await SetAsync(key, request, context, administration, caller, cancellationToken))
            .Produces<FeatureFlagPayload>(StatusCodes.Status200OK)
            .WithName("SetFeatureFlag")
            .WithSummary("Turn a feature flag on or off for the organisation.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.FeatureFlags, BranchScope.NotBranchOwned)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(FlagSetAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        flags.MapPut("/modules/{code}", async Task<IResult> (
                string code,
                SetFeatureFlagPayload request,
                HttpContext context,
                IFeatureFlagAdministration administration,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (!PermissionModules.All.Contains(code, StringComparer.Ordinal))
                {
                    return Problems.From(IdentityApiErrors.ModuleNotRecognised, context);
                }

                return await SetAsync(
                    $"{ModuleKeyPrefix}{code}", request, context, administration, caller, cancellationToken);
            })
            .Produces<FeatureFlagPayload>(StatusCodes.Status200OK)
            .WithName("SetModuleEnabled")
            .WithSummary("Switch a module and its menu entries on or off.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.FeatureFlags, BranchScope.NotBranchOwned)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(ModuleToggledAction, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return flags;
    }

    /// <summary>The audit action recorded when a flag is set.</summary>
    public const string FlagSetAction = "platform.feature-flag.set";

    /// <summary>The audit action recorded when a module is switched on or off.</summary>
    public const string ModuleToggledAction = "platform.module.toggled";

    private static async Task<IResult> SetAsync(
        string key,
        SetFeatureFlagPayload? request,
        HttpContext context,
        IFeatureFlagAdministration administration,
        ICurrentUser caller,
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

        // A flag that has never been configured has no version, so the precondition is required only
        // once there is something to have changed. Demanding one for a first configuration would ask
        // the administrator for a value that does not exist.
        EntityTag? expected = null;

        if (context.Request.Headers.IfMatch.ToString() is { Length: > 0 } header)
        {
            if (!EntityTag.TryParse(header, out var presented))
            {
                return ConcurrencyResults.PreconditionMissing(context);
            }

            expected = presented;
        }

        var result = await administration.SetAsync(
            key, request.Enabled, reason, expected, caller.UserId, cancellationToken);

        if (result.IsFailure)
        {
            return Problems.From(result.Error, context);
        }

        context.Response.SetEntityTag(result.Value.Version);

        return Results.Ok(FeatureFlagPayload.From(result.Value));
    }
}
