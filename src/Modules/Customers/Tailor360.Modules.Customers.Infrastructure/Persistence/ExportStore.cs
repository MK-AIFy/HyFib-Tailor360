using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// Reads and writes generated subject-access exports, and gathers what one is made of.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class ExportStore(CustomersDbContext context) : IExportStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Tracked rather than <c>AsNoTracking</c>, and the aliases are included, because the renderer
    /// walks them. The organisation is part of the predicate rather than checked afterwards: "not
    /// there" and "not yours" are one answer everywhere in this module, and a query that found the row
    /// and then refused it would be a slower way of saying the same thing with a timing difference.
    /// </remarks>
    public async Task<CustomerSubjectData?> GatherAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        // Follow the merge pointer before reading anything, exactly as ConsentQuery does. A record that
        // was folded into another still answers to its old identifier — an alias on the survivor keeps
        // the number searchable — and somebody exercising a subject-access right will quote whichever
        // number they were given. Reading the folded record directly would answer with the shell it
        // became: a name and a pointer, no consent history, no preferences. That is the worst possible
        // answer to "what do you hold about me", because it is not a refusal and it looks complete.
        var subjectId = await SurvivorOfAsync(customerId, organisationId, cancellationToken);

        var customer = await context.Customers
            .Include(record => record.Aliases)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                record => record.Id == subjectId && record.OrganisationId == organisationId,
                cancellationToken);

        if (customer is null)
        {
            return null;
        }

        // The whole history, oldest first, not the current answer per purpose. A subject-access export
        // says what the person agreed to and when they changed their mind; collapsing it to the
        // current state would answer a different question (docs/nfr/data-classification.md section 5.3
        // — a change is a new record and nothing is ever edited).
        var consent = await context.ConsentRecords
            .AsNoTracking()
            .Where(record => record.CustomerId == subjectId && record.OrganisationId == organisationId)
            .OrderBy(record => record.RecordedAt)
            .ThenBy(record => record.Id)
            .ToListAsync(cancellationToken);

        var preferences = await context.CommunicationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                record => record.CustomerId == subjectId && record.OrganisationId == organisationId,
                cancellationToken);

        return new CustomerSubjectData(customer, consent, preferences);
    }

    /// <inheritdoc />
    public Task<CustomerExport?> FindAsync(
        Guid exportId,
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => context.CustomerExports
            .FirstOrDefaultAsync(
                export => export.Id == exportId
                    && export.CustomerId == customerId
                    && export.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerExport>> LiveForCustomerAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.CustomerExports
            .Where(export => export.CustomerId == customerId
                && export.OrganisationId == organisationId
                && export.PurgedAt == null)
            .OrderBy(export => export.GeneratedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately not filtered by organisation. The cleanup job runs for the installation rather
    /// than for a caller, and an expired copy of somebody's personal data is not one organisation's to
    /// keep because another organisation's job did not reach it.
    /// </remarks>
    public async Task<IReadOnlyList<CustomerExport>> ExpiredHoldingDataAsync(
        DateTimeOffset asAt,
        int limit,
        CancellationToken cancellationToken = default)
        => await context.CustomerExports
            .Where(export => export.PurgedAt == null && export.ExpiresAt <= asAt)
            .OrderBy(export => export.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The record that answers for this identifier: itself, or the one it was folded into.
    /// </summary>
    /// <remarks>
    /// One hop, not a chain. <c>Customer.Absorb</c> re-points the merges that already named the record
    /// being folded in, so a survivor never itself carries a pointer and a second hop would have
    /// nothing to follow. Scoped by organisation for the same reason every other read here is: an
    /// identifier from outside is not found rather than resolved.
    /// </remarks>
    private async Task<Guid> SurvivorOfAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var mergedInto = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == customerId && customer.OrganisationId == organisationId)
            .Select(customer => customer.MergedIntoCustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        return mergedInto ?? customerId;
    }

    /// <inheritdoc />
    public void Add(CustomerExport export) => context.CustomerExports.Add(export);

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
            return Result.Failure(CustomersErrors.ConcurrentChange);
        }
    }
}
