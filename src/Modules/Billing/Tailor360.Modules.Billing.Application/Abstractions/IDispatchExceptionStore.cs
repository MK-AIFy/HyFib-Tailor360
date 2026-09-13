using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Dispatch exceptions, as persistence answers for them.</summary>
public interface IDispatchExceptionStore
{
    /// <summary>One exception, or null when it is not the organisation's.</summary>
    Task<DispatchException?> FindAsync(Guid dispatchExceptionId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live (approved, unexpired) exception of this order that covers exactly this job set, or null
    /// when none does — an exception for a different set of jobs answers nothing for this one.
    /// </summary>
    Task<DispatchException?> FindLiveMatchingAsync(
        Guid orderId, Guid organisationId, IReadOnlyCollection<Guid> jobIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Every exception still approved and past its expiry, oldest first, up to a batch size.</summary>
    Task<IReadOnlyList<DispatchException>> ListDueForExpiryAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Tracks a newly approved exception.</summary>
    void Add(DispatchException exception);

    /// <summary>
    /// Runs a consumption or an expiry in one transaction that first locks the exception's row, so the
    /// state <paramref name="work"/> reads and decides against is the state it commits over: a second,
    /// concurrent attempt on the same exception waits for the first to commit and then finds it moved
    /// on already, rather than racing it. The work reads everything it changes, because a retry replays
    /// it from an empty change tracker; when the commit's outcome is unknown after a connection retry,
    /// <paramref name="committed"/> says whether the transition this call was making has landed, rather
    /// than the work running again over a row already moved on.
    /// </summary>
    Task<Result<TOutcome>> RunExclusiveAsync<TOutcome>(
        Guid dispatchExceptionId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        Func<CancellationToken, Task<bool>> committed,
        CancellationToken cancellationToken = default);

    /// <summary>Commits.</summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
