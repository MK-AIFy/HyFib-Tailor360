using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Invoicing;

/// <summary>
/// The commands a cashier runs against draft invoices (#42, #153): draft one from an order's stored
/// calculation, re-price a draft, discard a draft.
/// </summary>
/// <remarks>
/// <para>
/// Billing holds no reference to Orders (ARCH-010). What it knows about the order came through the
/// outbox into <see cref="OrderFact"/>; what it knows about the price came through
/// <see cref="IPricingService"/>, under the reference the caller names, and is verified to reproduce
/// on the versions it was made on before a single line is copied (<c>INV-INV-03</c>). A figure that no
/// longer reproduces is refused, never re-priced quietly.
/// </para>
/// <para>
/// The customer is copied onto the invoice as the document names them, read through Customers'
/// snapshot contract under the caller's own permissions: a cashier who may not read contact details
/// drafts an invoice without an address on it, and the document says so rather than printing what the
/// cashier could not see.
/// </para>
/// </remarks>
public sealed class InvoiceHandler(
    IInvoiceStore invoices,
    IOrderFactStore orders,
    IGstRegistrationStore registrations,
    IPricingService pricing,
    ICustomerSnapshotQuery customers,
    IBranchDirectory branches,
    IBillingEventPublisher events,
    IOptions<InvoiceOptions> options,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A draft was created.</summary>
    public const string DraftedAction = "billing.invoice.drafted";

    /// <summary>A draft's lines were replaced.</summary>
    public const string UpdatedAction = "billing.invoice.updated";

    /// <summary>A draft was discarded.</summary>
    public const string DiscardedAction = "billing.invoice.discarded";

    /// <summary>A draft was posted: numbered and frozen.</summary>
    public const string PostedAction = "billing.invoice.posted";

    /// <summary>A posted invoice was cancelled by its compensating record.</summary>
    public const string CancelledAction = "billing.invoice.cancelled";

    /// <summary>A credit note was posted.</summary>
    public const string CreditNotePostedAction = "billing.credit_note.posted";

    /// <summary>A debit note was posted.</summary>
    public const string DebitNotePostedAction = "billing.debit_note.posted";

    /// <summary>
    /// Posts a draft (#154): the figures are checked against the calculation the draft was made from, the
    /// number is drawn under the sequence lock in the transaction that freezes the row, the barcode is
    /// minted, and <c>InvoicePosted</c> goes onto the outbox in the same transaction.
    /// </summary>
    public async Task<Result<AdministeredInvoice>> PostAsync(PostInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.InvoiceId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(found.Error);
        }

        var invoice = found.Value;
        if (!invoice.IsDraft)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.InvoiceNotEditable);
        }

        // The order as it stands now: still confirmed, still this branch's, not revised since the draft was
        // made, and every garment the draft charges for still live. What was true at drafting is checked
        // again here, because the outbox may have brought a revision or a cancellation since.
        var order = await orders.FindAsync(invoice.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, invoice.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(checkedOrder.Error);
        }

        if (order!.RevisionNumber != invoice.OrderRevisionNumber)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.OrderRevisedSinceDraft);
        }

        foreach (var line in invoice.Lines)
        {
            var checkedJob = InvoiceLines.CheckJob(line.GarmentJobId, order, $"lines[{line.LineKey}].lineKey");
            if (checkedJob.IsFailure)
            {
                return Result.Failure<AdministeredInvoice>(checkedJob.Error);
            }
        }

        // Recomputed and compared, as the issue asks: the draft's lines are the calculation's, and a draft
        // whose calculation no longer reproduces, or whose figures drifted from it, is not posted.
        var verified = await pricing.VerifySnapshotAsync(command.OrganisationId, invoice.Calculation.Reference, cancellationToken);
        if (verified.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(verified.Error);
        }

        if (!FiguresMatch(invoice, verified.Value.Result))
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.TotalsMismatch);
        }

        var numbering = await NumberingAsync(invoice, cancellationToken);
        if (numbering.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(numbering.Error);
        }

        var (branchCode, financialYear, postedOn) = numbering.Value;
        var scope = DocumentNumbers.SequenceScope(command.OrganisationId, branchCode, financialYear);

        // Minted once per command rather than per attempt: it is how a commit whose outcome the connection
        // lost is told apart from a rival's post of the same draft.
        var barcode = BarcodePayload.Mint(BarcodePayload.InvoiceNamespace).Value;
        var posted = await invoices.PostInTransactionAsync(invoice.Id, command.OrganisationId, barcode, async token =>
        {
            var fresh = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, token);
            if (fresh is null)
            {
                return Result.Failure<Invoice>(BillingErrors.InvoiceNotFound);
            }

            if (!command.ExpectedVersion.Matches(invoices.EntityTagOf(fresh)))
            {
                return Result.Failure<Invoice>(BillingErrors.InvoiceChanged);
            }

            var sequence = await invoices.AllocateAsync(DocumentNumbers.InvoiceSequence, scope, token);
            var number = DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, branchCode, financialYear, sequence);
            if (number.IsFailure)
            {
                return Result.Failure<Invoice>(number.Error);
            }

            var now = clock.UtcNow;
            var post = fresh.Post(number.Value, barcode, financialYear, postedOn, now, command.By);
            if (post.IsFailure)
            {
                return Result.Failure<Invoice>(post.Error);
            }

            events.Publish(new InvoicePosted(
                ids.NewId(), now, fresh.Id, fresh.OrganisationId, fresh.BranchId, fresh.CustomerId, fresh.OrderId, number.Value,
                fresh.Totals.GrandTotal.Amount, fresh.Totals.GrandTotal.Currency));

            var saved = await invoices.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<Invoice>(saved.Error) : Result.Success(fresh);
        }, cancellationToken);
        if (posted.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(posted.Error);
        }

        await BillingAudit.RecordAsync(
            audit, PostedAction, BillingAudit.InvoiceEntity, posted.Value.Id,
            $"Invoice {posted.Value.InvoiceNumber} posted for order {posted.Value.OrderNumber} from calculation '{posted.Value.Calculation.Reference}'.",
            command.Reason, InvoiceSnapshot.Of(invoice), InvoiceSnapshot.Of(posted.Value), cancellationToken);

        return Result.Success(Administered(posted.Value));
    }

    /// <summary>
    /// Cancels a posted invoice (#154): the cancellation is appended and a credit note relieving the whole
    /// amount is posted with it, in one transaction, numbered from the credit-note sequence.
    /// </summary>
    public async Task<Result<AdministeredNote>> CancelAsync(CancelInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // No If-Match here or on a note: nothing on the invoice's row moves for either, so its tag cannot
        // say whether a rival got in first. The transaction below locks the row and re-reads instead.
        var invoice = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceNotFound);
        }

        if (!invoice.IsPosted)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceNotPosted);
        }

        if (invoice.IsCancelled)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceAlreadyCancelled);
        }

        if (options.Value.CancellationWindow is { } window && invoice.PostedAt is { } postedAt && clock.UtcNow - postedAt > window)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.CancellationWindowClosed);
        }

        var numbering = await NumberingAsync(invoice, cancellationToken);
        if (numbering.IsFailure)
        {
            return Result.Failure<AdministeredNote>(numbering.Error);
        }

        var (branchCode, financialYear, _) = numbering.Value;
        var scope = DocumentNumbers.SequenceScope(command.OrganisationId, branchCode, financialYear);
        var creditNoteId = ids.NewId();
        var cancelled = await invoices.AppendInTransactionAsync(invoice.Id, creditNoteId, command.OrganisationId, async token =>
        {
            var fresh = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, token);
            if (fresh is null)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(BillingErrors.InvoiceNotFound);
            }

            var sequence = await invoices.AllocateAsync(DocumentNumbers.CreditNoteSequence, scope, token);
            var number = DocumentNumbers.Compose(DocumentNumbers.CreditNotePrefix, branchCode, financialYear, sequence);
            if (number.IsFailure)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(number.Error);
            }

            var now = clock.UtcNow;
            var cancel = fresh.Cancel(ids.NewId(), creditNoteId, number.Value, command.Reason, now, command.By);
            if (cancel.IsFailure)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(cancel.Error);
            }

            events.Publish(new InvoiceCancelled(
                ids.NewId(), now, fresh.Id, fresh.OrganisationId, fresh.BranchId, fresh.CustomerId, fresh.OrderId, fresh.InvoiceNumber!, creditNoteId));
            events.Publish(new CreditNotePosted(
                ids.NewId(), now, creditNoteId, fresh.OrganisationId, fresh.BranchId, fresh.CustomerId, fresh.Id, number.Value,
                cancel.Value.Totals.GrandTotal.Amount, cancel.Value.Totals.GrandTotal.Currency));

            var saved = await invoices.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<(Invoice, AdjustmentNote)>(saved.Error) : Result.Success((fresh, cancel.Value));
        }, cancellationToken);
        if (cancelled.IsFailure)
        {
            return Result.Failure<AdministeredNote>(cancelled.Error);
        }

        var (after, note) = cancelled.Value;
        await BillingAudit.RecordAsync(
            audit, CancelledAction, BillingAudit.InvoiceEntity, after.Id,
            $"Invoice {after.InvoiceNumber} cancelled; credit note {note.Number} posted for its whole amount.",
            command.Reason, InvoiceSnapshot.Of(invoice), InvoiceSnapshot.Of(after), cancellationToken);

        return Result.Success(new AdministeredNote(after, note, invoices.EntityTagOf(after)));
    }

    /// <summary>Posts a credit or debit note against a posted invoice (#154), numbered from the note's own sequence.</summary>
    public async Task<Result<AdministeredNote>> PostNoteAsync(PostAdjustmentNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoice = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceNotFound);
        }

        if (!invoice.IsPosted)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceNotPosted);
        }

        if (invoice.IsCancelled)
        {
            return Result.Failure<AdministeredNote>(BillingErrors.InvoiceAlreadyCancelled);
        }

        var numbering = await NumberingAsync(invoice, cancellationToken);
        if (numbering.IsFailure)
        {
            return Result.Failure<AdministeredNote>(numbering.Error);
        }

        var (branchCode, financialYear, _) = numbering.Value;
        var scope = DocumentNumbers.SequenceScope(command.OrganisationId, branchCode, financialYear);
        var (sequenceKey, prefix) = command.Kind == AdjustmentNoteKind.Credit
            ? (DocumentNumbers.CreditNoteSequence, DocumentNumbers.CreditNotePrefix)
            : (DocumentNumbers.DebitNoteSequence, DocumentNumbers.DebitNotePrefix);
        var noteId = ids.NewId();
        var posted = await invoices.AppendInTransactionAsync(invoice.Id, noteId, command.OrganisationId, async token =>
        {
            var fresh = await invoices.FindAsync(command.InvoiceId, command.OrganisationId, token);
            if (fresh is null)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(BillingErrors.InvoiceNotFound);
            }

            var sequence = await invoices.AllocateAsync(sequenceKey, scope, token);
            var number = DocumentNumbers.Compose(prefix, branchCode, financialYear, sequence);
            if (number.IsFailure)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(number.Error);
            }

            var now = clock.UtcNow;
            var note = fresh.PostNote(noteId, command.Kind, number.Value, command.Lines, command.Reason, now, command.By);
            if (note.IsFailure)
            {
                return Result.Failure<(Invoice, AdjustmentNote)>(note.Error);
            }

            var total = note.Value.Totals.GrandTotal;
            events.Publish(command.Kind == AdjustmentNoteKind.Credit
                ? new CreditNotePosted(ids.NewId(), now, noteId, fresh.OrganisationId, fresh.BranchId, fresh.CustomerId, fresh.Id, number.Value, total.Amount, total.Currency)
                : new DebitNotePosted(ids.NewId(), now, noteId, fresh.OrganisationId, fresh.BranchId, fresh.CustomerId, fresh.Id, number.Value, total.Amount, total.Currency));

            var saved = await invoices.SaveAsync(token);
            return saved.IsFailure ? Result.Failure<(Invoice, AdjustmentNote)>(saved.Error) : Result.Success((fresh, note.Value));
        }, cancellationToken);
        if (posted.IsFailure)
        {
            return Result.Failure<AdministeredNote>(posted.Error);
        }

        var (after, postedNote) = posted.Value;
        await BillingAudit.RecordAsync(
            audit, command.Kind == AdjustmentNoteKind.Credit ? CreditNotePostedAction : DebitNotePostedAction, BillingAudit.InvoiceEntity, after.Id,
            $"{command.Kind} note {postedNote.Number} posted against invoice {after.InvoiceNumber} for {postedNote.Totals.GrandTotal.Amount} over {postedNote.Lines.Count} line(s).",
            command.Reason, InvoiceSnapshot.Of(invoice), InvoiceSnapshot.Of(after), cancellationToken);

        return Result.Success(new AdministeredNote(after, postedNote, invoices.EntityTagOf(after)));
    }

    /// <summary>The draft's figures against the calculation's: every line total and the document totals, to the paisa.</summary>
    private static bool FiguresMatch(Invoice invoice, PricingResult result)
    {
        if (invoice.Lines.Count != result.Lines.Count || invoice.Totals != InvoiceLines.TotalsOf(result))
        {
            return false;
        }

        foreach (var (line, priced) in invoice.Lines.OrderBy(line => line.LineNumber).Zip(result.Lines))
        {
            if (line.LineKey != priced.LineKey || line.LineTotal != priced.LineTotal || line.TaxableValue != priced.TaxableValue || line.TaxTotal != priced.TaxTotal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What a number is drawn under: the branch's code as the register writes it, and the financial year of
    /// today in the branch's own calendar (<c>docs/architecture/conventions.md</c> section 2.2).
    /// </summary>
    private async Task<Result<(string BranchCode, string FinancialYear, DateOnly Today)>> NumberingAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var branch = await branches.FindAsync(invoice.BranchId, cancellationToken);
        if (branch is null || branch.OrganisationId != invoice.OrganisationId)
        {
            return Result.Failure<(string, string, DateOnly)>(BillingErrors.BranchNotKnown);
        }

        var code = DocumentNumbers.NormaliseBranchCode(branch.Code);
        if (code.IsFailure)
        {
            return Result.Failure<(string, string, DateOnly)>(code.Error);
        }

        var today = clock.TodayIn(TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZoneId));
        return Result.Success((code.Value, DocumentNumbers.FinancialYearToken(today), today));
    }

    /// <summary>Drafts an invoice from an order's stored calculation.</summary>
    public async Task<Result<AdministeredInvoice>> CreateDraftAsync(CreateInvoiceDraftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orders.FindAsync(command.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, command.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(checkedOrder.Error);
        }

        // Trimmed once, so the reference the invoice carries is the key the snapshot is stored under.
        var reference = command.CalculationReference.Trim();
        var verified = await pricing.VerifySnapshotAsync(command.OrganisationId, reference, cancellationToken);
        if (verified.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(verified.Error);
        }

        // A figure priced under another branch's list is not this branch's document, however well it reproduces.
        if (verified.Value.Request.BranchId != order!.BranchId)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.CalculationForAnotherBranch);
        }

        var lines = InvoiceLines.From(verified.Value.Result, order);
        if (lines.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(lines.Error);
        }

        var jobIds = lines.Value.Select(line => line.GarmentJobId).Distinct().ToArray();
        if (command.GarmentJobIds is { Count: > 0 } wanted && !wanted.ToHashSet().SetEquals(jobIds))
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.LineNotAGarmentJob("garmentJobIds"));
        }

        if ((await invoices.AlreadyInvoicedAsync(command.OrganisationId, jobIds, cancellationToken)).Count > 0)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.JobAlreadyInvoiced);
        }

        var calculation = await CalculationOf(verified.Value.Result, reference, verified.Value.Request.PlaceOfSupplyStateCode, command.OrganisationId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(calculation.Error);
        }

        var customer = await customers.GetAsync(order.CustomerId, command.CallerPermissions, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.CustomerNotFound);
        }

        var created = Invoice.CreateDraft(
            ids.NewId(), command.OrganisationId, order.BranchId, order.CustomerId, order.OrderId, order.OrderNumber,
            new InvoiceCustomer(customer.CustomerNumber, customer.DisplayName, customer.AddressLine, customer.Locality, customer.Postcode),
            calculation.Value, lines.Value, InvoiceLines.TotalsOf(verified.Value.Result), clock.UtcNow, command.By);
        if (created.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(created.Error);
        }

        created.Value.StampOrderRevision(order.RevisionNumber);
        invoices.Add(created.Value);
        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, DraftedAction, BillingAudit.InvoiceEntity, created.Value.Id,
            $"Invoice drafted for order {order.OrderNumber} with {created.Value.Lines.Count} line(s) from calculation "
            + $"'{reference}'.",
            command.Reason, null, InvoiceSnapshot.Of(created.Value), cancellationToken);

        return Result.Success(Administered(created.Value));
    }

    /// <summary>Replaces a draft's lines by pricing them afresh.</summary>
    public async Task<Result<AdministeredInvoice>> RepriceAsync(RepriceInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.InvoiceId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(found.Error);
        }

        var invoice = found.Value;
        if (!invoice.IsDraft)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.InvoiceNotEditable);
        }

        var order = await orders.FindAsync(invoice.OrderId, command.OrganisationId, cancellationToken);
        var checkedOrder = CheckOrder(order, invoice.BranchId);
        if (checkedOrder.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(checkedOrder.Error);
        }

        // Everything that can be refused about the lines is refused before a figure is stored: the engine
        // keeps every calculation it makes, so a refusal after pricing would leave a snapshot behind.
        var wantedJobs = InvoiceLines.JobsOf(command.Lines, order!);
        if (wantedJobs.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(wantedJobs.Error);
        }

        var newJobs = wantedJobs.Value.Except(invoice.GarmentJobIds).ToArray();
        if (newJobs.Length > 0 && (await invoices.AlreadyInvoicedAsync(command.OrganisationId, newJobs, cancellationToken)).Count > 0)
        {
            return Result.Failure<AdministeredInvoice>(BillingErrors.JobAlreadyInvoiced);
        }

        // A fresh calculation under a reference of the invoice's own, stored like every other. The
        // revision says which edit it was; the attempt token keeps an edit that failed after this point
        // — a lost race on the row, say — from leaving a snapshot the next edit would collide with.
        var reference = $"invoice:{invoice.Id:N}:{invoice.Revision + 1}:{ids.NewId():N}";
        var priced = await pricing.PriceAsync(
            new PricingRequest(command.OrganisationId, invoice.BranchId, command.On, command.PlaceOfSupplyStateCode, reference, command.Lines),
            cancellationToken);
        if (priced.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(priced.Error);
        }

        var lines = InvoiceLines.From(priced.Value, order!);
        if (lines.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(lines.Error);
        }

        var calculation = await CalculationOf(priced.Value, reference, command.PlaceOfSupplyStateCode, command.OrganisationId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(calculation.Error);
        }

        var before = InvoiceSnapshot.Of(invoice);
        var set = invoice.SetLines(calculation.Value, lines.Value, InvoiceLines.TotalsOf(priced.Value), clock.UtcNow, command.By);
        if (set.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(set.Error);
        }

        invoice.StampOrderRevision(order!.RevisionNumber);

        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, UpdatedAction, BillingAudit.InvoiceEntity, invoice.Id,
            $"Invoice draft re-priced as revision {invoice.Revision} with {invoice.Lines.Count} line(s) from calculation '{reference}'.",
            command.Reason, before, InvoiceSnapshot.Of(invoice), cancellationToken);

        return Result.Success(Administered(invoice));
    }

    /// <summary>Discards a draft.</summary>
    public async Task<Result<AdministeredInvoice>> DiscardAsync(DiscardInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.InvoiceId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(found.Error);
        }

        var invoice = found.Value;
        var before = InvoiceSnapshot.Of(invoice);
        var discarded = invoice.Discard(clock.UtcNow, command.By, command.Reason);
        if (discarded.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(discarded.Error);
        }

        var saved = await invoices.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredInvoice>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, DiscardedAction, BillingAudit.InvoiceEntity, invoice.Id,
            $"Invoice draft for order {invoice.OrderNumber} discarded.",
            command.Reason, before, InvoiceSnapshot.Of(invoice), cancellationToken);

        return Result.Success(Administered(invoice));
    }

    /// <summary>What every command asks of the order: heard of, confirmed, not cancelled, and the caller's branch's.</summary>
    private static Result CheckOrder(OrderFact? order, Guid branchId)
    {
        if (order is null)
        {
            return Result.Failure(BillingErrors.OrderNotKnown);
        }

        if (order.CustomerId == Guid.Empty)
        {
            // Its garments arrived ahead of its confirmation; the customer is not known yet.
            return Result.Failure(BillingErrors.OrderNotYetConfirmed);
        }

        if (!order.IsInvoiceable)
        {
            return Result.Failure(BillingErrors.OrderCancelled);
        }

        return order.BranchId == branchId ? Result.Success() : Result.Failure(BillingErrors.OrderAtAnotherBranch);
    }

    private async Task<Result<InvoiceCalculation>> CalculationOf(PricingResult result, string reference, string placeOfSupply, Guid organisationId, CancellationToken cancellationToken)
    {
        var registration = await registrations.FindAsync(result.GstRegistrationId, organisationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<InvoiceCalculation>(BillingErrors.ConfigurationMissing(
                "The registration the calculation was made against is not one of this organisation.", "calculationReference"));
        }

        return Result.Success(new InvoiceCalculation(
            reference, result.PriceListVersionId, result.TaxConfigurationVersionId, registration.Id, registration.Gstin,
            registration.StateCode, placeOfSupply, result.Scheme.ToString(), result.TaxInclusive));
    }

    private async Task<Result<Invoice>> LoadForChangeAsync(Guid invoiceId, Guid organisationId, EntityTag expectedVersion, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindAsync(invoiceId, organisationId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<Invoice>(BillingErrors.InvoiceNotFound);
        }

        return expectedVersion.Matches(invoices.EntityTagOf(invoice))
            ? Result.Success(invoice)
            : Result.Failure<Invoice>(BillingErrors.InvoiceChanged);
    }

    private AdministeredInvoice Administered(Invoice invoice) => new(invoice, invoices.EntityTagOf(invoice));
}

/// <summary>Draft an invoice from an order's stored calculation.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The branch the caller is working in; it must be the order's.</param>
/// <param name="OrderId">The order.</param>
/// <param name="CalculationReference">The reference the order's calculation was stored under.</param>
/// <param name="GarmentJobIds">The jobs the invoice must cover, or empty for whatever the calculation priced.</param>
/// <param name="CallerPermissions">The caller's permissions, deciding what of the customer may be copied.</param>
/// <param name="Reason">Why, or null.</param>
/// <param name="By">Who.</param>
public sealed record CreateInvoiceDraftCommand(
    Guid OrganisationId,
    Guid BranchId,
    Guid OrderId,
    string CalculationReference,
    IReadOnlyCollection<Guid> GarmentJobIds,
    IReadOnlyCollection<string> CallerPermissions,
    string? Reason,
    Guid? By);

/// <summary>Replace a draft's lines by pricing them afresh.</summary>
public sealed record RepriceInvoiceCommand(
    Guid InvoiceId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    DateOnly On,
    string PlaceOfSupplyStateCode,
    IReadOnlyList<PricingLineRequest> Lines,
    string? Reason,
    Guid? By);

/// <summary>Discard a draft.</summary>
public sealed record DiscardInvoiceCommand(Guid InvoiceId, Guid OrganisationId, EntityTag ExpectedVersion, string? Reason, Guid? By);

/// <summary>Post a draft: number it and freeze it.</summary>
public sealed record PostInvoiceCommand(Guid InvoiceId, Guid OrganisationId, EntityTag ExpectedVersion, string? Reason, Guid? By);

/// <summary>Cancel a posted invoice by its compensating record and credit note.</summary>
public sealed record CancelInvoiceCommand(Guid InvoiceId, Guid OrganisationId, string? Reason, Guid? By);

/// <summary>Post a credit or debit note against a posted invoice.</summary>
public sealed record PostAdjustmentNoteCommand(
    Guid InvoiceId,
    Guid OrganisationId,
    AdjustmentNoteKind Kind,
    IReadOnlyList<AdjustmentNoteLineRequest> Lines,
    string? Reason,
    Guid? By);

/// <summary>A note posted against an invoice, with the invoice as it now stands and its tag.</summary>
public sealed record AdministeredNote(Invoice Invoice, AdjustmentNote Note, EntityTag Tag);

/// <summary>An invoice with the tag its row carries.</summary>
public sealed record AdministeredInvoice(Invoice Invoice, EntityTag Tag);

/// <summary>What the audit trail records about an invoice: the figures, never the customer's details.</summary>
internal sealed record InvoiceSnapshot(string Status, int Revision, int Lines, decimal GrandTotal, string CalculationReference, string? InvoiceNumber, bool Cancelled, int Notes)
{
    public static InvoiceSnapshot Of(Invoice invoice)
        => new(
            invoice.Status.ToString(), invoice.Revision, invoice.Lines.Count, invoice.Totals.GrandTotal.Amount, invoice.Calculation.Reference,
            invoice.InvoiceNumber, invoice.IsCancelled, invoice.Notes.Count);
}
