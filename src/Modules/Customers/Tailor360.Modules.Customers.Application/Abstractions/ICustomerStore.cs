using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Reads and writes customer records.
/// </summary>
/// <remarks>
/// The module's internal port, so it may speak in domain types — unlike a published contract, which
/// may not. It exists so the handler can be tested without a database and so the one place that knows
/// how the aggregate is loaded stays in Infrastructure.
/// </remarks>
public interface ICustomerStore
{
    /// <summary>
    /// Loads one customer for change, or null when no record has that identity.
    /// </summary>
    /// <remarks>
    /// Deliberately not filtered by branch. The record is organisation-wide
    /// (<c>docs/prd/workflows/branch-scenarios.md</c> section 3), so reach is the endpoint's decision
    /// and the aliases come with it because a correction may add one.
    /// </remarks>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The customer, or null.</returns>
    Task<Customer?> FindAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new record to the unit of work.</summary>
    /// <param name="customer">The customer.</param>
    void Add(Customer customer);

    /// <summary>
    /// Commits, turning a lost optimistic-concurrency race into a failure rather than an exception.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or <c>CustomersErrors.ConcurrentChange</c>.</returns>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>The concurrency token an edit must be made against.</summary>
    /// <param name="customer">A tracked customer.</param>
    /// <returns>The entity tag.</returns>
    EntityTag EntityTagOf(Customer customer);

    /// <summary>
    /// Allocates the next customer number for a branch, inside the caller's transaction.
    /// </summary>
    /// <param name="branchCode">The branch's short code, which is the middle of the number.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    /// <returns>The number, formatted <c>C-&lt;branch&gt;-000001</c>.</returns>
    Task<string> NextCustomerNumberAsync(string branchCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a merge over two customer records with both rows locked, in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a callback rather than a lock method.</strong> The lock is only worth anything for
    /// as long as the transaction that took it is open, and a port that handed the caller two locked
    /// aggregates and returned would have ended the transaction before the caller decided anything.
    /// Inverting it puts the whole decision inside the lock and leaves no way to write the merge
    /// outside it.
    /// </para>
    /// <para>
    /// <strong>What the implementation guarantees.</strong> Both rows are locked before either is
    /// read, in ascending PostgreSQL <c>uuid</c> order so that two merges naming the same pair the
    /// opposite way round queue instead of deadlocking
    /// (<c>docs/architecture/invariants.md</c>, the Customers concurrency row). The two aggregates
    /// handed to the callback are read inside the transaction and inside the lock, so they are
    /// current; anything the caller read beforehand is not, and is discarded. The callback's own save
    /// is part of this transaction. A failing result rolls the whole thing back.
    /// </para>
    /// <para>
    /// Answers <c>CustomersErrors.CustomerNotFound</c> when either record is missing, and
    /// <c>CustomersErrors.ConcurrentChange</c> when the save loses to somebody else.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOutcome">What the merge produces.</typeparam>
    /// <param name="survivorCustomerId">The record that is to survive.</param>
    /// <param name="mergedCustomerId">The record that is to be folded in.</param>
    /// <param name="merge">
    /// The decision, given the survivor and the merged record in that order, both locked and current.
    /// It is responsible for saving; the transaction commits only when it returns success.
    /// </param>
    /// <param name="cancellationToken">Cancels the merge.</param>
    /// <returns>What the callback produced, or the reason the merge was refused.</returns>
    Task<Result<TOutcome>> InMergeTransactionAsync<TOutcome>(
        Guid survivorCustomerId,
        Guid mergedCustomerId,
        Func<Customer, Customer, CancellationToken, Task<Result<TOutcome>>> merge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-points every record that had been merged into one record so that it names another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Merging B into C when A was already merged into B would otherwise leave A pointing at a record
    /// that itself no longer stands, and every reader would have to follow the chain and decide what
    /// to do about a cycle. Flattening it here means the pointer is always one hop and always lands on
    /// a record that stands.
    /// </para>
    /// <para>
    /// A bulk statement rather than loaded aggregates: the set is unbounded, none of it is read
    /// afterwards, and the rows it touches are the one kind nothing else can write — a merged record
    /// refuses correction, reactivation and a second merge. It stamps the audit columns because the
    /// rows did change, and leaves <c>merged_at</c> alone because <em>when</em> A was merged is still
    /// when A was merged.
    /// </para>
    /// </remarks>
    /// <param name="fromCustomerId">The record they currently name.</param>
    /// <param name="toCustomerId">The record they should name.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>How many records were re-pointed, which is usually none.</returns>
    Task<int> FlattenMergePointersAsync(
        Guid fromCustomerId,
        Guid toCustomerId,
        DateTimeOffset now,
        Guid? by,
        CancellationToken cancellationToken = default);
}
