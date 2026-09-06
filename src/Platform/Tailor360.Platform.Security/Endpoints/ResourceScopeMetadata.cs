using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Records that an endpoint's authorisation is decided against a resource, which resource, and where in
/// the route its identifier is found.
/// </summary>
/// <param name="ResourceKind">The kind, matching an <c>IResourceScopeResolver</c> the host registered.</param>
/// <param name="RouteValueName">The route parameter carrying the identifier, without braces.</param>
/// <param name="Scope">
/// The reach the endpoint declared alongside its permission. It is repeated here so the resource check
/// can honour organisation-wide read reach; the endpoint inventory holds the two equal.
/// </param>
public sealed record ResourceScopeMetadata(string ResourceKind, string RouteValueName, BranchScope Scope);

/// <summary>
/// Records that an endpoint is limited to the resource's assignees, and which permissions see past that.
/// </summary>
/// <param name="SupervisorPermissions">Permissions whose holders are not limited to their own assignments.</param>
public sealed record ResourceOwnershipMetadata(IReadOnlyList<string> SupervisorPermissions);

/// <summary>
/// Records that an endpoint carries an identifier in its route and deliberately declares no resource
/// scope, because nothing the route names belongs to a branch.
/// </summary>
/// <remarks>
/// It exists so that "this route has an identifier and no branch check" is a decision somebody wrote
/// down rather than something nobody noticed. Without it, omitting <c>ScopedToResource</c> silently
/// disables the branch check on a permissioned route — the endpoint still declares a policy, still
/// passes ARCH-007 and ARCH-008, and still reconciles against a matrix row, while the row's branch is
/// never loaded.
/// </remarks>
/// <param name="Justification">Why the route touches no branch-owned row.</param>
/// <param name="ReviewedIn">Where the exposure was reviewed — an issue number or a document path.</param>
public sealed record UnscopedRouteMetadata(string Justification, string ReviewedIn);
