using Shouldly;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// Posting, cancelling and correcting an invoice in the aggregate (#154): a draft posts once, a posted invoice
/// never changes, a cancellation appends its record and a full credit note, and a note moves value per line at
/// the line's own rates.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InvoicePostingTests
{
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid JobTwo = BillingTestData.Id("job-2");
    private static readonly Guid Cashier = BillingTestData.Id("cashier");

    [Fact]
    public void PostsADraftOnceAndNeverAgain()
    {
        var invoice = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;

        invoice.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();

        invoice.IsPosted.ShouldBeTrue();
        invoice.IsDraft.ShouldBeFalse();
        invoice.IsCancelled.ShouldBeFalse();
        invoice.InvoiceNumber.ShouldBe("INV-MAIN-2627-000001");
        invoice.BarcodePayload.ShouldBe("I-7K3M9QW2XZ4B");
        invoice.FinancialYear.ShouldBe("2627");
        invoice.PostedOn.ShouldBe(new DateOnly(2026, 9, 12));
        invoice.PostedAt.ShouldBe(BillingTestData.Now);
        invoice.PostedBy.ShouldBe(Cashier);

        invoice.Post("INV-MAIN-2627-000002", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.invoice-not-editable");
        invoice.SetLines(InvoiceTests.Calculation("x"), [InvoiceTests.Line(JobTwo, "SKIRT")], InvoiceTests.Totals(567m), BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.invoice-not-editable");
        invoice.Discard(BillingTestData.Now, Cashier, "No.").Error.Code.ShouldBe("billing.invoice-not-editable");
        invoice.InvoiceNumber.ShouldBe("INV-MAIN-2627-000001");
    }

    [Fact]
    public void TheOrderRevisionIsStampedOnADraftAndFrozenWithIt()
    {
        var invoice = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        invoice.OrderRevisionNumber.ShouldBe(0);

        invoice.StampOrderRevision(3);
        invoice.OrderRevisionNumber.ShouldBe(3);

        invoice.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        invoice.StampOrderRevision(4);
        invoice.OrderRevisionNumber.ShouldBe(3, "a posted invoice records the revision it was posted against");
    }

    [Fact]
    public void ADiscardedDraftIsNotPostedAndADraftIsNotCancelledOrCorrected()
    {
        var discarded = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        discarded.Discard(BillingTestData.Now, Cashier, "Wrong order.").IsSuccess.ShouldBeTrue();
        discarded.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.invoice-not-editable");

        var draft = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        draft.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "Wrong customer.", BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.invoice-not-posted");
        draft.PostNote(BillingTestData.Id("note"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 90m)], "Lining twice.", BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.invoice-not-posted");
    }

    [Fact]
    public void CancellationAppendsItsRecordAndAFullCreditNoteAndLeavesTheInvoiceAsPosted()
    {
        var invoice = Posted([InvoiceTests.Line(JobOne, "BLOUSE"), InvoiceTests.Line(JobTwo, "SKIRT")]);
        var later = BillingTestData.Now.AddHours(1);

        invoice.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "  ", later, Cashier).Error.Code.ShouldBe("billing.reason-required");

        var cancelled = invoice.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", " Issued to the wrong customer. ", later, Cashier);

        cancelled.IsSuccess.ShouldBeTrue(cancelled.IsFailure ? cancelled.Error.Message : string.Empty);
        invoice.IsCancelled.ShouldBeTrue();
        invoice.IsPosted.ShouldBeTrue("the status column never moves; cancelled is derived from the record");
        invoice.InvoiceNumber.ShouldBe("INV-MAIN-2627-000001");
        invoice.Totals.GrandTotal.ShouldBe(Money.Rupees(1134m));
        invoice.Lines.Count.ShouldBe(2);
        invoice.UpdatedAt.ShouldBe(BillingTestData.Now, "nothing on the invoice row moves for a cancellation");

        var record = invoice.Cancellation.ShouldNotBeNull();
        record.Reason.ShouldBe("Issued to the wrong customer.");
        record.CancelledAt.ShouldBe(later);
        record.CreditNoteId.ShouldBe(BillingTestData.Id("cn"));

        var note = cancelled.Value;
        invoice.Notes.ShouldBe([note]);
        note.Kind.ShouldBe(AdjustmentNoteKind.Credit);
        note.Number.ShouldBe("CN-MAIN-2627-000001");
        note.Lines.Select(line => (line.LineNumber, line.GarmentJobId, line.TaxableValue.Amount, line.LineTotal.Amount))
            .ShouldBe([(1, JobOne, 540m, 567m), (2, JobTwo, 540m, 567m)]);
        note.Totals.GrandTotal.ShouldBe(invoice.Totals.GrandTotal);
        note.Totals.RoundOff.ShouldBe(invoice.Totals.RoundOff);
        note.Totals.TaxableValue.ShouldBe(invoice.Totals.TaxableValue);

        invoice.Cancel(BillingTestData.Id("cancel2"), BillingTestData.Id("cn2"), "CN-MAIN-2627-000002", "Again.", later, Cashier).Error.Code.ShouldBe("billing.invoice-already-cancelled");
        invoice.PostNote(BillingTestData.Id("note"), AdjustmentNoteKind.Debit, "DN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 10m)], "More.", later, Cashier)
            .Error.Code.ShouldBe("billing.invoice-already-cancelled");
        invoice.RemainingTaxableValueOf(JobOne).ShouldBe(Money.Zero);
    }

    [Fact]
    public void ANoteMovesValuePerLineAtTheLinesOwnRatesRoundedOncePerComponent()
    {
        var invoice = Posted([InvoiceTests.Line(JobOne, "BLOUSE"), InvoiceTests.Line(JobTwo, "SKIRT")]);

        // 90 taxable at 2.5% + 2.5%: 2.25 each, 94.50 in all; 33.33 at 2.5% is 0.83325, rounded once to 0.83.
        var credit = invoice.PostNote(
            BillingTestData.Id("cn-1"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001",
            [new AdjustmentNoteLineRequest(JobOne, 90m), new AdjustmentNoteLineRequest(JobTwo, 33.33m)], "Lining charged twice.", BillingTestData.Now, Cashier);

        credit.IsSuccess.ShouldBeTrue(credit.IsFailure ? $"{credit.Error.Code} {credit.Error.Target}" : string.Empty);
        var note = credit.Value;
        note.Lines[0].Taxes.Select(tax => (tax.Kind, tax.RatePercent, tax.Amount.Amount)).ShouldBe([("CGST", 2.5m, 2.25m), ("SGST", 2.5m, 2.25m)]);
        note.Lines[0].LineTotal.ShouldBe(Money.Rupees(94.5m));
        note.Lines[1].Taxes.Select(tax => tax.Amount.Amount).ShouldBe([0.83m, 0.83m]);
        note.Lines[1].LineTotal.ShouldBe(Money.Rupees(34.99m));
        note.Totals.TaxableValue.ShouldBe(Money.Rupees(123.33m));
        note.Totals.CentralTax.ShouldBe(Money.Rupees(3.08m));
        note.Totals.StateTax.ShouldBe(Money.Rupees(3.08m));
        note.Totals.IntegratedTax.ShouldBe(Money.Zero);
        note.Totals.RoundOff.ShouldBe(Money.Zero, "a correction is to the paisa");
        note.Totals.GrandTotal.ShouldBe(Money.Rupees(129.49m));
        invoice.RemainingTaxableValueOf(JobOne).ShouldBe(Money.Rupees(450m));

        // A credit note relieves at most what the line still carries; a debit note is not bounded.
        invoice.PostNote(BillingTestData.Id("cn-2"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000002", [new AdjustmentNoteLineRequest(JobOne, 450.01m)], "Too much.", BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.note-exceeds-line");
        var debit = invoice.PostNote(BillingTestData.Id("dn-1"), AdjustmentNoteKind.Debit, "DN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 1000m)], "Express finishing.", BillingTestData.Now, Cashier);
        debit.IsSuccess.ShouldBeTrue();
        debit.Value.Totals.GrandTotal.ShouldBe(Money.Rupees(1050m));
        invoice.Notes.Count.ShouldBe(2);
        invoice.RemainingTaxableValueOf(JobOne).ShouldBe(Money.Rupees(450m), "a debit note relieves nothing");
    }

    [Fact]
    public void ANoteRefusesALineTheInvoiceDoesNotCarryARepeatedLineAndAValueThatIsNotPositiveToThePaisa()
    {
        var invoice = Posted([InvoiceTests.Line(JobOne, "BLOUSE")]);
        var stranger = BillingTestData.Id("job-9");

        Note([new AdjustmentNoteLineRequest(stranger, 10m)]).Error.Code.ShouldBe("billing.note-line-not-on-invoice");
        Note([new AdjustmentNoteLineRequest(stranger, 10m)]).Error.Target.ShouldBe($"lines[{stranger}].garmentJobId");
        Note([new AdjustmentNoteLineRequest(JobOne, 10m), new AdjustmentNoteLineRequest(JobOne, 10m)]).Error.Code.ShouldBe("billing.job-repeated");
        Note([new AdjustmentNoteLineRequest(JobOne, 0m)]).Error.Code.ShouldBe("billing.note-value-not-well-formed");
        Note([new AdjustmentNoteLineRequest(JobOne, -1m)]).Error.Code.ShouldBe("billing.note-value-not-well-formed");
        Note([new AdjustmentNoteLineRequest(JobOne, 10.001m)]).Error.Code.ShouldBe("billing.note-value-not-well-formed");
        Note([]).Error.Code.ShouldBe("billing.lines-required");
        invoice.PostNote(BillingTestData.Id("n"), AdjustmentNoteKind.Credit, "CN-1", [new AdjustmentNoteLineRequest(JobOne, 10m)], null, BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.reason-required");
        invoice.Notes.ShouldBeEmpty();

        Tailor360.Platform.Abstractions.Results.Result<AdjustmentNote> Note(IReadOnlyList<AdjustmentNoteLineRequest> lines)
            => invoice.PostNote(BillingTestData.Id("n"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001", lines, "Because.", BillingTestData.Now, Cashier);
    }

    private static Invoice Posted(IReadOnlyList<InvoicedLine> lines)
    {
        var invoice = InvoiceTests.Draft(lines).Value;
        invoice.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        return invoice;
    }
}
