using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// Stages the merge decision and the duplicate decisions on the module's own context.
/// </summary>
/// <remarks>
/// It has no save of its own, and that is the point: the same <see cref="CustomersDbContext"/> holds
/// the customer aggregates and the outbox, so <see cref="CustomerStore.TrySaveChangesAsync"/> commits
/// the record, the evidence and the message that announces it in one transaction on one connection.
/// A second store with a second save would be a second transaction, and the pair could disagree.
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class MergeStore(CustomersDbContext context) : IMergeStore
{
    /// <inheritdoc />
    public void Add(CustomerMerge merge) => context.CustomerMerges.Add(merge);

    /// <inheritdoc />
    public void Add(DuplicateCandidateDecision decision)
        => context.DuplicateCandidates.Add(decision);
}
