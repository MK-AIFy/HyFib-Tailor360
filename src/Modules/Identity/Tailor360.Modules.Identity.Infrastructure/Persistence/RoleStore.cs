using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>The role register over the <c>identity</c> schema.</summary>
/// <param name="context">The module's context.</param>
public sealed class RoleStore(IdentityDbContext context) : IRoleStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.Roles
            .Include(role => role.Permissions)
            .Where(role => role.OrganisationId == organisationId)
            .OrderBy(role => role.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Role?> FindAsync(Guid roleId, CancellationToken cancellationToken = default)
        => context.Roles
            .Include(role => role.Permissions)
            .FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsKeyTakenAsync(
        Guid organisationId,
        string key,
        CancellationToken cancellationToken = default)
        => context.Roles.AsNoTracking().AnyAsync(
            role => role.OrganisationId == organisationId && role.Key == key, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountHoldersAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        // Deactivated accounts are excluded on purpose. A closed account holding a role is not a reason
        // to refuse deleting it, and counting them would make the number on the screen disagree with
        // "who can actually do this" — which is the question the count is there to answer.
        var counted = await context.UserRoles
            .AsNoTracking()
            .Where(assignment => context.Roles.Any(
                       role => role.Id == assignment.RoleId && role.OrganisationId == organisationId)
                   && context.Users.Any(
                       user => user.Id == assignment.UserId && user.Status != UserStatus.Deactivated))
            .GroupBy(assignment => assignment.RoleId)
            .Select(group => new { RoleId = group.Key, Holders = group.Count() })
            .ToListAsync(cancellationToken);

        return counted.ToDictionary(row => row.RoleId, row => row.Holders);
    }

    /// <inheritdoc />
    public Task<int> CountHoldersThroughOtherRolesAsync(
        Guid organisationId,
        string permissionKey,
        Guid exceptRoleId,
        CancellationToken cancellationToken = default)
        => context.Users
            .AsNoTracking()
            .CountAsync(
                user => user.OrganisationId == organisationId
                        && user.Status != UserStatus.Deactivated
                        && user.Status != UserStatus.Suspended
                        && context.UserRoles.Any(
                            assignment => assignment.UserId == user.Id
                                          && assignment.RoleId != exceptRoleId
                                          && context.RolePermissions.Any(
                                              grant => grant.RoleId == assignment.RoleId
                                                       && grant.PermissionKey == permissionKey)),
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> PermissionsOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var held = await context.RolePermissions
            .AsNoTracking()
            .Where(grant => context.UserRoles.Any(
                assignment => assignment.UserId == userId && assignment.RoleId == grant.RoleId))
            .Select(grant => grant.PermissionKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        return held.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void Add(Role role) => context.Roles.Add(role);

    /// <inheritdoc />
    public void Remove(Role role) => context.Roles.Remove(role);

    /// <inheritdoc />
    public EntityTag EntityTagOf(Role role) => context.EntityTagOf(role);

    /// <inheritdoc />
    public void Touch(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        // Marking the entry modified is what puts the role row in the UPDATE statement and therefore in
        // the version check. Setting a property would do the same and would also change something the
        // administrator did not ask to change; this changes nothing and still contends.
        context.Entry(role).State = EntityState.Modified;
    }

    /// <inheritdoc />
    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(IdentityErrors.ConcurrentChange);
        }
    }
}
