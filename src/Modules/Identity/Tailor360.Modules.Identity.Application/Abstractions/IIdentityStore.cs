using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Users;

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

    /// <summary>Commits the changes made through this store.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
