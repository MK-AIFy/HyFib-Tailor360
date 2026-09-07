using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Infrastructure.Access;

/// <summary>
/// Creates or refreshes the system roles and their default grants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent by reconciliation, not by "insert if absent".</b> Each run brings every system role to
/// exactly its definition: it creates the role if it is missing, corrects the name, description, reach
/// and onboarding flag if they drifted, and replaces the grants with the definition's set. A release
/// that adds a permission to Reception therefore grants it on the next run, and a release that takes
/// one away removes it — which is the half an "insert if absent" seeder gets wrong, leaving a grant in
/// the database that nobody approved and no document records.
/// </para>
/// <para>
/// <b>Custom roles are never touched.</b> Reconciliation is by key against
/// <see cref="SystemRoles.All"/>; a role an administrator created is not in that list and is left
/// exactly as it is, grants included.
/// </para>
/// <para>
/// <b>The catalogue is checked before anything is written.</b> A definition naming a permission the
/// application does not declare fails the whole run, because a grant of a permission that does not
/// exist is a grant nobody can ever satisfy and a typo nobody would notice.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="catalogue">The permission catalogue every grant is validated against.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class IdentityReferenceDataSeeder(
    IdentityDbContext context,
    PermissionCatalogue catalogue,
    IClock clock,
    IIdGenerator ids) : IIdentityReferenceDataSeeder
{
    /// <inheritdoc />
    public async Task<RoleSeedOutcome> SeedSystemRolesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (organisationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reference data is seeded for a named organisation.", nameof(organisationId));
        }

        EnsureEveryGrantIsCatalogued();

        var now = clock.UtcNow;
        var keys = SystemRoles.All.Select(definition => definition.Key).ToArray();

        var existing = await context.Roles
            .Include(role => role.Permissions)
            .Where(role => role.OrganisationId == organisationId && keys.Contains(role.Key))
            .ToDictionaryAsync(role => role.Key, StringComparer.Ordinal, cancellationToken);

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var granted = 0;
        var revoked = 0;

        foreach (var definition in SystemRoles.All)
        {
            if (!existing.TryGetValue(definition.Key, out var role))
            {
                role = Define(definition, organisationId, now);
                context.Roles.Add(role);

                var seeded = role.ReplacePermissions(definition.Permissions, now, by: null);
                if (seeded.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Role '{definition.Key}' could not be seeded: {seeded.Error.Message}");
                }

                created++;
                granted += definition.Permissions.Count;
                continue;
            }

            if (!role.IsSystem)
            {
                // A role an administrator created under a key this release ships is a collision, not a
                // role to reconcile. Overwriting it would silently replace somebody's grants; the fix
                // is a human renaming one of the two.
                throw new InvalidOperationException(
                    $"A role with the key '{definition.Key}' exists in this organisation and is not a "
                    + "system role. Rename it before seeding reference data again.");
            }

            var before = role.PermissionKeys.ToHashSet(StringComparer.Ordinal);
            var changed = Reconcile(role, definition, now);

            var replaced = role.ReplacePermissions(definition.Permissions, now, by: null);
            if (replaced.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Role '{definition.Key}' could not be reconciled: {replaced.Error.Message}");
            }

            if (replaced.Value)
            {
                var after = role.PermissionKeys.ToHashSet(StringComparer.Ordinal);
                granted += after.Except(before, StringComparer.Ordinal).Count();
                revoked += before.Except(after, StringComparer.Ordinal).Count();
                changed = true;
            }

            if (changed)
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new RoleSeedOutcome(created, updated, unchanged, granted, revoked);
    }

    private void EnsureEveryGrantIsCatalogued()
    {
        var unknown = SystemRoles.All
            .SelectMany(definition => definition.Permissions.Select(key => (definition.Key, Permission: key)))
            .Where(grant => !catalogue.Contains(grant.Permission))
            .ToArray();

        if (unknown.Length == 0)
        {
            return;
        }

        var detail = string.Join(
            ", ", unknown.Select(grant => $"{grant.Key} → {grant.Permission}"));

        throw new InvalidOperationException(
            "A default role grant names a permission the catalogue does not declare: " + detail
            + ". " + IdentityErrors.PermissionNotInCatalogue(unknown[0].Permission).Message);
    }

    private Role Define(SystemRoleDefinition definition, Guid organisationId, DateTimeOffset now)
    {
        var defined = Role.Define(
            ids.NewId(),
            organisationId,
            definition.Key,
            definition.Name,
            definition.Description,
            definition.Reach,
            now,
            isSystem: true,
            assignedByDefault: definition.AssignedByDefault);

        return defined.IsFailure
            ? throw new InvalidOperationException(
                $"Role '{definition.Key}' is not a definable role: {defined.Error.Message}")
            : defined.Value;
    }

    private static bool Reconcile(Role role, SystemRoleDefinition definition, DateTimeOffset now)
    {
        var changed = false;

        if (!string.Equals(role.Name, definition.Name, StringComparison.Ordinal)
            || !string.Equals(role.Description, definition.Description, StringComparison.Ordinal))
        {
            var described = role.Describe(definition.Name, definition.Description, now, by: null);
            if (described.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Role '{definition.Key}' could not be described: {described.Error.Message}");
            }

            changed = true;
        }

        if (role.Reach != definition.Reach)
        {
            role.SetReach(definition.Reach, now, by: null);
            changed = true;
        }

        if (role.AssignedByDefault != definition.AssignedByDefault)
        {
            role.SetAssignedByDefault(definition.AssignedByDefault, now, by: null);
            changed = true;
        }

        return changed;
    }
}
