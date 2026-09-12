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

    /// <summary>The optimistic-concurrency token of a tracked session.</summary>
    EntityTag EntityTagOf(CashierSession session);

    /// <summary>
    /// Commits. A second open session for the cashier at the branch is refused by the index and comes back
    /// as <c>billing.cashier-session-already-open</c>; a close that lost the race comes back as
    /// <c>billing.cashier-session-already-closed</c>.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
