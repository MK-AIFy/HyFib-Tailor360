using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>The sanctioned way for a module to declare what an endpoint demands.</summary>
public static class EndpointAuthorisationExtensions
{
    /// <summary>
    /// Requires a permission and a branch scope. Use this on every endpoint that touches branch-owned
    /// data; the branch requirement is what stops a permission from reaching across branches.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string permissionKey,
        BranchScope scope = BranchScope.CurrentBranch)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionKey);

        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new PermissionRequirement(permissionKey));
            policy.AddRequirements(new BranchScopeRequirement(scope));
        });

        builder.WithMetadata(new RequiredPermissionMetadata(permissionKey, scope));
        return builder;
    }

    /// <summary>
    /// Declares that this endpoint's invocation is written to the audit trail. Every state-changing
    /// endpoint must carry it (ARCH-008); reads carry it only when the read itself is sensitive, such as
    /// exporting personal data.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="action">The stable audit action name.</param>
    /// <param name="reasonRequired">True when the caller must supply a reason.</param>
    public static TBuilder Audited<TBuilder>(
        this TBuilder builder,
        string action,
        bool reasonRequired = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        builder.WithMetadata(new AuditedEndpointMetadata(action, reasonRequired));
        return builder;
    }

    /// <summary>
    /// Declares an endpoint deliberately reachable without a session, recording why and where the
    /// exposure was reviewed. Anything else without a policy fails the architecture test.
    /// </summary>
    public static TBuilder AllowAnonymousWithJustification<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedIn);

        builder.AllowAnonymous();
        builder.WithMetadata(new AnonymousJustificationMetadata(justification, reviewedIn));
        return builder;
    }
}

/// <summary>Records the permission and branch scope an endpoint demands, for tests and for the API inventory.</summary>
/// <param name="PermissionKey">The permission the endpoint requires.</param>
/// <param name="Scope">The branch reach the endpoint requires.</param>
public sealed record RequiredPermissionMetadata(string PermissionKey, BranchScope Scope);
