using Tailor360.Modules.Identity.Domain.Access;

namespace Tailor360.Modules.Identity.Application.Access;

/// <summary>
/// One role this release ships, with the permissions it grants by default.
/// </summary>
/// <remarks>
/// The definition is the seed, not the enforcement. <c>init-reference-data</c> writes these rows and
/// then the database is the authority: an administrator who removes a grant has removed it, and the
/// next seeding run does not put it back — it reconciles the role to its definition only for the
/// permissions the definition names, and the test in the unit tier is what keeps the definition equal
/// to the owner-approved matrix.
/// </remarks>
/// <param name="Key">The stable lower-case role key.</param>
/// <param name="Name">The name shown on screen. Matched case-insensitively by the multi-factor policy.</param>
/// <param name="Description">What the role is for, in operator language.</param>
/// <param name="Reach">How far the role is meant to reach.</param>
/// <param name="AssignedByDefault">True when an installation assigns the role at onboarding.</param>
/// <param name="Permissions">The permission keys the role grants by default.</param>
public sealed record SystemRoleDefinition(
    string Key,
    string Name,
    string Description,
    RoleReach Reach,
    bool AssignedByDefault,
    IReadOnlyList<string> Permissions);
