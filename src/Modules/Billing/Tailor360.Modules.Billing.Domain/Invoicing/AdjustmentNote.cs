using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Invoicing;

/// <summary>Which way a note moves the amount a customer owes.</summary>
public enum AdjustmentNoteKind
{
    /// <summary>Relieves the invoice: the customer owes less.</summary>
    Credit = 0,

    /// <summary>Adds to the invoice: the customer owes more.</summary>
    Debit = 1,
}

/// <summary>
/// A credit or debit note against a posted invoice (#154): numbered from its own sequence, posted once,
/// immutable at the database, and the only way a posted invoice's value is ever corrected — a posted
/// invoice itself never changes. A note is per line: each line names a garment job of the invoice and the
/// taxable value it relieves or adds, and carries that line's own tax components at that line's rates, so a
/// note on a two-rate invoice never has to guess a rate.
/// </summary>
public sealed class AdjustmentNote
{
    private readonly List<AdjustmentNoteLine> _lines = [];

    private AdjustmentNote()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private AdjustmentNote(Guid id, Invoice invoice, AdjustmentNoteKind kind, string number, string reason, DateOnly postedOn, DateTimeOffset now, Guid? by)
    {
        Id = id;
        InvoiceId = invoice.Id;
        OrganisationId = invoice.OrganisationId;
        BranchId = invoice.BranchId;
        Kind = kind;
        Number = number;
        Reason = reason;
        PostedOn = postedOn;
        PostedAt = now;
        PostedBy = by;
        Totals = new InvoiceTotals(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The invoice the note relieves or adds to.</summary>
    public Guid InvoiceId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that issued the invoice, and so the note.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Credit or debit.</summary>
    public AdjustmentNoteKind Kind { get; private set; }

    /// <summary>The display number, from the note's own sequence.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Why the note was posted.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>The note's totals: the taxable value, the tax by component, and the sum.</summary>
    public InvoiceTotals Totals { get; private set; } = null!;

    /// <summary>The branch-local date it was posted on, which the document prints and the financial year is read from.</summary>
    public DateOnly PostedOn { get; private set; }

    /// <summary>When it was posted.</summary>
    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>Who posted it.</summary>
    public Guid? PostedBy { get; private set; }

    /// <summary>The lines, one per garment job named.</summary>
    public IReadOnlyList<AdjustmentNoteLine> Lines => _lines;

    /// <summary>
    /// Posts a note over the lines named, each line taxed at the invoice line's own rates and rounded once
    /// per component, as the engine rounds (<c>docs/architecture/conventions.md</c> section 1.2).
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="invoice">The posted invoice.</param>
    /// <param name="kind">Credit or debit.</param>
    /// <param name="number">The number allocated for it.</param>
    /// <param name="lines">The garment jobs and the taxable value each relieves or adds.</param>
    /// <param name="reason">Why.</param>
    /// <param name="postedOn">The branch-local date.</param>
    /// <param name="now">When.</param>
    /// <param name="by">Who.</param>
    internal static Result<AdjustmentNote> Post(
        Guid id,
        Invoice invoice,
        AdjustmentNoteKind kind,
        string number,
        IReadOnlyList<AdjustmentNoteLineRequest> lines,
        string reason,
        DateOnly postedOn,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Result.Failure<AdjustmentNote>(BillingErrors.LinesRequired);
        }

        var note = new AdjustmentNote(id, invoice, kind, number, reason, postedOn, now, by);
        var number1 = 0;
        var seen = new HashSet<Guid>();
        foreach (var request in lines)
        {
            var field = $"lines[{request.GarmentJobId}]";
            var invoiceLine = invoice.Lines.FirstOrDefault(line => line.GarmentJobId == request.GarmentJobId);
            if (invoiceLine is null)
            {
                return Result.Failure<AdjustmentNote>(BillingErrors.NoteLineNotOnInvoice($"{field}.garmentJobId"));
            }

            if (!seen.Add(request.GarmentJobId))
            {
                return Result.Failure<AdjustmentNote>(BillingErrors.JobRepeated($"{field}.garmentJobId"));
            }

            if (request.TaxableValue <= 0m || request.TaxableValue != decimal.Round(request.TaxableValue, Money.DocumentScale))
            {
                return Result.Failure<AdjustmentNote>(BillingErrors.NoteValueNotWellFormed($"{field}.taxableValue"));
            }

            if (kind == AdjustmentNoteKind.Credit && request.TaxableValue > invoice.RemainingTaxableValueOf(request.GarmentJobId).Amount)
            {
                return Result.Failure<AdjustmentNote>(BillingErrors.NoteExceedsLine($"{field}.taxableValue"));
            }

            note._lines.Add(AdjustmentNoteLine.For(note.Id, ++number1, invoiceLine, Money.Rupees(request.TaxableValue)));
        }

        note.Totals = TotalsOf(note._lines);

        return Result.Success(note);
    }

    /// <summary>The credit note a cancellation posts: every line of the invoice, whole, and the invoice's own totals.</summary>
    internal static AdjustmentNote ForCancellation(Guid id, Invoice invoice, string number, string reason, DateOnly postedOn, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var note = new AdjustmentNote(id, invoice, AdjustmentNoteKind.Credit, number, reason, postedOn, now, by);
        var number1 = 0;
        foreach (var line in invoice.Lines)
        {
            note._lines.Add(AdjustmentNoteLine.Copy(note.Id, ++number1, line));
        }

        // Relieved to the paisa the invoice charged, round-off included, so the two documents net to nothing.
        var totals = invoice.Totals;
        note.Totals = new InvoiceTotals(
            totals.TaxableValue, Money.Zero, totals.TaxableValue, totals.CentralTax, totals.StateTax, totals.IntegratedTax, totals.Cess,
            totals.RoundOff, totals.GrandTotal);

        return note;
    }

    private static InvoiceTotals TotalsOf(IReadOnlyList<AdjustmentNoteLine> lines)
    {
        var taxable = Sum(lines.Select(line => line.TaxableValue));
        var central = Sum(lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == InvoiceTaxKinds.Central).Select(tax => tax.Amount));
        var state = Sum(lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == InvoiceTaxKinds.State).Select(tax => tax.Amount));
        var integrated = Sum(lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == InvoiceTaxKinds.Integrated).Select(tax => tax.Amount));
        var cess = Sum(lines.SelectMany(line => line.Taxes).Where(tax => tax.Kind == InvoiceTaxKinds.Cess).Select(tax => tax.Amount));

        // A note is not rounded off to the rupee: it is a correction, and the correction is to the paisa.
        return new InvoiceTotals(taxable, Money.Zero, taxable, central, state, integrated, cess, Money.Zero, taxable + central + state + integrated + cess);
    }

    private static Money Sum(IEnumerable<Money> amounts) => amounts.Aggregate(Money.Zero, (total, amount) => total + amount);
}

/// <summary>A garment job and the taxable value a note relieves or adds for it.</summary>
/// <param name="GarmentJobId">The job, which must be a line of the invoice.</param>
/// <param name="TaxableValue">The taxable value, positive, to the paisa.</param>
public sealed record AdjustmentNoteLineRequest(Guid GarmentJobId, decimal TaxableValue);

/// <summary>One line of a note: the invoice line it moves, by garment job, and the amounts.</summary>
public sealed class AdjustmentNoteLine
{
    private readonly List<AdjustmentNoteTax> _taxes = [];

    private AdjustmentNoteLine()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private AdjustmentNoteLine(Guid noteId, int lineNumber, Guid garmentJobId, Money taxableValue)
    {
        NoteId = noteId;
        LineNumber = lineNumber;
        GarmentJobId = garmentJobId;
        TaxableValue = taxableValue;
    }

    /// <summary>The note.</summary>
    public Guid NoteId { get; private set; }

    /// <summary>The line's position on the note, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The garment job of the invoice line this moves.</summary>
    public Guid GarmentJobId { get; private set; }

    /// <summary>The taxable value moved.</summary>
    public Money TaxableValue { get; private set; }

    /// <summary>The tax on it, by component.</summary>
    public Money TaxTotal { get; private set; }

    /// <summary>The taxable value and its tax.</summary>
    public Money LineTotal { get; private set; }

    /// <summary>The components, at the invoice line's rates.</summary>
    public IReadOnlyList<AdjustmentNoteTax> Taxes => _taxes;

    internal static AdjustmentNoteLine For(Guid noteId, int lineNumber, InvoiceLine invoiceLine, Money taxableValue)
    {
        var line = new AdjustmentNoteLine(noteId, lineNumber, invoiceLine.GarmentJobId, taxableValue);
        foreach (var component in invoiceLine.Taxes)
        {
            // Once per component, to the paisa, half away from zero — the engine's own rule.
            var amount = Money.Rupees(decimal.Round(taxableValue.Amount * component.RatePercent / 100m, Money.DocumentScale, MidpointRounding.AwayFromZero));
            line._taxes.Add(new AdjustmentNoteTax(noteId, invoiceLine.GarmentJobId, component.Kind, component.RatePercent, amount));
        }

        line.Settle();
        return line;
    }

    internal static AdjustmentNoteLine Copy(Guid noteId, int lineNumber, InvoiceLine invoiceLine)
    {
        var line = new AdjustmentNoteLine(noteId, lineNumber, invoiceLine.GarmentJobId, invoiceLine.TaxableValue);
        foreach (var component in invoiceLine.Taxes)
        {
            line._taxes.Add(new AdjustmentNoteTax(noteId, invoiceLine.GarmentJobId, component.Kind, component.RatePercent, component.Amount));
        }

        line.Settle();
        return line;
    }

    private void Settle()
    {
        TaxTotal = _taxes.Aggregate(Money.Zero, (total, tax) => total + tax.Amount);
        LineTotal = TaxableValue + TaxTotal;
    }
}

/// <summary>One tax component of a note line.</summary>
public sealed class AdjustmentNoteTax
{
    private AdjustmentNoteTax()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal AdjustmentNoteTax(Guid noteId, Guid garmentJobId, string kind, decimal ratePercent, Money amount)
    {
        NoteId = noteId;
        GarmentJobId = garmentJobId;
        Kind = kind;
        RatePercent = ratePercent;
        Amount = amount;
    }

    /// <summary>The note.</summary>
    public Guid NoteId { get; private set; }

    /// <summary>The line, by the garment job it moves.</summary>
    public Guid GarmentJobId { get; private set; }

    /// <summary>The component: CGST, SGST, IGST or CESS.</summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>The rate applied.</summary>
    public decimal RatePercent { get; private set; }

    /// <summary>The amount, rounded once.</summary>
    public Money Amount { get; private set; }
}

/// <summary>The names the engine writes on a tax component, which the totals are summed by.</summary>
public static class InvoiceTaxKinds
{
    /// <summary>Central GST.</summary>
    public const string Central = "CGST";

    /// <summary>State GST.</summary>
    public const string State = "SGST";

    /// <summary>Integrated GST.</summary>
    public const string Integrated = "IGST";

    /// <summary>Compensation cess.</summary>
    public const string Cess = "CESS";
}

/// <summary>
/// The record a cancellation appends (<c>INV-INV-02</c>): the invoice keeps its number and its totals, and its
/// displayed status derives from this row's existence. Immutable at the database.
/// </summary>
public sealed class InvoiceCancellation
{
    private InvoiceCancellation()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal InvoiceCancellation(Guid id, Invoice invoice, Guid creditNoteId, string reason, DateTimeOffset now, Guid? by)
    {
        Id = id;
        InvoiceId = invoice.Id;
        OrganisationId = invoice.OrganisationId;
        BranchId = invoice.BranchId;
        CreditNoteId = creditNoteId;
        Reason = reason;
        CancelledAt = now;
        CancelledBy = by;
    }

    /// <summary>The identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The invoice.</summary>
    public Guid InvoiceId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The credit note posted with the cancellation, relieving the invoice in full.</summary>
    public Guid CreditNoteId { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When.</summary>
    public DateTimeOffset CancelledAt { get; private set; }

    /// <summary>Who.</summary>
    public Guid? CancelledBy { get; private set; }
}
