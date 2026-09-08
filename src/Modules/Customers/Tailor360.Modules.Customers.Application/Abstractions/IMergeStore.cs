using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Where the evidence for exception EX-01 is written: the decisions a person took about a scored
/// duplicate suspicion, and the irreversible merge one of them produced.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of row on one port because they are two halves of one story and are always written by
/// the same transaction as the customer change they describe. There is no save here for the same
/// reason: <see cref="ICustomerStore.TrySaveChangesAsync"/> commits the aggregate, these rows and the
/// outbox message together, on one connection, because a merge recorded without its evidence — or
/// evidence recorded for a merge that rolled back — is worse than either failing.
/// </para>
/// <para>
/// Reads are deliberately absent. What a screen needs about duplicates it gets by scoring the current
/// records through <see cref="ICustomerDirectory.FindDuplicatesAsync"/>; what an auditor needs it gets
/// from the audit trail. A stored decision is evidence that somebody judged, not a cache of the
/// judgement, and reading it back to make the next decision would let a stale score decide something.
/// </para>
/// </remarks>
public interface IMergeStore
{
    /// <summary>Stages a merge decision. Committed by the customer store's save.</summary>
    /// <param name="merge">The decision.</param>
    void Add(CustomerMerge merge);

    /// <summary>Stages what a person decided about one scored candidate.</summary>
    /// <param name="decision">The decision.</param>
    void Add(DuplicateCandidateDecision decision);
}
