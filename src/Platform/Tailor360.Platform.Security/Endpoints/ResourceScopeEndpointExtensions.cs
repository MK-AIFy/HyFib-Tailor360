using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// The declaration verbs for endpoints whose authorisation depends on the row they name rather than on
/// the caller alone.
/// </summary>
/// <remarks>
/// They are additive to <see cref="EndpointAuthorisationExtensions.RequirePermission"/>, never a
/// replacement for it: an endpoint declares what the caller must hold, and then what must be true of the
/// thing they are asking for. The endpoint inventory refuses a resource declaration without a permission,
/// so neither verb can be used to reach a route the other would have closed.
/// </remarks>
public static class ResourceScopeEndpointExtensions
{
    /// <summary>
    /// Declares that this endpoint acts on a resource whose branch decides the answer, and that the
    /// resource is to be loaded before the authorisation handlers run.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="resourceKind">The kind, matching a registered <see cref="IResourceScopeResolver"/>.</param>
    /// <param name="routeValueName">The route parameter carrying the identifier, without braces.</param>
    /// <param name="scope">
    /// The same reach the endpoint declared with its permission. Repeating it is what lets an
    /// organisation-wide read reach a row in another branch while a write declared
    /// <see cref="BranchScope.CurrentBranch"/> still cannot.
    /// </param>
    public static TBuilder ScopedToResource<TBuilder>(
        this TBuilder builder,
        string resourceKind,
        string routeValueName,
        BranchScope scope = BranchScope.CurrentBranch)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeValueName);

        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new ResourceBranchRequirement(scope));
        });

        builder.WithMetadata(new ResourceScopeMetadata(resourceKind, routeValueName, scope));
        return builder;
    }

    /// <summary>
    /// Declares that this endpoint carries an identifier in its route and needs no resource scope,
    /// because nothing the route names belongs to a branch.
    /// </summary>
    /// <remarks>
    /// The exemption is deliberately noisy to write. A permissioned route with an identifier in it and
    /// no <see cref="ScopedToResource"/> takes no branch decision at all — the caller-side check asks
    /// only whether the caller's own active branch is one of their own assignments, which every signed-in
    /// person passes — so the omission is invisible in review and invisible in the matrix. Saying so
    /// out loud, with a reason and where it was reviewed, is the difference between a decision and an
    /// oversight.
    /// </remarks>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="justification">Why the route touches no branch-owned row.</param>
    /// <param name="reviewedIn">Where the exposure was reviewed — an issue number or a document path.</param>
    public static TBuilder TouchesNoBranchOwnedResource<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedIn);

        builder.WithMetadata(new UnscopedRouteMetadata(justification, reviewedIn));
        return builder;
    }

    /// <summary>
    /// Declares that only the resource's assignees may use this endpoint, and which permissions see past
    /// that. Requires <see cref="ScopedToResource"/> on the same endpoint, because assignment is a fact
    /// about the resource and there is nothing to read it from otherwise.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="supervisorPermissions">
    /// Permissions whose holders are not limited to their own assignments — the lead who assigns the
    /// work, and the reach that reads across branches. At least one is required.
    /// </param>
    public static TBuilder RequireAssignment<TBuilder>(
        this TBuilder builder,
        params string[] supervisorPermissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(supervisorPermissions);

        if (supervisorPermissions.Length == 0)
        {
            throw new ArgumentException(
                "An assignment requirement names at least one supervising permission, or nobody but the "
                + "assignee can ever see the resource — including the person whose job it is to assign it.",
                nameof(supervisorPermissions));
        }

        var permissions = supervisorPermissions.ToArray();

        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new ResourceOwnershipRequirement(permissions));
        });

        builder.WithMetadata(new ResourceOwnershipMetadata(permissions));
        return builder;
    }
}
