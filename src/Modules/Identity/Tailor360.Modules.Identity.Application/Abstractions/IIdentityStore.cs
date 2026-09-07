using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// The reads and writes the multi-factor and recovery handlers need. It is deliberately narrow: this
/// is not a general repository over the <c>identity</c> schema, it is the set of operations these
/// handlers perform, so that widening it is a visible decision.
/// </summary>
/// <remarks>
/// A user returned by either lookup carries the whole aggregate — password, authenticator, recovery
/// codes, passkeys, remembered devices — because every rule these handlers apply spans two of those.
/// Loading a partial aggregate and then asking it whether the account has a second factor would give a
/// confident wrong answer, so the store does not offer that option.
/// </remarks>
public interface IIdentityStore
{
    /// <summary>Loads one account and its credentials, or <see langword="null"/> when there is none.</summary>
    Task<StaffUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one account by the upper-cased form of its email address. The caller normalises; the store
    /// does not guess, because a lookup that silently normalised differently from the write path would
    /// make accounts unfindable rather than fail.
    /// </summary>
    Task<StaffUser?> FindUserByNormalisedEmailAsync(
        string normalisedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a freshly issued recovery token.</summary>
    void AddRecoveryToken(RecoveryToken token);

    /// <summary>
    /// Adds a newly invited account.
    /// </summary>
    /// <remarks>
    /// Provisioning widens what this store is for, which the note above asks to be a visible decision
    /// rather than a quiet one. It belongs here because an invitation writes the account and its
    /// single-use token in one unit of work, and splitting them across two stores would mean an account
    /// that exists with no way to reach it, or a token pointing at nothing.
    /// </remarks>
    /// <param name="user">The account.</param>
    void AddUser(StaffUser user);

    /// <summary>
    /// Whether a sign-in name or an address is already in use in this organisation.
    /// </summary>
    /// <remarks>
    /// Asked before the insert so that a collision is a field error naming the field, rather than a
    /// unique-index violation reaching the caller as a server error. It is a check and not a promise:
    /// two invitations racing on one name still meet the index, which is the guarantee, and the loser
    /// is answered as the conflict it is.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="userName">The sign-in name being claimed.</param>
    /// <param name="normalisedEmail">The upper-cased address being claimed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsIdentifierTakenAsync(
        Guid organisationId,
        string userName,
        string normalisedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a recovery token by its digest, whatever its state. A spent or expired token is returned
    /// rather than filtered out, so that the caller reports one failure for every unusable token
    /// instead of a different one for "not found".
    /// </summary>
    Task<RecoveryToken?> FindRecoveryTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws every outstanding token of one purpose for one account, and returns how many. Issuing
    /// a replacement calls this first, so a second request never leaves two working links behind.
    /// </summary>
    Task<int> InvalidateOutstandingRecoveryTokensAsync(
        Guid userId,
        RecoveryPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the concurrency token of an account this store loaded, for the <c>ETag</c> an
    /// administrative screen edits against.
    /// </summary>
    /// <remarks>
    /// The token is a shadow property maintained by the store, so there is nothing on
    /// <see cref="StaffUser"/> to read it from and no way for this layer to reach it directly. Asking
    /// the store keeps the mapping's business where the mapping is.
    /// </remarks>
    /// <param name="user">An account returned by one of the lookups above.</param>
    EntityTag EntityTagOf(StaffUser user);

    /// <summary>Commits the changes made through this store.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the changes made through this store, reporting a lost race rather than throwing.
    /// </summary>
    /// <remarks>
    /// Accepting an answer to a challenge is a read-modify-write: the row is read, the domain decides
    /// whether the answer may be accepted, and the decision is written back. Two requests carrying the
    /// same answer both read a row that still permits it, so it is the database that settles which of
    /// them wrote — and the caller has to be told, because to the loser nothing looks wrong. Every
    /// other write through this store is an unconditional one where losing that race is not possible,
    /// which is why this is a second method rather than the shape of the first.
    /// </remarks>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
