using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// Changing what one account may do and where it may do it.
/// </summary>
/// <remarks>
/// <para>
/// Both replacements are whole-set: the administrator sends the roles the person should hold, not the
/// ones to add. That is what the screen shows and what an audit entry can be read against — "these
/// three, where it used to be these two" is a sentence; "added one, removed one, left one" is a diff
/// somebody has to reconstruct.
/// </para>
/// <para>
/// <b>Neither assignment table carries a concurrency token.</b> They are join rows, not aggregates, so
/// two administrators editing one person's access at the same moment would both succeed and the second
/// would silently discard the first. The account row is touched as part of the change, which makes both
/// edits contend for the one row that does carry a token — so the loser is refused and can see what the
/// winner did rather than wondering why their change evaporated.
/// </para>
/// <para>
/// The invariants live here rather than in the domain because they are questions about the catalogue
/// and about other rows: whether a role exists, whether a branch is open, and whether anybody would be
/// left who can administer users. An aggregate cannot see any of that, and a rule it cannot enforce
/// does not belong in it.
/// </para>
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="assignments">Role and branch assignments.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="logger">Logger. Never receives a reason or a name.</param>
public sealed class UserAssignmentHandler(
    IIdentityStore store,
    IUserAssignmentStore assignments,
    IAuditWriter audit,
    ILogger<UserAssignmentHandler> logger)
{
    /// <summary>The audit action recorded when an account's roles are replaced.</summary>
    public const string RolesReplacedAction = "identity.user.roles-replaced";

    /// <summary>The audit action recorded when an account's branch assignments are replaced.</summary>
    public const string BranchesReplacedAction = "identity.user.branches-replaced";

    /// <summary>Replaces the roles an account holds.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="roleKeys">The roles it should hold afterwards, by key.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AssignedAccess>> ReplaceRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roleKeys,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleKeys);

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.UserNotFound);
        }

        var catalogue = await assignments.ListAssignableRolesAsync(user.OrganisationId, cancellationToken);
        var wanted = new List<AssignableRole>();

        foreach (var key in roleKeys.Select(key => key?.Trim()).Distinct(StringComparer.Ordinal))
        {
            var role = catalogue.FirstOrDefault(
                candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));

            if (role is null)
            {
                return Result.Failure<AssignedAccess>(IdentityErrors.RoleNotFound(key));
            }

            wanted.Add(role);
        }

        // The guard that stops an organisation locking itself out. Checked before the write, against
        // the accounts that would remain: an administrator may take the last administrative role off
        // somebody else only while somebody else can still put it back.
        var losingAdministration =
            (await assignments.RolesOfAsync(userId, cancellationToken)).Count > 0
            && !wanted.Any(role => role.Key is SystemRoleKeys.Owner or SystemRoleKeys.Admin);

        if (losingAdministration
            && await assignments.CountOtherHoldersAsync(
                user.OrganisationId, IdentityPermissions.Users, userId, cancellationToken) == 0)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.LastAdministrator);
        }

        var before = await SnapshotAsync(user, cancellationToken);

        var written = await assignments.ReplaceRolesAsync(
            user, [.. wanted.Select(role => role.RoleId)], actor, cancellationToken);

        if (written.IsFailure)
        {
            return Result.Failure<AssignedAccess>(written.Error);
        }

        var after = await SnapshotAsync(user, cancellationToken);

        await AdministrationAudit.RecordAsync(
            audit,
            RolesReplacedAction,
            AdministrationAudit.StaffUserEntity,
            userId,
            $"The account now holds {wanted.Count} role(s).",
            reason,
            before,
            after,
            cancellationToken);

        IdentityLog.AccountAdministered(logger, userId, RolesReplacedAction, 0);

        return Result.Success(new AssignedAccess(after.Roles, after.Branches, store.EntityTagOf(user)));
    }

    /// <summary>Replaces the branches an account is assigned to.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="branches">The branches it should work in afterwards.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AssignedAccess>> ReplaceBranchesAsync(
        Guid userId,
        IReadOnlyCollection<BranchAssignment> branches,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branches);

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.UserNotFound);
        }

        var wanted = branches
            .GroupBy(branch => branch.BranchId)
            .Select(group => new BranchAssignment(group.Key, group.Any(branch => branch.IsPrimary)))
            .ToList();

        var open = await assignments.ListActiveBranchIdsAsync(user.OrganisationId, cancellationToken);

        if (wanted.FirstOrDefault(branch => !open.Contains(branch.BranchId)) is { } closed)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.BranchNotAssignable(closed.BranchId));
        }

        // The store's filtered unique index allows one primary per account and would otherwise refuse
        // the write with a constraint violation, which reaches the caller as a server error rather than
        // as something they can fix.
        if (wanted.Count(branch => branch.IsPrimary) > 1)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.OnePrimaryBranchOnly);
        }

        // A default branch the account is not assigned to would send every screen to a branch its
        // holder cannot act in.
        if (user.HomeBranchId is { } home && wanted.Count > 0 && wanted.All(branch => branch.BranchId != home))
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.HomeBranchNotAssigned);
        }

        var before = await SnapshotAsync(user, cancellationToken);

        var written = await assignments.ReplaceBranchesAsync(user, wanted, actor, cancellationToken);

        if (written.IsFailure)
        {
            return Result.Failure<AssignedAccess>(written.Error);
        }

        var after = await SnapshotAsync(user, cancellationToken);

        await AdministrationAudit.RecordAsync(
            audit,
            BranchesReplacedAction,
            AdministrationAudit.StaffUserEntity,
            userId,
            $"The account now works in {wanted.Count} branch(es).",
            reason,
            before,
            after,
            cancellationToken);

        IdentityLog.AccountAdministered(logger, userId, BranchesReplacedAction, 0);

        return Result.Success(new AssignedAccess(after.Roles, after.Branches, store.EntityTagOf(user)));
    }

    /// <summary>Reads what the account may do and where, for an administrative screen.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AssignedAccess>> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AssignedAccess>(IdentityErrors.UserNotFound);
        }

        var snapshot = await SnapshotAsync(user, cancellationToken);

        return Result.Success(
            new AssignedAccess(snapshot.Roles, snapshot.Branches, store.EntityTagOf(user)));
    }

    /// <summary>
    /// What the audit trail records: role keys and branch identifiers, never names.
    /// </summary>
    private async Task<AccessSnapshot> SnapshotAsync(StaffUser user, CancellationToken cancellationToken)
    {
        var catalogue = await assignments.ListAssignableRolesAsync(user.OrganisationId, cancellationToken);
        var held = await assignments.RolesOfAsync(user.Id, cancellationToken);
        var branches = await assignments.BranchesOfAsync(user.Id, cancellationToken);

        return new AccessSnapshot(
            [.. catalogue.Where(role => held.Contains(role.RoleId)).Select(role => role.Key).Order(StringComparer.Ordinal)],
            [.. branches]);
    }
}

/// <summary>The keys of the two roles that carry administrative authority.</summary>
/// <remarks>
/// Named here rather than reached through the seeder, which lives in Infrastructure and is not visible
/// from this layer. They are the two roles the reference data grants <c>admin.users</c> to, and the
/// last-administrator guard asks about them by key for that reason.
/// </remarks>
public static class SystemRoleKeys
{
    /// <summary>The owner of the business.</summary>
    public const string Owner = "owner";

    /// <summary>An administrator of users, branches, roles and configuration.</summary>
    public const string Admin = "admin";
}

/// <summary>What an account may do and where, as an administrative screen sees it.</summary>
/// <param name="RoleKeys">The roles it holds, by key, in a stable order.</param>
/// <param name="Branches">The branches it works in, and which one is primary.</param>
/// <param name="Version">The concurrency token an edit must present. It is the account's own.</param>
public sealed record AssignedAccess(
    IReadOnlyList<string> RoleKeys,
    IReadOnlyList<BranchAssignment> Branches,
    Tailor360.Platform.Abstractions.Concurrency.EntityTag Version);

/// <summary>The assignment state recorded in the audit trail before and after a change.</summary>
/// <param name="Roles">Role keys.</param>
/// <param name="Branches">Branch assignments.</param>
internal sealed record AccessSnapshot(
    IReadOnlyList<string> Roles,
    IReadOnlyList<BranchAssignment> Branches);
