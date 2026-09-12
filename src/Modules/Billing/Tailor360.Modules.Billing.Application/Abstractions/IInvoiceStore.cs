using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Reads and writes invoices. An invoice is loaded whole: its lines, their surcharges and their tax components.</summary>
public interface IInvoiceStore
{
    /// <summary>One invoice, tracked for change. An invoice of another organisation is not found.</summary>
    Task<Invoice?> FindAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>A branch's invoices, newest first, without their lines.</summary>
    Task<InvoicePage> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Which of the given garment jobs are already charged on a draft or a posted invoice.</summary>
    Task<IReadOnlySet<Guid>> AlreadyInvoicedAsync(Guid organisationId, IReadOnlyCollection<Guid> garmentJobIds, CancellationToken cancellationToken = default);

    /// <summary>Adds an invoice to the context.</summary>
    void Add(Invoice invoice);

    /// <summary>The invoice's concurrency token.</summary>
    EntityTag EntityTagOf(Invoice invoice);

    /// <summary>
    /// Commits everything left in the context, translating a lost write race into a precondition failure
    /// and a garment job charged twice — two drafts for one job in the same moment — into a conflict.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a list asks for.</summary>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch whose invoices are listed.</param>
/// <param name="Status">Only this status, or null for every status.</param>
/// <param name="Cursor">Where the page starts, from the previous page, or null.</param>
/// <param name="Limit">How many at most.</param>
public sealed record InvoiceListQuery(
    Guid OrganisationId,
    Guid BranchId,
    InvoiceStatus? Status,
    string? Cursor,
    int Limit = InvoiceListQuery.DefaultLimit)
{
    /// <summary>The page size when none is asked for.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page size.</summary>
    public const int MaximumLimit = 50;
}

/// <summary>One page of invoices.</summary>
/// <param name="Invoices">The invoices, newest first, without lines.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
public sealed record InvoicePage(IReadOnlyList<Invoice> Invoices, string? NextCursor);
