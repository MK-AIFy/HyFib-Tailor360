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
}
