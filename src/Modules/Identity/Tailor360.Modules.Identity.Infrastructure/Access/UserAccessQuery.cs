using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;

namespace Tailor360.Modules.Identity.Infrastructure.Access;

/// <summary>
/// Reads an account's effective roles, permissions and branch assignments from the <c>identity</c>
/// schema.
/// </summary>
/// <remarks>
/// <para>
/// Two round trips, not one and not four. The roles and their grants come back in a single left join,
/// so a role that grants nothing still contributes its name to the multi-factor decision; the branch
/// assignments are a second indexed lookup on the same key. This runs on every authenticated request,
/// which is why it is written as two statements rather than as a convenient chain of navigations that
/// would issue one per role.
/// </para>
/// <para>
/// Nothing here is cached. The session ticket is rebuilt from the database each request precisely so
/// that a permission revoked a minute ago stops working now, and a cache in front of this query would
/// give that property away.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class UserAccessQuery(IdentityDbContext context) : IUserAccessQuery
{
    /// <inheritdoc />
    public async Task<UserAccess> ResolveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return UserAccess.None;
        }

        var grants = await (
                from assignment in context.UserRoles.AsNoTracking()
                where assignment.UserId == userId
                join role in context.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                join permission in context.RolePermissions.AsNoTracking()
                    on role.Id equals permission.RoleId into granted
                from permission in granted.DefaultIfEmpty()
                select new { role.Name, Permission = permission != null ? permission.PermissionKey : null })
            .ToListAsync(cancellationToken);

        var branchIds = await context.UserBranchAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.BranchId)
            .ToListAsync(cancellationToken);

        var roleNames = new HashSet<string>(StringComparer.Ordinal);
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var grant in grants)
        {
            roleNames.Add(grant.Name);
            if (grant.Permission is { } permission)
            {
                permissions.Add(permission);
            }
        }

        return new UserAccess(roleNames, permissions, new HashSet<Guid>(branchIds));
    }
}
