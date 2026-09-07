using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// The reads and writes the assignment handler needs: which roles and branches exist, which the
/// account holds, and how to replace either set.
/// </summary>
/// <remarks>
/// A second narrow port rather than more methods on <c>IIdentityStore</c>, which is deliberately the
/// multi-factor and recovery handlers' view of the schema. Assignments are a different question asked
/// of the same tables, and keeping them apart means neither port grows into a general repository that
/// any handler can reach for.
/// </remarks>
public interface IUserAssignmentStore
{
    /// <summary>Every role in the organisation that an administrator may assign.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AssignableRole>> ListAssignableRolesAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The branches in the organisation that are open for business.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlySet<Guid>> ListActiveBranchIdsAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The roles the account holds now.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlySet<Guid>> RolesOfAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The branches the account is assigned to now, and which of them is primary.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<BranchAssignment>> BranchesOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the account's roles with exactly the set given, and commits.
    /// </summary>
    /// <remarks>
    /// The write is the store's rather than the caller's because the order of its statements is what
    /// makes it safe. The account row is claimed first — a conditional update on the version the caller
    /// read — and only a request that won that claim goes on to touch the assignment rows. Letting the
    /// change tracker choose the order instead put the inserts first, so two administrators racing
    /// collided on the assignment table's primary key and the loser was answered with a server error
    /// rather than with the conflict it actually was.
    /// </remarks>
    /// <param name="user">The account, loaded and tracked.</param>
    /// <param name="roleIds">The roles it should hold afterwards.</param>
    /// <param name="by">The administrator making the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> ReplaceRolesAsync(
        StaffUser user,
        IReadOnlyCollection<Guid> roleIds,
        Guid by,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the account's branch assignments with exactly the set given, and commits.</summary>
    /// <remarks>Ordered for the same reason as <see cref="ReplaceRolesAsync"/>.</remarks>
    /// <param name="user">The account, loaded and tracked.</param>
    /// <param name="branches">The branches it should be assigned to afterwards.</param>
    /// <param name="by">The administrator making the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> ReplaceBranchesAsync(
        StaffUser user,
        IReadOnlyCollection<BranchAssignment> branches,
        Guid by,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many other accounts are active and hold a role granting <paramref name="permissionKey"/>.
    /// </summary>
    /// <remarks>
    /// The question behind the self-lockout guard. Asked as a count of <em>other</em> accounts because
    /// the only answer that matters is whether anybody would be left, and asked of the store because
    /// the join it needs spans three tables the application layer cannot see.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="permissionKey">The permission that must not be lost.</param>
    /// <param name="exceptUserId">The account being changed, which is excluded from the count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountOtherHoldersAsync(
        Guid organisationId,
        string permissionKey,
        Guid exceptUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>A role an administrator may assign.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Key">Its stable key, which is what an audit snapshot records.</param>
/// <param name="Name">Its display name.</param>
public sealed record AssignableRole(Guid RoleId, string Key, string Name);

/// <summary>One branch an account works in.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="IsPrimary">Whether it is the account's usual place of work.</param>
public sealed record BranchAssignment(Guid BranchId, bool IsPrimary);
