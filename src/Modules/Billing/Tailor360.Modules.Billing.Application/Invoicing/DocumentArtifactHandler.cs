using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// The rendered documents (#155): requested when a document is posted, rendered and stored by the worker,
/// streamed to an authorised caller with the access audited, printed through the print queue, and resolved
/// from an <c>I-</c> or an <c>R-</c> barcode payload. The receipt (#169) rides the same pipeline: requested
/// on the recording event, rendered on the roll template, frozen at issue. Rendering happens outside any transaction — the store and the object
/// store are two systems — and the row records exactly what was stored: the key, the size and the SHA-256.
/// </summary>
public sealed partial class DocumentArtifactHandler(
    IDocumentArtifactStore artifacts,
    IInvoiceStore invoices,
    IPaymentStore payments,
    IOrderFactStore orders,
    IPaymentModeStore modes,
    IPricingService pricing,
    IBranchDirectory branches,
    IPdfRenderer renderer,
    IObjectStorage storage,
    IPrintQueue printQueue,
    IOptions<DocumentOptions> options,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    ILogger<DocumentArtifactHandler> logger)
{
    /// <summary>A rendered invoice was downloaded.</summary>
    public const string DownloadedAction = "billing.invoice.downloaded";

    /// <summary>A rendered invoice was sent to the print queue.</summary>
    public const string PrintedAction = "billing.invoice.printed";

    /// <summary>The prefix every document key carries (<c>docs/architecture/module-ownership.md</c>).</summary>
    public const string KeyPrefix = "documents/";

    /// <summary>The print-queue kind of an invoice.</summary>
    public const string InvoicePrintKind = "billing.invoice";

    /// <summary>A rendered receipt was downloaded.</summary>
    public const string ReceiptDownloadedAction = "billing.receipt.downloaded";

    /// <summary>A rendered receipt was sent to the print queue.</summary>
    public const string ReceiptPrintedAction = "billing.receipt.printed";

    /// <summary>The print-queue kind of a receipt: the 80 mm continuous roll (plan D15).</summary>
    public const string ReceiptPrintKind = "billing.receipt";

    /// <summary>
    /// Requests a rendering for a posted document, once: the outbox may deliver the posting event more than
    /// once, and a second request for the same document is nothing.
    /// </summary>
    public async Task RequestAsync(DocumentKind kind, Guid documentId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        if (await artifacts.FindAsync(kind, documentId, organisationId, cancellationToken) is not null)
        {
            return;
        }

        var (branchId, number) = kind switch
        {
            DocumentKind.Invoice => await InvoiceIdentityAsync(documentId, organisationId, cancellationToken),
            DocumentKind.Receipt => await ReceiptIdentityAsync(documentId, organisationId, cancellationToken),
            _ => await NoteIdentityAsync(documentId, organisationId, cancellationToken),
        };
        if (number is null)
        {
            // The document the event names is not here: the event is at least once and the row may be
            // gone only in a test that emptied the table. Left to be retried by the dispatcher.
            throw new InvalidOperationException($"No posted {kind} {documentId} to render.");
        }

        artifacts.Add(DocumentArtifact.Request(ids.NewId(), organisationId, branchId, kind, documentId, number, clock.UtcNow));
    }

    /// <summary>
    /// Renders and stores every pending artefact, at most the batch the options allow and within the time
    /// budget given. A failure — a refusal or an exception, from the renderer, the store or the figures —
    /// is recorded on the row and retried on the next pass; after the bounded attempts the artefact is
    /// failed for good and logged as the operational alert it is (<c>INV-INV-08</c>). One artefact's
    /// failure never touches the next: its row is forgotten by the change tracker before the pass goes on.
    /// </summary>
    /// <param name="budget">How long the pass may run; the job's lease minus a margin. Null for no limit.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many artefacts were completed.</returns>
    public async Task<int> RenderPendingAsync(TimeSpan? budget = null, CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;
        var pending = await artifacts.ListPendingAsync(options.Value.RenderBatchSize, cancellationToken);
        var completed = 0;
        foreach (var artifact in pending)
        {
            if (budget is { } limit && clock.UtcNow - started > limit)
            {
                break;
            }

            Result rendered;
            try
            {
                rendered = await RenderOneAsync(artifact, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A renderer that throws instead of refusing, a model builder tripping on a row it did not
                // expect: counted as an attempt like any refusal, never left to head the next pass forever.
                LogRenderingThrew(logger, artifact.Kind, artifact.DocumentId, exception);
                rendered = Result.Failure(Error.Unavailable("billing.document-render-failed", exception.GetType().Name));
            }

            if (rendered.IsSuccess)
            {
                completed++;
                continue;
            }

            if (artifact.IsCompleted)
            {
                // Stored and completed in memory, and the row's save refused: another worker got there first.
                // The row is theirs; this copy is forgotten and the object it stored is an orphan the store's
                // retention sweeps.
                artifacts.Forget(artifact);
                LogRenderingRetried(logger, artifact.Kind, artifact.DocumentId, artifact.Attempts, rendered.Error.Code);
                continue;
            }

            var failed = artifact.RecordFailure(rendered.Error.Code, clock.UtcNow);
            var saved = await artifacts.SaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                artifacts.Forget(artifact);
            }

            if (failed)
            {
                LogRenderingFailedForGood(logger, artifact.Kind, artifact.DocumentId, rendered.Error.Code);
            }
            else
            {
                LogRenderingRetried(logger, artifact.Kind, artifact.DocumentId, artifact.Attempts, saved.IsFailure ? saved.Error.Code : rendered.Error.Code);
            }
        }

        return completed;
    }

    /// <summary>Opens the rendered invoice for streaming and records the access against the invoice.</summary>
    public async Task<Result<StoredDocument>> OpenInvoiceAsync(Guid invoiceId, Guid organisationId, Guid? by, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.FindAsync(invoiceId, organisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<StoredDocument>(BillingErrors.InvoiceNotFound);
        }

        var opened = await OpenAsync(DocumentKind.Invoice, invoiceId, organisationId, cancellationToken);
        if (opened.IsFailure)
        {
            return opened;
        }

        await BillingAudit.RecordAsync(
            audit, DownloadedAction, BillingAudit.InvoiceEntity, invoiceId,
            $"Invoice {invoice.InvoiceNumber} downloaded ({opened.Value.SizeBytes} bytes, sha256 {opened.Value.Sha256}).",
            null, null, new { opened.Value.Sha256, opened.Value.SizeBytes, Actor = by }, cancellationToken);

        return opened;
    }

    /// <summary>Opens a rendered note for streaming; the access is recorded against its invoice.</summary>
    public async Task<Result<StoredDocument>> OpenNoteAsync(Guid invoiceId, Guid noteId, Guid organisationId, Guid? by, CancellationToken cancellationToken = default)
    {
        var found = await invoices.FindNoteAsync(noteId, organisationId, cancellationToken);
        if (found is not { } pair || pair.Invoice.Id != invoiceId)
        {
            return Result.Failure<StoredDocument>(BillingErrors.DocumentNotFound);
        }

        var kind = pair.Note.Kind == AdjustmentNoteKind.Credit ? DocumentKind.CreditNote : DocumentKind.DebitNote;
        var opened = await OpenAsync(kind, noteId, organisationId, cancellationToken);
        if (opened.IsFailure)
        {
            return opened;
        }

        await BillingAudit.RecordAsync(
            audit, DownloadedAction, BillingAudit.InvoiceEntity, invoiceId,
            $"{pair.Note.Kind} note {pair.Note.Number} downloaded ({opened.Value.SizeBytes} bytes, sha256 {opened.Value.Sha256}).",
            null, null, new { opened.Value.Sha256, opened.Value.SizeBytes, Actor = by }, cancellationToken);

        return opened;
    }

    /// <summary>The most copies one print sends.</summary>
    public const int MaximumCopies = 5;

    /// <summary>Sends the rendered invoice to the branch's print queue and records it.</summary>
    public async Task<Result<Guid>> PrintInvoiceAsync(Guid invoiceId, Guid organisationId, int copies, Guid? by, CancellationToken cancellationToken = default)
    {
        if (copies is < 1 or > MaximumCopies)
        {
            return Result.Failure<Guid>(BillingErrors.CopiesOutOfRange);
        }

        var invoice = await invoices.FindAsync(invoiceId, organisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<Guid>(BillingErrors.InvoiceNotFound);
        }

        var artifact = await artifacts.FindAsync(DocumentKind.Invoice, invoiceId, organisationId, cancellationToken);
        if (artifact is null || !artifact.IsCompleted)
        {
            return Result.Failure<Guid>(BillingErrors.DocumentNotAvailable);
        }

        var jobId = await printQueue.EnqueueAsync(new PrintJobRequest(invoice.BranchId, InvoicePrintKind, artifact.ObjectKey!, copies), cancellationToken);
        await BillingAudit.RecordAsync(
            audit, PrintedAction, BillingAudit.InvoiceEntity, invoiceId,
            $"Invoice {invoice.InvoiceNumber} sent to the print queue as job {jobId}.",
            null, null, new { PrintJobId = jobId, Copies = copies, Actor = by }, cancellationToken);

        return Result.Success(jobId);
    }

    /// <summary>
    /// The invoice an <c>I-</c> payload resolves to, for a caller working in its branch; a payload of
    /// another branch's invoice, of another organisation's, or of nothing at all reads alike as nothing.
    /// </summary>
    public async Task<Invoice?> ResolveBarcodeAsync(string payload, Guid organisationId, Guid branchId, CancellationToken cancellationToken = default)
    {
        if (!Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.TryParse(payload, Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.InvoiceNamespace, out var parsed))
        {
            return null;
        }

        var invoice = await invoices.FindByBarcodeAsync(parsed.Value, organisationId, cancellationToken);
        return invoice is { } found && found.BranchId == branchId ? found : null;
    }

    /// <summary>Opens the rendered receipt for streaming and records the access against it.</summary>
    public async Task<Result<StoredDocument>> OpenReceiptAsync(Guid receiptId, Guid organisationId, Guid? by, CancellationToken cancellationToken = default)
    {
        var receipt = await payments.FindReceiptAsync(receiptId, organisationId, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure<StoredDocument>(BillingErrors.ReceiptNotFound);
        }

        var opened = await OpenAsync(DocumentKind.Receipt, receiptId, organisationId, cancellationToken);
        if (opened.IsFailure)
        {
            return opened;
        }

        await BillingAudit.RecordAsync(
            audit, ReceiptDownloadedAction, BillingAudit.ReceiptEntity, receiptId,
            $"Receipt {receipt.ReceiptNumber} downloaded ({opened.Value.SizeBytes} bytes, sha256 {opened.Value.Sha256}).",
            null, null, new { opened.Value.Sha256, opened.Value.SizeBytes, Actor = by }, cancellationToken);

        return opened;
    }

    /// <summary>Sends the rendered receipt to the branch's print queue, on the receipt roll, and records it.</summary>
    public async Task<Result<Guid>> PrintReceiptAsync(Guid receiptId, Guid organisationId, int copies, Guid? by, CancellationToken cancellationToken = default)
    {
        if (copies is < 1 or > MaximumCopies)
        {
            return Result.Failure<Guid>(BillingErrors.CopiesOutOfRange);
        }

        var receipt = await payments.FindReceiptAsync(receiptId, organisationId, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure<Guid>(BillingErrors.ReceiptNotFound);
        }

        var artifact = await artifacts.FindAsync(DocumentKind.Receipt, receiptId, organisationId, cancellationToken);
        if (artifact is null || !artifact.IsCompleted)
        {
            return Result.Failure<Guid>(BillingErrors.DocumentNotAvailable);
        }

        var jobId = await printQueue.EnqueueAsync(new PrintJobRequest(receipt.BranchId, ReceiptPrintKind, artifact.ObjectKey!, copies), cancellationToken);
        await BillingAudit.RecordAsync(
            audit, ReceiptPrintedAction, BillingAudit.ReceiptEntity, receiptId,
            $"Receipt {receipt.ReceiptNumber} sent to the print queue as job {jobId}.",
            null, null, new { PrintJobId = jobId, Copies = copies, Actor = by }, cancellationToken);

        return Result.Success(jobId);
    }

    /// <summary>
    /// The receipt an <c>R-</c> payload resolves to, for a caller working in the branch that issued it; any
    /// other payload, another branch's receipt or nothing at all reads alike as nothing.
    /// </summary>
    public async Task<Receipt?> ResolveReceiptBarcodeAsync(string payload, Guid organisationId, Guid branchId, CancellationToken cancellationToken = default)
    {
        if (!Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.TryParse(payload, Tailor360.Platform.Abstractions.Barcodes.BarcodePayload.ReceiptNamespace, out var parsed))
        {
            return null;
        }

        var receipt = await payments.FindReceiptByBarcodeAsync(parsed.Value, organisationId, cancellationToken);
        return receipt is { } found && found.BranchId == branchId ? found : null;
    }

    private async Task<Result<StoredDocument>> OpenAsync(DocumentKind kind, Guid documentId, Guid organisationId, CancellationToken cancellationToken)
    {
        var artifact = await artifacts.FindAsync(kind, documentId, organisationId, cancellationToken);
        if (artifact is null || !artifact.IsCompleted)
        {
            return Result.Failure<StoredDocument>(BillingErrors.DocumentNotAvailable);
        }

        var stream = await storage.OpenReadAsync(artifact.ObjectKey!, cancellationToken);
        if (stream is null)
        {
            // The row says stored and the store says nothing: the alert INV-INV-08 describes, answered to
            // the caller as not available rather than as a five hundred.
            LogObjectMissing(logger, artifact.Kind, artifact.DocumentId, artifact.ObjectKey!);
            return Result.Failure<StoredDocument>(BillingErrors.DocumentNotAvailable);
        }

        return Result.Success(new StoredDocument(stream, artifact.ContentType ?? DocumentArtifact.PdfContentType, artifact.DocumentNumber, artifact.SizeBytes ?? 0, artifact.Sha256 ?? string.Empty));
    }

    private async Task<Result> RenderOneAsync(DocumentArtifact artifact, CancellationToken cancellationToken)
    {
        var model = await ModelAsync(artifact, cancellationToken);
        if (model.IsFailure)
        {
            return Result.Failure(model.Error);
        }

        using var buffer = new MemoryStream();
        var rendered = await renderer.RenderAsync(model.Value.Template, model.Value.Model, buffer, cancellationToken);
        if (rendered.IsFailure)
        {
            return rendered;
        }

        if (buffer.Length == 0)
        {
            return Result.Failure(Error.Unavailable("billing.document-empty", "The renderer produced no bytes."));
        }

        buffer.Position = 0;
        var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;

        // Opaque: the identifier is fresh and says nothing about the document (conventions.md section 3.5).
        var key = $"{KeyPrefix}{ids.NewId():N}";
        try
        {
            await storage.PutAsync(key, buffer, DocumentArtifact.PdfContentType, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreFailed(logger, artifact.Kind, artifact.DocumentId, exception);
            return Result.Failure(Error.Unavailable("billing.document-store-unavailable", "The object store refused the document."));
        }

        var completed = artifact.Complete(key, DocumentArtifact.PdfContentType, buffer.Length, checksum, clock.UtcNow);
        if (completed.IsFailure)
        {
            return completed;
        }

        var saved = await artifacts.SaveAsync(cancellationToken);
        if (saved.IsSuccess)
        {
            LogRendered(logger, artifact.Kind, artifact.DocumentId, buffer.Length);
        }

        return saved;
    }

    private async Task<Result<(string Template, IReadOnlyDictionary<string, object?> Model)>> ModelAsync(DocumentArtifact artifact, CancellationToken cancellationToken)
    {
        if (artifact.Kind == DocumentKind.Receipt)
        {
            return await ReceiptModelAsync(artifact, cancellationToken);
        }

        Invoice? invoice;
        AdjustmentNote? note = null;
        if (artifact.Kind == DocumentKind.Invoice)
        {
            invoice = await invoices.FindAsync(artifact.DocumentId, artifact.OrganisationId, cancellationToken);
        }
        else
        {
            var found = await invoices.FindNoteAsync(artifact.DocumentId, artifact.OrganisationId, cancellationToken);
            invoice = found?.Invoice;
            note = found?.Note;
        }

        if (invoice is null || !invoice.IsPosted)
        {
            return Result.Failure<(string, IReadOnlyDictionary<string, object?>)>(BillingErrors.InvoiceNotPosted);
        }

        // The figures rendered are the figures posted: the calculation the invoice was drafted from must
        // still reproduce, and the invoice must still equal it, before a document says them (INV-INV-06).
        var verified = await pricing.VerifySnapshotAsync(artifact.OrganisationId, invoice.Calculation.Reference, cancellationToken);
        if (verified.IsFailure)
        {
            return Result.Failure<(string, IReadOnlyDictionary<string, object?>)>(verified.Error);
        }

        if (!InvoiceLines.FiguresMatch(invoice, verified.Value.Result))
        {
            return Result.Failure<(string, IReadOnlyDictionary<string, object?>)>(BillingErrors.TotalsMismatch);
        }

        var branch = await branches.FindAsync(invoice.BranchId, cancellationToken);
        var branchName = branch?.Name ?? branch?.Code ?? string.Empty;

        return artifact.Kind switch
        {
            DocumentKind.Invoice => Result.Success(("billing.invoice", DocumentModels.Invoice(invoice, branchName, options.Value.Terms))),
            DocumentKind.CreditNote => Result.Success(("billing.credit-note", DocumentModels.Note(invoice, note!, branchName))),
            _ => Result.Success(("billing.debit-note", DocumentModels.Note(invoice, note!, branchName))),
        };
    }

    /// <summary>
    /// The receipt's model: the figures frozen on the receipt at issue — not the balance as it stands now —
    /// with the invoice numbers its allocations name, the order's number, the mode's name and the branch.
    /// </summary>
    private async Task<Result<(string Template, IReadOnlyDictionary<string, object?> Model)>> ReceiptModelAsync(DocumentArtifact artifact, CancellationToken cancellationToken)
    {
        var receipt = await payments.FindReceiptAsync(artifact.DocumentId, artifact.OrganisationId, cancellationToken);
        var payment = receipt is null ? null : await payments.FindAsync(receipt.PaymentId, artifact.OrganisationId, cancellationToken);
        if (receipt is null || payment is null)
        {
            return Result.Failure<(string, IReadOnlyDictionary<string, object?>)>(BillingErrors.ReceiptNotFound);
        }

        var numbers = (await invoices.ListPostedForOrderAsync(payment.OrderId, artifact.OrganisationId, cancellationToken))
            .ToDictionary(invoice => invoice.Id, invoice => invoice.InvoiceNumber ?? string.Empty);
        var order = await orders.FindAsync(payment.OrderId, artifact.OrganisationId, cancellationToken);
        var mode = (await modes.ListAsync(artifact.OrganisationId, cancellationToken)).FirstOrDefault(candidate => string.Equals(candidate.Code, payment.ModeCode, StringComparison.Ordinal));
        var branch = await branches.FindAsync(receipt.BranchId, cancellationToken);

        return Result.Success((ReceiptPrintKind, DocumentModels.Receipt(receipt, payment, numbers, order?.OrderNumber ?? string.Empty, mode?.Name ?? payment.ModeCode, branch?.Name ?? branch?.Code ?? string.Empty)));
    }

    private async Task<(Guid BranchId, string? Number)> ReceiptIdentityAsync(Guid receiptId, Guid organisationId, CancellationToken cancellationToken)
    {
        var receipt = await payments.FindReceiptAsync(receiptId, organisationId, cancellationToken);
        return receipt is not null ? (receipt.BranchId, receipt.ReceiptNumber) : (Guid.Empty, null);
    }

    private async Task<(Guid BranchId, string? Number)> InvoiceIdentityAsync(Guid invoiceId, Guid organisationId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, organisationId, cancellationToken);
        return invoice is { IsPosted: true } ? (invoice.BranchId, invoice.InvoiceNumber) : (Guid.Empty, null);
    }

    private async Task<(Guid BranchId, string? Number)> NoteIdentityAsync(Guid noteId, Guid organisationId, CancellationToken cancellationToken)
    {
        var found = await invoices.FindNoteAsync(noteId, organisationId, cancellationToken);
        return found is { } pair ? (pair.Invoice.BranchId, pair.Note.Number) : (Guid.Empty, null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendered {Kind} {DocumentId}: {SizeBytes} bytes stored.")]
    private static partial void LogRendered(ILogger logger, DocumentKind kind, Guid documentId, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendering {Kind} {DocumentId} failed on attempt {Attempts} with {Code}; it will be retried.")]
    private static partial void LogRenderingRetried(ILogger logger, DocumentKind kind, Guid documentId, int attempts, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rendering {Kind} {DocumentId} failed for good with {Code}: the document is posted and has no artefact (INV-INV-08). Operator action needed; never re-post.")]
    private static partial void LogRenderingFailedForGood(ILogger logger, DocumentKind kind, Guid documentId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rendering {Kind} {DocumentId} threw rather than refused; counted as a failed attempt.")]
    private static partial void LogRenderingThrew(ILogger logger, DocumentKind kind, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storing {Kind} {DocumentId} in the object store failed.")]
    private static partial void LogStoreFailed(ILogger logger, DocumentKind kind, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The artefact of {Kind} {DocumentId} says stored under {ObjectKey}, and the object store holds nothing there (INV-INV-08).")]
    private static partial void LogObjectMissing(ILogger logger, DocumentKind kind, Guid documentId, string objectKey);
}

/// <summary>A rendered document ready to stream: the bytes, the media type, the number for the file name, and what the row recorded.</summary>
public sealed record StoredDocument(Stream Content, string ContentType, string DocumentNumber, long SizeBytes, string Sha256);
