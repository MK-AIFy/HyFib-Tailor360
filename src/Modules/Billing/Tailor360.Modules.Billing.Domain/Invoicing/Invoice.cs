using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>
/// A tax document for an order: a draft until posted, posted until cancelled. This slice (#153) holds
/// the draft — created from a calculation snapshot, re-priced on every edit, discarded when abandoned.
/// Posting, numbering and the freeze arrive with E09-F02-2, which is why nothing here moves a row to
/// <see cref="InvoiceStatus.Posted"/>.
/// </summary>
/// <remarks>
/// The lines are copies of what the engine produced, never recomputed here: an invoice prints what was
/// calculated, and the calculation is what the snapshot reproduces (<c>INV-INV-03</c>). Every line
/// names the garment job it charges for, and a job is charged on at most one live invoice, which the
/// store's unique index judges rather than this aggregate.
/// </remarks>
public sealed class Invoice
{
    /// <summary>The longest reason kept.</summary>
    public const int MaximumReasonLength = 500;

    private readonly List<InvoiceLine> _lines = [];
    private readonly List<AdjustmentNote> _notes = [];

    private Invoice()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Invoice(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        Guid orderId,
        string orderNumber,
        InvoiceCustomer customer,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        OrderId = orderId;
        OrderNumber = orderNumber;
        Customer = customer;
        Status = InvoiceStatus.Draft;
        Calculation = new InvoiceCalculation(string.Empty, Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false);
        Totals = new InvoiceTotals(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>The invoice's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that issues it, which is the order's branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The customer.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The order it charges for.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The order's display number.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Draft, posted or discarded.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>The customer as the document names them.</summary>
    public InvoiceCustomer Customer { get; private set; } = null!;

    /// <summary>The configuration the lines were calculated on.</summary>
    public InvoiceCalculation Calculation { get; private set; } = null!;

    /// <summary>The document totals.</summary>
    public InvoiceTotals Totals { get; private set; } = null!;

    /// <summary>How many times the draft's lines were set; the calculation reference carries it.</summary>
    public int Revision { get; private set; }

    /// <summary>The lines, in line-number order.</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>When it was drafted.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who drafted it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When the draft was discarded, or null.</summary>
    public DateTimeOffset? DiscardedAt { get; private set; }

    /// <summary>Who discarded it.</summary>
    public Guid? DiscardedBy { get; private set; }

    /// <summary>Why it was discarded.</summary>
    public string? DiscardReason { get; private set; }

    /// <summary>
    /// The order's revision the draft was made at, or last re-priced at. A draft is posted only while the
    /// order is still at it: a revision that arrived since may have changed what the lines charge for.
    /// </summary>
    public int OrderRevisionNumber { get; private set; }

    /// <summary>The number allocated at posting, or null while a draft; never reused, kept through a cancellation.</summary>
    public string? InvoiceNumber { get; private set; }

    /// <summary>The opaque <c>I-</c> barcode payload minted at posting, or null while a draft.</summary>
    public string? BarcodePayload { get; private set; }

    /// <summary>The financial year the number was drawn in, as the number's four-digit token.</summary>
    public string? FinancialYear { get; private set; }

    /// <summary>The branch-local date the invoice was posted on, which decides its financial year.</summary>
    public DateOnly? PostedOn { get; private set; }

    /// <summary>When it was posted.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>Who posted it.</summary>
    public Guid? PostedBy { get; private set; }

    /// <summary>The cancellation, once one has been appended; the invoice's own row never changes for it.</summary>
    public InvoiceCancellation? Cancellation { get; private set; }

    /// <summary>The credit and debit notes posted against the invoice, oldest first.</summary>
    public IReadOnlyList<AdjustmentNote> Notes => _notes;

    /// <summary>Whether it may still change.</summary>
    public bool IsDraft => Status == InvoiceStatus.Draft;

    /// <summary>True once posted: numbered and frozen.</summary>
    public bool IsPosted => Status == InvoiceStatus.Posted;

    /// <summary>True once a cancellation has been appended. Derived: the status column still says posted.</summary>
    public bool IsCancelled => Cancellation is not null;

    /// <summary>The garment jobs the lines charge for.</summary>
    public IEnumerable<Guid> GarmentJobIds => _lines.Select(line => line.GarmentJobId).Distinct();

    /// <summary>Drafts an invoice with its first set of lines.</summary>
    public static Result<Invoice> CreateDraft(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        Guid orderId,
        string orderNumber,
        InvoiceCustomer customer,
        InvoiceCalculation calculation,
        IReadOnlyList<InvoicedLine> lines,
        InvoiceTotals totals,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var checkedCustomer = CheckCustomer(customer);
        if (checkedCustomer.IsFailure)
        {
            return Result.Failure<Invoice>(checkedCustomer.Error);
        }

        var invoice = new Invoice(id, organisationId, branchId, customerId, orderId, orderNumber, customer, now, by);
        var set = invoice.SetLines(calculation, lines, totals, now, by);

        return set.IsFailure ? Result.Failure<Invoice>(set.Error) : Result.Success(invoice);
    }

    /// <summary>Replaces the draft's lines with a fresh calculation's.</summary>
    public Result SetLines(InvoiceCalculation calculation, IReadOnlyList<InvoicedLine> lines, InvoiceTotals totals, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(totals);

        if (!IsDraft)
        {
            return Result.Failure(BillingErrors.InvoiceNotEditable);
        }

        if (lines.Count == 0)
        {
            return Result.Failure(BillingErrors.LinesRequired);
        }

        if (lines.Any(line => line.GarmentJobId == Guid.Empty))
        {
            return Result.Failure(BillingErrors.LineNotAGarmentJob("lines"));
        }

        // One line per garment job: the line's identity in the store is the job it charges for, which is
        // what lets a re-price keep a job's row where it is rather than moving the job between rows.
        if (lines.Select(line => line.GarmentJobId).Distinct().Count() != lines.Count)
        {
            return Result.Failure(BillingErrors.JobRepeated("lines"));
        }

        _lines.Clear();
        var number = 0;
        foreach (var line in lines)
        {
            _lines.Add(InvoiceLine.For(Id, ++number, line));
        }

        Calculation = calculation;
        Totals = totals;
        Revision++;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Posts a draft: numbered, given its barcode, frozen. Everything about the figures was checked by the
    /// caller against the stored calculation before the number was drawn; nothing here recomputes.
    /// </summary>
    /// <param name="invoiceNumber">The number allocated under the sequence lock.</param>
    /// <param name="barcodePayload">The <c>I-</c> payload minted for it.</param>
    /// <param name="financialYear">The financial year token the number was drawn in.</param>
    /// <param name="postedOn">The branch-local date.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    public Result Post(string invoiceNumber, string barcodePayload, string financialYear, DateOnly postedOn, DateTimeOffset now, Guid? by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcodePayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(financialYear);

        if (!IsDraft)
        {
            return Result.Failure(BillingErrors.InvoiceNotEditable);
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(BillingErrors.LinesRequired);
        }

        Status = InvoiceStatus.Posted;
        InvoiceNumber = invoiceNumber;
        BarcodePayload = barcodePayload;
        FinancialYear = financialYear;
        PostedOn = postedOn;
        PostedAt = now;
        PostedBy = by;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Cancels a posted invoice by appending its cancellation and the credit note that relieves it in full
    /// (<c>INV-INV-02</c>): the number, the lines and the totals stay exactly as posted.
    /// </summary>
    /// <param name="cancellationId">The record's identifier.</param>
    /// <param name="creditNoteId">The credit note's identifier.</param>
    /// <param name="creditNoteNumber">The number allocated for the credit note.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    /// <returns>The credit note posted with it.</returns>
    public Result<AdjustmentNote> Cancel(Guid cancellationId, Guid creditNoteId, string creditNoteNumber, string? reason, DateTimeOffset now, Guid? by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creditNoteNumber);

        if (!IsPosted)
        {
            return Result.Failure<AdjustmentNote>(BillingErrors.InvoiceNotPosted);
        }

        if (IsCancelled)
        {
            return Result.Failure<AdjustmentNote>(BillingErrors.InvoiceAlreadyCancelled);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return Result.Failure<AdjustmentNote>(reasoned.Error);
        }

        var note = AdjustmentNote.ForCancellation(creditNoteId, this, creditNoteNumber, reason!.Trim(), now, by);
        _notes.Add(note);
        Cancellation = new InvoiceCancellation(cancellationId, this, creditNoteId, reason.Trim(), now, by);

        return Result.Success(note);
    }

    /// <summary>Posts a credit or debit note against a posted, uncancelled invoice.</summary>
    /// <param name="noteId">The note's identifier.</param>
    /// <param name="kind">Credit or debit.</param>
    /// <param name="number">The number allocated for it.</param>
    /// <param name="lines">The garment jobs and the taxable value each moves.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    public Result<AdjustmentNote> PostNote(Guid noteId, AdjustmentNoteKind kind, string number, IReadOnlyList<AdjustmentNoteLineRequest> lines, string? reason, DateTimeOffset now, Guid? by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (!IsPosted)
        {
            return Result.Failure<AdjustmentNote>(BillingErrors.InvoiceNotPosted);
        }

        if (IsCancelled)
        {
            return Result.Failure<AdjustmentNote>(BillingErrors.InvoiceAlreadyCancelled);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return Result.Failure<AdjustmentNote>(reasoned.Error);
        }

        var posted = AdjustmentNote.Post(noteId, this, kind, number, lines, reason!.Trim(), now, by);
        if (posted.IsFailure)
        {
            return posted;
        }

        _notes.Add(posted.Value);

        return posted;
    }

    /// <summary>Records the order's revision the draft's lines were made against.</summary>
    /// <param name="revisionNumber">The order's revision, from Billing's own order fact.</param>
    public void StampOrderRevision(int revisionNumber)
    {
        if (IsDraft)
        {
            OrderRevisionNumber = revisionNumber;
        }
    }

    /// <summary>The taxable value of a line that credit notes have not yet relieved.</summary>
    /// <param name="garmentJobId">The line, by its job.</param>
    public Money RemainingTaxableValueOf(Guid garmentJobId)
    {
        var line = _lines.Find(candidate => candidate.GarmentJobId == garmentJobId);
        if (line is null)
        {
            return Money.Zero;
        }

        var credited = _notes
            .Where(note => note.Kind == AdjustmentNoteKind.Credit)
            .SelectMany(note => note.Lines)
            .Where(noteLine => noteLine.GarmentJobId == garmentJobId)
            .Aggregate(Money.Zero, (total, noteLine) => total + noteLine.TaxableValue);

        return line.TaxableValue - credited;
    }

    /// <summary>Abandons the draft, freeing its garment jobs for another invoice.</summary>
    public Result Discard(DateTimeOffset now, Guid? by, string? reason)
    {
        if (!IsDraft)
        {
            return Result.Failure(BillingErrors.InvoiceNotEditable);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = InvoiceStatus.Discarded;
        DiscardedAt = now;
        DiscardedBy = by;
        DiscardReason = reason!.Trim();
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Checks a reason where one is required.</summary>
    public static Result CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BillingErrors.ReasonRequired);
        }

        return reason.Trim().Length > MaximumReasonLength
            ? Result.Failure(BillingErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private static Result CheckCustomer(InvoiceCustomer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.DisplayName) || string.IsNullOrWhiteSpace(customer.CustomerNumber))
        {
            return Result.Failure(BillingErrors.Required("customer"));
        }

        return new[] { customer.DisplayName, customer.CustomerNumber, customer.AddressLine, customer.Locality, customer.Postcode }
            .Any(value => value is { Length: > InvoiceCustomer.MaximumLength })
            ? Result.Failure(BillingErrors.TooLong("customer", InvoiceCustomer.MaximumLength))
            : Result.Success();
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}

/// <summary>One charged line of an invoice, carrying the garment job it relates to.</summary>
public sealed class InvoiceLine
{
    private readonly List<InvoiceLineSurcharge> _surcharges = [];
    private readonly List<InvoiceTaxComponent> _taxes = [];

    private InvoiceLine()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private InvoiceLine(Guid invoiceId, int lineNumber, InvoicedLine line)
    {
        InvoiceId = invoiceId;
        LineNumber = lineNumber;
        GarmentJobId = line.GarmentJobId;
        LineKey = line.LineKey;
        ItemCode = line.ItemCode;
        Description = line.Description;
        Quantity = line.Quantity;
        CatalogueRate = line.CatalogueRate;
        AppliedRate = line.AppliedRate;
        Base = line.Base;
        DiscountRuleCode = line.DiscountRuleCode;
        DiscountKind = line.DiscountKind;
        DiscountValue = line.DiscountValue;
        DiscountAmount = line.DiscountAmount;
        Gross = line.Gross;
        TaxableValue = line.TaxableValue;
        TaxCode = line.TaxCode;
        Classification = line.Classification;
        TaxCodeKind = line.TaxCodeKind;
        TaxTotal = line.TaxTotal;
        LineTotal = line.LineTotal;
        Variance = line.Variance;
        _surcharges.AddRange(line.Surcharges.Select((surcharge, index) => new InvoiceLineSurcharge(invoiceId, line.GarmentJobId, index + 1, surcharge)));
        _taxes.AddRange(line.Taxes.Select(tax => new InvoiceTaxComponent(invoiceId, line.GarmentJobId, tax)));
    }

    public Guid InvoiceId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid GarmentJobId { get; private set; }

    public string LineKey { get; private set; } = string.Empty;

    public string ItemCode { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public decimal CatalogueRate { get; private set; }

    public decimal AppliedRate { get; private set; }

    public Money Base { get; private set; }

    public string? DiscountRuleCode { get; private set; }

    public string? DiscountKind { get; private set; }

    public decimal? DiscountValue { get; private set; }

    public Money DiscountAmount { get; private set; }

    public Money Gross { get; private set; }

    public Money TaxableValue { get; private set; }

    public string TaxCode { get; private set; } = string.Empty;

    public string Classification { get; private set; } = string.Empty;

    public string TaxCodeKind { get; private set; } = string.Empty;

    public Money TaxTotal { get; private set; }

    public Money LineTotal { get; private set; }

    public Money Variance { get; private set; }

    public IReadOnlyList<InvoiceLineSurcharge> Surcharges => _surcharges;

    public IReadOnlyList<InvoiceTaxComponent> Taxes => _taxes;

    internal static InvoiceLine For(Guid invoiceId, int lineNumber, InvoicedLine line) => new(invoiceId, lineNumber, line);
}

/// <summary>A surcharge printed under a line.</summary>
public sealed class InvoiceLineSurcharge
{
    private InvoiceLineSurcharge()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal InvoiceLineSurcharge(Guid invoiceId, Guid garmentJobId, int position, InvoicedSurcharge surcharge)
    {
        InvoiceId = invoiceId;
        GarmentJobId = garmentJobId;
        Position = position;
        ItemCode = surcharge.ItemCode;
        Description = surcharge.Description;
        Rate = surcharge.Rate;
        Amount = surcharge.Amount;
    }

    public Guid InvoiceId { get; private set; }

    public Guid GarmentJobId { get; private set; }

    public int Position { get; private set; }

    public string ItemCode { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }

    public Money Amount { get; private set; }
}

/// <summary>One tax component of a line: CGST, SGST, IGST or cess with its rate and amount.</summary>
public sealed class InvoiceTaxComponent
{
    private InvoiceTaxComponent()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal InvoiceTaxComponent(Guid invoiceId, Guid garmentJobId, InvoicedTax tax)
    {
        InvoiceId = invoiceId;
        GarmentJobId = garmentJobId;
        Kind = tax.Kind;
        RatePercent = tax.RatePercent;
        Amount = tax.Amount;
    }

    public Guid InvoiceId { get; private set; }

    public Guid GarmentJobId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public decimal RatePercent { get; private set; }

    public Money Amount { get; private set; }
}
