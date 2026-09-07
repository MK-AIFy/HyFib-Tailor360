namespace Tailor360.Modules.Identity.Application.Access;

/// <summary>
/// Writes the reference data the Identity module owns: the system roles of
/// <see cref="SystemRoles"/> and the permissions they grant.
/// </summary>
/// <remarks>
/// Idempotent and safe to run against a live database. Running it twice changes nothing; running it
/// after a release that added a permission to a role's definition adds exactly that grant.
/// </remarks>
public interface IIdentityReferenceDataSeeder
{
    /// <summary>Creates or refreshes the system roles for an organisation.</summary>
    /// <param name="organisationId">The organisation whose roles are being seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RoleSeedOutcome> SeedSystemRolesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What one seeding run did, so that an operator has evidence rather than a promise.</summary>
/// <param name="RolesCreated">Roles that did not exist and were created.</param>
/// <param name="RolesUpdated">Roles whose name, description, reach or grants changed.</param>
/// <param name="RolesUnchanged">Roles already matching their definition.</param>
/// <param name="PermissionsGranted">Grants added across every role.</param>
/// <param name="PermissionsRevoked">Grants removed because a definition no longer names them.</param>
public sealed record RoleSeedOutcome(
    int RolesCreated,
    int RolesUpdated,
    int RolesUnchanged,
    int PermissionsGranted,
    int PermissionsRevoked);
