using Microsoft.AspNetCore.Authorization;
using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Requires that the resource the request names belongs to a branch the caller may act in.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of branch scoping that <see cref="BranchScopeRequirement"/> cannot do.
/// <see cref="BranchScopeRequirement"/> asks whether the caller is acting inside their own assignments,
/// which is a question about the caller alone; this one asks whether the row they named is inside them,
/// which cannot be answered without loading the row. Endpoints that take a resource identifier carry
/// both, and the two together are what the design means by "branch scope is evaluated, never inferred".
/// </para>
/// <para>
/// The declared <see cref="Scope"/> repeats the endpoint's own declaration on purpose, so that the check
/// against the resource can honour organisation-wide read reach: a cross-branch read declared
/// <see cref="BranchScope.Organisation"/> passes for a caller holding
/// <see cref="BranchScopeAuthorisationHandler.OrganisationWidePermission"/>, and a write declared
/// <see cref="BranchScope.CurrentBranch"/> does not, however wide that caller's reading reach is. The
/// contract test holds the two declarations equal so they cannot drift.
/// </para>
/// </remarks>
/// <param name="scope">The reach the endpoint declared alongside its permission.</param>
public sealed class ResourceBranchRequirement(BranchScope scope) : IAuthorizationRequirement
{
    /// <summary>The reach the endpoint declared.</summary>
    public BranchScope Scope { get; } = scope;
}
