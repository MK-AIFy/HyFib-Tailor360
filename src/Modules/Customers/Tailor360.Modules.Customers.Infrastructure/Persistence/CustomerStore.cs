using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Sequencing;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// Loads and saves customer records.
/// </summary>
/// <param name="context">The module's context.</param>
/// <param name="sequences">The platform's sequence allocator, for the display number.</param>
public sealed class CustomerStore(CustomersDbContext context, ISequenceAllocator sequences)
    : ICustomerStore
{
    /// <inheritdoc />
    public Task<Customer?> FindAsync(Guid customerId, CancellationToken cancellationToken = default)
        => context.Customers
            .Include(customer => customer.Aliases)
            .Include(customer => customer.Visibility)
            // Two collections on one aggregate, so a single query would return the Cartesian product of
            // aliases and visibility rows — every alias repeated once per branch that can see the
            // record. EF Core warns about exactly this; splitting is safe here because the query names
            // one row by its key and so cannot see a different set of children between the two round
            // trips than a single query would have.
            .AsSplitQuery()
            .FirstOrDefaultAsync(customer => customer.Id == customerId, cancellationToken);

    /// <inheritdoc />
    public void Add(Customer customer) => context.Customers.Add(customer);

    /// <inheritdoc />
    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else corrected the record between it being read and being written. The screen
            // can say so and re-read; an exception here would become a 500 and say nothing.
            return Result.Failure(CustomersErrors.ConcurrentChange);
        }
    }

    /// <inheritdoc />
    public EntityTag EntityTagOf(Customer customer) => context.EntityTagOf(customer);

    /// <inheritdoc />
    /// <remarks>
    /// A gap is acceptable in a customer number and a reuse is not. The allocator runs on the platform
    /// context, so a customer whose own save then fails leaves its number unused — which is why
    /// <c>docs/architecture/conventions.md</c> section 3.2 requires only that display numbers are
    /// never reused, and reserves gap-free allocation for the statutory documents that need it.
    /// </remarks>
    public async Task<string> NextCustomerNumberAsync(
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchCode);

        var next = await sequences.NextAsync(
            CustomerHandler.CustomerNumberSequence, branchCode, cancellationToken);

        return string.Create(
            CultureInfo.InvariantCulture, $"C-{branchCode.ToUpperInvariant()}-{next:000000}");
    }

    /// <inheritdoc />
    public async Task<Result<TOutcome>> InMergeTransactionAsync<TOutcome>(
        Guid survivorCustomerId,
        Guid mergedCustomerId,
        Func<Customer, Customer, CancellationToken, Task<Result<TOutcome>>> merge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(merge);

        // The connection retries on a transient failure, and a retry replays this whole delegate. An
        // explicit transaction outside the strategy would be rejected by EF for exactly that reason.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Anything read before the lock was read from an unlocked row, and the change tracker
            // would hand that copy back to the reads below rather than going to the database. The
            // merge transaction is the unit of work and it starts from nothing.
            context.ChangeTracker.Clear();

            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);

            // Both rows locked before either is read, in ascending PostgreSQL uuid order, so that two
            // merges naming the same pair the opposite way round queue instead of deadlocking
            // (docs/architecture/invariants.md, the Customers concurrency row).
            var (first, second) = InPostgresOrder(survivorCustomerId, mergedCustomerId);

            await LockAsync(first, cancellationToken);
            await LockAsync(second, cancellationToken);

            var survivor = await FindAsync(survivorCustomerId, cancellationToken);
            var merged = await FindAsync(mergedCustomerId, cancellationToken);

            if (survivor is null || merged is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return Result.Failure<TOutcome>(CustomersErrors.CustomerNotFound);
            }

            Result<TOutcome> outcome;

            try
            {
                outcome = await merge(survivor, merged, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The callback saves inside this transaction, and its own store call already turns
                // this into a result. Caught here as well because a merge writes more than the
                // aggregate, and a raw exception escaping would be a 500 for two people editing at
                // once.
                await transaction.RollbackAsync(cancellationToken);

                return Result.Failure<TOutcome>(CustomersErrors.ConcurrentChange);
            }

            if (outcome.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);

                return outcome;
            }

            await transaction.CommitAsync(cancellationToken);

            return outcome;
        });
    }

    /// <inheritdoc />
    public Task<int> FlattenMergePointersAsync(
        Guid fromCustomerId,
        Guid toCustomerId,
        DateTimeOffset now,
        Guid? by,
        CancellationToken cancellationToken = default)
        => context.Customers
            .Where(customer => customer.MergedIntoCustomerId == fromCustomerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(customer => customer.MergedIntoCustomerId, toCustomerId)
                    .SetProperty(customer => customer.UpdatedAt, now)
                    .SetProperty(customer => customer.UpdatedBy, by),
                cancellationToken);

    /// <summary>
    /// Takes a row lock on one customer for the length of the ambient transaction.
    /// </summary>
    /// <remarks>
    /// A statement rather than a query with <c>FOR UPDATE</c> appended: PostgreSQL refuses
    /// <c>FOR UPDATE</c> inside the sub-query EF composes when a <c>FromSql</c> is combined with
    /// <c>Include</c> or <c>AsSplitQuery</c>, which is how the aggregate is loaded. Taking the lock
    /// first and reading afterwards, in the same transaction, gets both without either fighting the
    /// other. A row that does not exist locks nothing and is not an error; the caller reads null and
    /// answers "not found".
    /// </remarks>
    /// <param name="customerId">The record to lock.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The rows the statement reports, which nothing reads.</returns>
    private Task<int> LockAsync(Guid customerId, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlAsync(
            $"SELECT id FROM customers.customers WHERE id = {customerId} FOR UPDATE",
            cancellationToken);

    /// <summary>
    /// The two identifiers in the order PostgreSQL compares them.
    /// </summary>
    /// <remarks>
    /// PostgreSQL orders a <c>uuid</c> by its sixteen bytes as they are written; .NET's
    /// <see cref="Guid.CompareTo(Guid)"/> walks the structure's fields, and for the first three of
    /// them the byte order is reversed. The two disagree, so ordering with the wrong one would give
    /// two requests different opinions about which row to lock first — which is the deadlock this
    /// exists to prevent.
    /// </remarks>
    private static (Guid First, Guid Second) InPostgresOrder(Guid left, Guid right)
        => left.ToByteArray(bigEndian: true).AsSpan()
                .SequenceCompareTo(right.ToByteArray(bigEndian: true)) <= 0
            ? (left, right)
            : (right, left);
}
