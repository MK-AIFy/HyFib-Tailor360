using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Reads and replaces an account's role and branch assignments.
/// </summary>
/// <remarks>
/// Both replacements delete and re-insert rather than computing a difference. The rows carry nothing
/// but the pair and who assigned it, so a difference would save two statements and cost the reader the
/// one property that matters here — that what is in the table afterwards is exactly what the
/// administrator chose, with nothing left over from before.
/// </remarks>
/// <param name="context">The module's context.</param>
/// <param name="clock">The clock.</param>
public sealed class UserAssignmentStore(IdentityDbContext context, IClock clock) : IUserAssignmentStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignableRole>> ListAssignableRolesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.Roles
            .AsNoTracking()
            .Where(role => role.OrganisationId == organisationId)
            .OrderBy(role => role.Key)
            .Select(role => new AssignableRole(role.Id, role.Key, role.Name))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ListActiveBranchIdsAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => (await context.Branches
            .AsNoTracking()
            .Where(branch => branch.OrganisationId == organisationId && branch.Status == BranchStatus.Active)
            .Select(branch => branch.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> RolesOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => (await context.UserRoles
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.RoleId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

    /// <inheritdoc />
    public async Task<IReadOnlyList<BranchAssignment>> BranchesOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await context.UserBranchAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.BranchId)
            .Select(assignment => new BranchAssignment(assignment.BranchId, assignment.IsPrimary))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Result> ReplaceRolesAsync(
        StaffUser user,
        IReadOnlyCollection<Guid> roleIds,
        Guid by,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roleIds);

        return ReplaceAsync(
            user,
            by,
            async token =>
            {
                var existing = await context.UserRoles
                    .Where(assignment => assignment.UserId == user.Id)
                    .ToListAsync(token);

                context.UserRoles.RemoveRange(existing);

                var now = clock.UtcNow;

                foreach (var roleId in roleIds)
                {
                    context.UserRoles.Add(UserRoleAssignment.Create(user.Id, roleId, now, by));
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> ReplaceBranchesAsync(
        StaffUser user,
        IReadOnlyCollection<BranchAssignment> branches,
        Guid by,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(branches);

        return ReplaceAsync(
            user,
            by,
            async token =>
            {
                var existing = await context.UserBranchAssignments
                    .Where(assignment => assignment.UserId == user.Id)
                    .ToListAsync(token);

                context.UserBranchAssignments.RemoveRange(existing);

                var now = clock.UtcNow;

                foreach (var branch in branches)
                {
                    context.UserBranchAssignments.Add(
                        UserBranchAssignment.Create(user.Id, branch.BranchId, now, branch.IsPrimary, by));
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Claims the account, then rewrites the rows that hang off it, in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two saves are the point. The first writes nothing but the account's own audit columns, and
    /// its <c>WHERE</c> carries the concurrency token — so a request whose version has moved on is
    /// refused here, before it has touched a single assignment row. The second does the rewrite, by
    /// which time this request is the only one that can be doing it.
    /// </para>
    /// <para>
    /// One save would not do: the change tracker orders inserts before updates, so the losing request
    /// reached the assignment table's primary key first and failed there — a duplicate-key error,
    /// which is a server error, for what is really two administrators editing at once.
    /// </para>
    /// </remarks>
    private async Task<Result> ReplaceAsync(
        StaffUser user,
        Guid by,
        Func<CancellationToken, Task> rewrite,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            user.RecordAdministrativeChange(clock.UtcNow, by);

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                await rewrite(cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure(IdentityErrors.ConcurrentChange);
            }

            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        });
    }

    /// <inheritdoc />
    public Task<int> CountOtherHoldersAsync(
        Guid organisationId,
        string permissionKey,
        Guid exceptUserId,
        CancellationToken cancellationToken = default)
        => context.Users
            .AsNoTracking()
            .Where(user => user.OrganisationId == organisationId
                           && user.Id != exceptUserId
                           && user.Status == UserStatus.Active)
            .CountAsync(
                user => context.UserRoles
                    .Where(assignment => assignment.UserId == user.Id)
                    .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role)
                    .Any(role => role.Permissions.Any(permission => permission.PermissionKey == permissionKey)),
                cancellationToken);
}
