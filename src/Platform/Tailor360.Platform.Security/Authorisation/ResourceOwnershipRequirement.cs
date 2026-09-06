using Microsoft.AspNetCore.Authorization;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Requires that the resource is assigned to the caller, unless the caller holds one of the permissions
/// that supervise it.
/// </summary>
/// <remarks>
/// <para>
/// This is the requirement behind "a Tailor sees the jobs assigned to them, and not the rest of the
/// workshop's". Holding <c>orders.phase_transition</c> and working in the right branch is not enough on
/// a job somebody else is stitching; being the assignee is.
/// </para>
/// <para>
/// The supervising permissions are named by the endpoint rather than by a role, because roles are data
/// and a shop may rename or re-cut them. A Tailor Master holds <c>orders.assign</c> and therefore sees
/// every job on the board; an Owner holds
/// <see cref="BranchScopeAuthorisationHandler.OrganisationWidePermission"/> and therefore sees every
/// job everywhere. Neither is written into this class. Declaring at least one supervising permission is
/// checked by the endpoint inventory, because a requirement nobody can pass but the assignee would hide
/// an unassigned job from the person whose job it is to assign it.
/// </para>
/// </remarks>
/// <param name="supervisorPermissions">
/// Permissions whose holders see the resource whether or not it is assigned to them.
/// </param>
public sealed class ResourceOwnershipRequirement(IReadOnlyList<string> supervisorPermissions)
    : IAuthorizationRequirement
{
    /// <summary>Permissions whose holders are not limited to their own assignments.</summary>
    public IReadOnlyList<string> SupervisorPermissions { get; } = supervisorPermissions;
}
