using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Reconciliation batches, as persistence answers for them (INV-CSH-06).</summary>
public interface IReconciliationBatchStore
{
    /// <summary>The batch opened for a session's close, or null — a session closed before #172 shipped has none.</summary>
    Task<ReconciliationBatch?> FindBySessionAsync(Guid sessionId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new batch, opened in the same transaction as the close that produced it.</summary>
    void Add(ReconciliationBatch batch);

    /// <summary>
    /// Runs the approval in one transaction that first locks the batch's row, so the state <paramref
    /// name="work"/> reads and decides against is the state the approval commits over: a second
    /// approval of the same batch waits for the first to commit and then finds it approved already,
    /// rather than racing it. The work reads everything it changes, because a retry replays it from an
    /// empty change tracker, and an approval already committed is found rather than run again.
    /// </summary>
    Task<Result<TOutcome>> ApproveInTransactionAsync<TOutcome>(
        Guid sessionId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits. An approval that lost the race to another comes back as
    /// <c>billing.reconciliation-batch-already-approved</c>.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
