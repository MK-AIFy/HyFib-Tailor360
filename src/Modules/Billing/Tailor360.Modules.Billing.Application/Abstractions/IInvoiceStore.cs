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

    /// <summary>A note and the invoice it belongs to, tracked, or null.</summary>
    Task<(Invoice Invoice, AdjustmentNote Note)?> FindNoteAsync(Guid noteId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The posted invoice a barcode payload resolves to within the organisation, or null.</summary>
    Task<Invoice?> FindByBarcodeAsync(string barcodePayload, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The order's posted invoices with their notes and cancellation, oldest posting first: the order a payment is allocated in.</summary>
    Task<IReadOnlyList<Invoice>> ListPostedForOrderAsync(Guid orderId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Adds an invoice to the context.</summary>
    void Add(Invoice invoice);

    /// <summary>The invoice's concurrency token.</summary>
    EntityTag EntityTagOf(Invoice invoice);

    /// <summary>
    /// Commits everything left in the context, translating a lost write race into a precondition failure
    /// and a garment job charged twice — two drafts for one job in the same moment — into a conflict.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a posting as one transaction on the module's connection: the invoice's row is locked first, so
    /// two commands on one invoice run one after the other and each reads what the other committed; the
    /// work then reloads the invoice, draws its number through <see cref="AllocateAsync"/>, changes it,
    /// publishes and saves, and the whole of it commits or rolls back together — so a number drawn for a
    /// document that did not post returns to the sequence (<c>docs/architecture/conventions.md</c> section
    /// 3.2). The change tracker is emptied before each attempt, and an attempt whose commit outcome is
    /// unknown is judged by whether the invoice now carries the barcode payload this attempt minted.
    /// </summary>
    /// <typeparam name="TOutcome">What the work returns.</typeparam>
    /// <param name="invoiceId">The invoice being posted.</param>
    /// <param name="organisationId">Its organisation.</param>
    /// <param name="barcodePayload">The payload minted for this posting, by which a commit of unknown outcome is recognised.</param>
    /// <param name="work">The posting, run inside the transaction.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<Result<TOutcome>> PostInTransactionAsync<TOutcome>(
        Guid invoiceId,
        Guid organisationId,
        string barcodePayload,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="PostInTransactionAsync{TOutcome}"/>, for a record appended to a posted invoice — a
    /// cancellation with its credit note, a note — the invoice's row locked first, and judged, when the
    /// commit's outcome is unknown, by whether the note with the identifier given exists.
    /// </summary>
    /// <typeparam name="TOutcome">What the work returns.</typeparam>
    /// <param name="invoiceId">The invoice the record is appended to.</param>
    /// <param name="noteId">The note the work posts.</param>
    /// <param name="organisationId">Its organisation.</param>
    /// <param name="work">The append, run inside the transaction.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<Result<TOutcome>> AppendInTransactionAsync<TOutcome>(
        Guid invoiceId,
        Guid noteId,
        Guid organisationId,
        Func<CancellationToken, Task<Result<TOutcome>>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Draws the next number of a sequence inside the transaction one of the two methods above opened,
    /// holding the sequence row's lock until it commits. Outside one it throws: a number drawn outside the
    /// document's own transaction is a number that survives the document's rollback.
    /// </summary>
    /// <param name="sequenceKey">The sequence, from <c>DocumentNumbers</c>.</param>
    /// <param name="scope">The branch and financial year.</param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    Task<long> AllocateAsync(string sequenceKey, string scope, CancellationToken cancellationToken = default);
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
