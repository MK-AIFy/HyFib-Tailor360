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
}
