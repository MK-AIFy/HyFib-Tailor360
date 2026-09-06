namespace Tailor360.Modules.Identity.Domain.Access;

/// <summary>
/// How far a role is meant to reach. This is a description of intent for the administration screens
/// and the permission matrix; the reach a request actually gets is decided by the caller's branch
/// assignments and by whether they hold the organisation-wide read permission, never by this value.
/// </summary>
public enum RoleReach
{
    /// <summary>The role works inside the branches its holder is assigned to.</summary>
    Branch = 0,

    /// <summary>The role is meant to see the whole organisation, and is granted the reach to do so.</summary>
    Organisation = 1,
}
