using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The published staff directory, over the <c>identity</c> schema.
/// </summary>
/// <remarks>
/// Every read is projected in the database rather than by loading aggregates: a workload board asking
/// for a branch's staff must not fetch every password hash, authenticator secret and recovery code in
/// that branch to render a list of names.
/// <para>
/// The locale falls back to the default when an account has no preferences row. That is a real state —
/// preferences are written the first time somebody changes one — and the alternative would be a null
/// locale that every consumer has to remember to handle.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class UserDirectory(IdentityDbContext context) : IUserDirectory
{
    /// <inheritdoc />
    public async Task<StaffMember?> FindAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var found = await ProjectAsync(user => user.Id == userId, cancellationToken);

        return found.Count == 0 ? null : found[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StaffMember>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return [];
        }

        var wanted = userIds.Distinct().ToArray();

        return await ProjectAsync(user => wanted.Contains(user.Id), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StaffMember>> ListActiveInBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
        => await ProjectAsync(
            user => user.Status == UserStatus.Active
                    && context.UserBranchAssignments.Any(
                        assignment => assignment.UserId == user.Id && assignment.BranchId == branchId),
            cancellationToken);

    private async Task<IReadOnlyList<StaffMember>> ProjectAsync(
        System.Linq.Expressions.Expression<Func<StaffUser, bool>> match,
        CancellationToken cancellationToken)
        => await context.Users
            .AsNoTracking()
            .Where(match)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new StaffMember(
                user.Id,
                user.DisplayName,
                user.Status == UserStatus.Active,
                context.Preferences
                    .Where(preferences => preferences.UserId == user.Id)
                    .Select(preferences => preferences.Locale)
                    .FirstOrDefault() ?? UserPreferences.DefaultLocale,
                context.UserRoles
                    .Where(assignment => assignment.UserId == user.Id)
                    .Join(context.Roles, assignment => assignment.RoleId, role => role.Id,
                        (_, role) => role.Key)
                    .OrderBy(key => key)
                    .ToList(),
                context.UserBranchAssignments
                    .Where(assignment => assignment.UserId == user.Id)
                    .OrderBy(assignment => assignment.BranchId)
                    .Select(assignment => assignment.BranchId)
                    .ToList(),
                user.HomeBranchId))
            .ToListAsync(cancellationToken);
}
