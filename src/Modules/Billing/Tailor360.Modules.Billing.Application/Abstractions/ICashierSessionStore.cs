using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Cashier sessions, as persistence answers for them.</summary>
public interface ICashierSessionStore
{
    /// <summary>One session with its counts, or null when it is not the organisation's.</summary>
    Task<CashierSession?> FindAsync(Guid sessionId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The cashier's open session at the branch, or null.</summary>
    Task<CashierSession?> FindOpenAsync(Guid branchId, Guid cashierId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The branch's sessions, newest first, at most <paramref name="limit"/>; optionally one status only.</summary>
    Task<IReadOnlyList<CashierSession>> ListAsync(Guid organisationId, Guid branchId, CashierSessionStatus? status, int limit, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new session.</summary>
    void Add(CashierSession session);

    /// <summary>
    /// Holds the session's row in share mode for the rest of the current transaction: a payment
    /// recorded in the session takes this, so a close under way waits for it to commit and counts it,
    /// and a payment that arrives during the close waits and then finds the session closed
    /// (INV-CSH-02, INV-CSH-03). Outside a transaction it throws.
    /// </summary>
    Task LockForPaymentAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the close in one transaction that first locks the session's row against every payment, so
    /// the expected totals the work reads are the totals the close commits over. A refused outcome
    /// rolls the attempt back; the work reads everything it changes, because a retry replays it from
    /// an empty change tracker, and a close already committed is found rather than run again.
    /// </summary>
    Task<Result<TOutcome>> CloseInTransactionAsync<TOutcome>(
        Guid sessionId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default);

    /// <summary>The optimistic-concurrency token of a tracked session.</summary>
    EntityTag EntityTagOf(CashierSession session);

    /// <summary>
    /// Commits. A second open session for the cashier at the branch is refused by the index and comes back
    /// as <c>billing.cashier-session-already-open</c>; a close that lost the race comes back as
    /// <c>billing.cashier-session-already-closed</c>.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
