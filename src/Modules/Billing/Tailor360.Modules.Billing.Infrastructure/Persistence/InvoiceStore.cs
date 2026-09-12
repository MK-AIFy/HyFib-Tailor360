using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;
using Tailor360.Platform.Persistence.Sequencing;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>The invoice store over <see cref="BillingDbContext"/>.</summary>
public sealed class InvoiceStore(BillingDbContext context, ITransactionalSequenceAllocator sequences) : IInvoiceStore
{
    /// <inheritdoc />
    public Task<Result<TOutcome>> PostInTransactionAsync<TOutcome>(
        Guid invoiceId,
        Guid organisationId,
        string barcodePayload,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default)
        => InTransactionAsync(
            invoiceId,
            work,
            token => context.Invoices.AsNoTracking().IgnoreAutoIncludes()
                .AnyAsync(invoice => invoice.Id == invoiceId && invoice.OrganisationId == organisationId && invoice.BarcodePayload == barcodePayload, token),
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<TOutcome>> AppendInTransactionAsync<TOutcome>(
        Guid invoiceId,
        Guid noteId,
        Guid organisationId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default)
        => InTransactionAsync(
            invoiceId,
            work,
            token => context.AdjustmentNotes.AsNoTracking().IgnoreAutoIncludes()
                .AnyAsync(note => note.Id == noteId && note.OrganisationId == organisationId, token),
            cancellationToken);

    /// <inheritdoc />
    public Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A document number is drawn inside the transaction that posts the document.");

        return sequences.NextAsync(sequenceKey, scope, transaction.GetDbTransaction(), cancellationToken);
    }

    /// <summary>
    /// One attempt is one transaction on this context's connection. The connection retries on a transient
    /// failure and a retry replays the whole attempt from an empty change tracker, so the work must read
    /// everything it changes; when the commit's outcome is unknown, <paramref name="committed"/> decides
    /// whether the attempt is done rather than the work running again over a document already posted.
    /// </summary>
    private async Task<Result<TOutcome>> InTransactionAsync<TOutcome>(
        Guid invoiceId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        Func<CancellationToken, Task<bool>> committed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                async token =>
                {
                    context.ChangeTracker.Clear();

                    // The invoice's row, locked for the rest of the transaction before anything about it is
                    // read: two notes on one line, a note racing a cancellation, two posts of one draft —
                    // each waits for the other and then reads what it committed. FOR NO KEY UPDATE, so the
                    // lock neither fires the immutability trigger nor blocks a reader.
                    await context.Database.ExecuteSqlAsync($"SELECT id FROM billing.invoices WHERE id = {invoiceId} FOR NO KEY UPDATE", token);

                    var outcome = await work(token);
                    if (outcome.IsFailure)
                    {
                        throw new AttemptRefusedException(outcome.Error);
                    }

                    return outcome;
                },
                committed,
                cancellationToken);
        }
        catch (AttemptRefusedException refused)
        {
            return Result.Failure<TOutcome>(refused.Error);
        }
    }

    private sealed class AttemptRefusedException(Error error) : Exception(error.Code)
    {
        public Error Error { get; } = error;
    }

    /// <inheritdoc />
    public async Task<Invoice?> FindAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default)
        => await context.Invoices
            .SingleOrDefaultAsync(invoice => invoice.Id == invoiceId && invoice.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<InvoicePage> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, InvoiceListQuery.MaximumLimit);
        var rows = context.Invoices
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(invoice => invoice.Cancellation)
            .Where(invoice => invoice.OrganisationId == query.OrganisationId && invoice.BranchId == query.BranchId);
        if (query.Status is { } status)
        {
            rows = rows.Where(invoice => invoice.Status == status);
        }

        if (Decode(query.Cursor) is var (seenAt, lastId) && lastId != Guid.Empty)
        {
            rows = rows.Where(invoice => invoice.UpdatedAt < seenAt || (invoice.UpdatedAt == seenAt && invoice.Id.CompareTo(lastId) < 0));
        }

        var page = await rows
            .OrderByDescending(invoice => invoice.UpdatedAt)
            .ThenByDescending(invoice => invoice.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;
        var found = page.Take(limit).ToList();

        return new InvoicePage(found, hasMore && found.Count > 0 ? Encode(found[^1].UpdatedAt, found[^1].Id) : null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> AlreadyInvoicedAsync(Guid organisationId, IReadOnlyCollection<Guid> garmentJobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(garmentJobIds);

        if (garmentJobIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var wanted = garmentJobIds.ToHashSet();
        var taken = await (
                from line in context.InvoiceLines.AsNoTracking().IgnoreAutoIncludes()
                join invoice in context.Invoices.IgnoreAutoIncludes() on line.InvoiceId equals invoice.Id
                where invoice.OrganisationId == organisationId
                      && invoice.Status != InvoiceStatus.Discarded
                      && !context.InvoiceCancellations.Any(cancellation => cancellation.InvoiceId == invoice.Id)
                      && wanted.Contains(line.GarmentJobId)
                select line.GarmentJobId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return taken.ToHashSet();
    }

    /// <inheritdoc />
    public void Add(Invoice invoice) => context.Invoices.Add(invoice);

    /// <inheritdoc />
    public EntityTag EntityTagOf(Invoice invoice) => context.EntityTagOf(invoice);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(BillingErrors.InvoiceChanged);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OneLiveInvoicePerJobIndex,
            })
        {
            // Two drafts for one garment job in the same moment: the handler's read saw neither.
            return Result.Failure(BillingErrors.JobAlreadyInvoiced);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: BillingDbContext.OneCancellationPerInvoiceIndex,
            })
        {
            return Result.Failure(BillingErrors.InvoiceAlreadyCancelled);
        }
    }

    private static string Encode(DateTimeOffset updatedAt, Guid id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{updatedAt:O}|{id}")));

    private static (DateTimeOffset UpdatedAt, Guid Id) Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (default, Guid.Empty);
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');

            return parts.Length == 2
                   && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt)
                   && Guid.TryParse(parts[1], out var id)
                ? (updatedAt, id)
                : (default, Guid.Empty);
        }
        catch (FormatException)
        {
            // A cursor nobody issued reads as no cursor: the first page, never an error a screen has to explain.
            return (default, Guid.Empty);
        }
    }
}
