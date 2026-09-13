using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Reads and writes calculation snapshots, which are written once and never changed.</summary>
public interface ICalculationSnapshotStore
{
    /// <summary>The snapshot stored under a reference, or null.</summary>
    Task<CalculationSnapshot?> FindAsync(Guid organisationId, string reference, CancellationToken cancellationToken = default);

    /// <summary>Adds a snapshot to the context.</summary>
    void Add(CalculationSnapshot snapshot);

    /// <summary>
    /// Commits a snapshot. Two calculations under one reference in the same moment are settled by the
    /// unique index: the loser is told so, and the caller answers with what the winner stored.
    /// </summary>
    /// <remarks>
    /// Commits everything the module's context is tracking, as every store's save does: a caller that
    /// prices in the middle of its own unit of work is committing that work with the snapshot.
    /// </remarks>
    /// <returns>Success, or <c>billing.snapshot-exists</c>.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
