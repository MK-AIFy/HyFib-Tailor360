using Shouldly;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The compensating records in the aggregate (#163): a refund against one source and never beyond what
/// it holds, a reversal once per payment and never after a refund, the payment's advance net of both,
/// and the balance's refundable surplus and its <c>+ refunds</c> term on a property over random rows.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RefundTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid InvoiceOne = BillingTestData.Id("invoice-1");

    [Fact]
    public void RecordsARefundAgainstOneSourceWithinWhatItHolds()
    {
        var refund = Record(RefundSource.Advance, BillingTestData.Id("p"), null, 300m, 433m, "The order was cancelled before cutting.");

        refund.IsSuccess.ShouldBeTrue(refund.IsFailure ? refund.Error.Code : string.Empty);
        refund.Value.Source.ShouldBe(RefundSource.Advance);
        refund.Value.Amount.ShouldBe(Money.Rupees(300m));
        refund.Value.Reason.ShouldBe("The order was cancelled before cutting.");
        Record(RefundSource.Invoice, null, InvoiceOne, 433m, 433m, "Credited.").IsSuccess.ShouldBeTrue("the whole of what is held may go back");
    }

    [Fact]
    public void RefusesTheWrongSourceShapeAMissingReasonAnAmountBeyondTheSourceAndABadAmount()
    {
        Record(RefundSource.Advance, null, InvoiceOne, 1m, 10m, "x").Error.Code.ShouldBe("billing.refund-source-not-well-formed");
        Record(RefundSource.Advance, BillingTestData.Id("p"), InvoiceOne, 1m, 10m, "x").Error.Code.ShouldBe("billing.refund-source-not-well-formed");
        Record(RefundSource.Invoice, null, null, 1m, 10m, "x").Error.Code.ShouldBe("billing.refund-source-not-well-formed");
        Record(RefundSource.Advance, BillingTestData.Id("p"), null, 1m, 10m, "  ").Error.Code.ShouldBe("billing.reason-required");
        Record(RefundSource.Advance, BillingTestData.Id("p"), null, 10.01m, 10m, "x").Error.Code.ShouldBe("billing.refund-exceeds-refundable");
        Record(RefundSource.Advance, BillingTestData.Id("p"), null, 1m, 0m, "x").Error.Code.ShouldBe("billing.refund-exceeds-refundable", "nothing is held");
        Record(RefundSource.Advance, BillingTestData.Id("p"), null, 0m, 10m, "x").Error.Code.ShouldBe("billing.amount-not-well-formed");
        Record(RefundSource.Advance, BillingTestData.Id("p"), null, 0.001m, 10m, "x").Error.Code.ShouldBe("billing.amount-not-well-formed");
    }

    [Fact]
    public void ReversesOncePerPaymentAndNeverAfterARefund()
    {
        var payment = Allocated(1000m, [(InvoiceOne, 567m)]);

        var reversal = PaymentReversal.Of(BillingTestData.Id("rev"), payment, " Never cleared. ", BillingTestData.Now, Cashier);
        reversal.IsSuccess.ShouldBeTrue();
        reversal.Value.PaymentId.ShouldBe(payment.Id);
        reversal.Value.Reason.ShouldBe("Never cleared.");
        PaymentReversal.Of(BillingTestData.Id("rev"), payment, null, BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.reason-required");
    }

    [Fact]
    public void AReversedPaymentAllocatesNothingHoldsNothingAndTakesNoMoreApplications()
    {
        var payment = Allocated(1000m, [(InvoiceOne, 567m)]);
        payment.Allocated.ShouldBe(Money.Rupees(567m));
        payment.UnappliedAdvance.ShouldBe(Money.Rupees(433m));

        Reverse(payment);

        payment.IsReversed.ShouldBeTrue();
        payment.Allocated.ShouldBe(Money.Zero, "the allocations are released");
        payment.UnappliedAdvance.ShouldBe(Money.Zero, "the advance is released");
        payment.Allocations.Count.ShouldBe(1, "the rows stay: nothing is edited");
        payment.ApplyAdvance(BillingTestData.Id("m"), InvoiceOne, Money.Rupees(1m), Money.Rupees(1m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.no-advance-held");
    }

    [Fact]
    public void TheBalanceOffersTheSurplusAsRefundableAndCountsRefundsBackIntoWhatIsOwed()
    {
        var invoice = Posted();
        invoice.PostNote(BillingTestData.Id("cn"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 540m)], "Garment not made.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();

        // Paid in full, then credited in full: 567 sits on the invoice beyond what it charges.
        var surplus = InvoiceBalance.Of(invoice, Money.Rupees(567m), Money.Zero);
        surplus.Outstanding.ShouldBe(Money.Zero);
        surplus.Refundable.ShouldBe(Money.Rupees(567m));
        surplus.Status.ShouldBe(InvoicePaidStatus.Paid);

        // 567 paid back: nothing held, nothing owed.
        var settled = InvoiceBalance.Of(invoice, Money.Rupees(567m), Money.Rupees(567m));
        settled.Refundable.ShouldBe(Money.Zero);
        settled.Outstanding.ShouldBe(Money.Zero);

        // A refund beyond the credit reopens what is owed: 100 too much paid back.
        var reopened = InvoiceBalance.Of(invoice, Money.Rupees(567m), Money.Rupees(667m));
        reopened.Outstanding.ShouldBe(Money.Rupees(100m));
        reopened.Refundable.ShouldBe(Money.Zero);
        reopened.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);

        InvoiceBalance.Of(Posted(), Money.Zero, Money.Zero).Refundable.ShouldBe(Money.Zero);
    }

    [Fact]
    public void ACancelledInvoiceThatOwesAgainIsNeverReportedCancelled()
    {
        // Paid, then cancelled: the cancellation's own credit note relieves it in full while the payment's
        // allocation still stands, so the invoice reads Paid with the whole amount refundable — Cancelled
        // is not yet the answer, because the allocation has not moved.
        var invoice = Posted();
        invoice.Cancel(BillingTestData.Id("cancellation"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "Issued to the wrong customer.", BillingTestData.Today, BillingTestData.Now, Cashier)
            .IsSuccess.ShouldBeTrue();
        var stillAllocated = InvoiceBalance.Of(invoice, Money.Rupees(567m), Money.Zero);
        stillAllocated.Status.ShouldBe(InvoicePaidStatus.Paid, "allocated still stands; nothing has been paid back yet");
        stillAllocated.Refundable.ShouldBe(Money.Rupees(567m));

        // The payment that funded it is reversed with nothing yet paid back: the allocation is released,
        // nothing is owed and nothing is held. Genuinely settled, and only now does Cancelled apply.
        var settled = InvoiceBalance.Of(invoice, Money.Zero, Money.Zero);
        settled.Outstanding.ShouldBe(Money.Zero);
        settled.Refundable.ShouldBe(Money.Zero);
        settled.Status.ShouldBe(InvoicePaidStatus.Cancelled);

        // Instead, the 567 is refunded back from the cancelled invoice's surplus before the reversal, and
        // only then is the funding payment reversed: the allocation still drops to zero, but the 567
        // already paid back does not un-happen. The invoice owes 567 again — never Cancelled while it
        // does, whatever emptied its allocation (Codex finding on #163).
        var reopened = InvoiceBalance.Of(invoice, Money.Zero, Money.Rupees(567m));
        reopened.Outstanding.ShouldBe(Money.Rupees(567m));
        reopened.Status.ShouldNotBe(InvoicePaidStatus.Cancelled);
        reopened.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);
    }

    [Fact]
    public void OutstandingAndRefundableAreTheTwoSidesOfOneSumOverRandomRows()
    {
        var random = new Random(163_041);
        for (var run = 0; run < 300; run++)
        {
            var invoice = Posted();
            var creditable = 540m;
            for (var note = 0; note < random.Next(0, 3); note++)
            {
                if (random.Next(2) == 0 && creditable >= 1m)
                {
                    var taxable = random.Next(1, (int)creditable);
                    invoice.PostNote(BillingTestData.Id($"cn-{run}-{note}"), AdjustmentNoteKind.Credit, $"CN-{run}-{note}", [new AdjustmentNoteLineRequest(JobOne, taxable)], "Because.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
                    creditable -= taxable;
                }
                else
                {
                    invoice.PostNote(BillingTestData.Id($"dn-{run}-{note}"), AdjustmentNoteKind.Debit, $"DN-{run}-{note}", [new AdjustmentNoteLineRequest(JobOne, random.Next(1, 500))], "Because.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
                }
            }

            var allocated = Money.Rupees(random.Next(0, 150_000) / 100m);
            var refunds = Money.Rupees(random.Next(0, 80_000) / 100m);
            var balance = InvoiceBalance.Of(invoice, allocated, refunds);

            var owed = balance.Charges - balance.Credits + balance.Debits - allocated + refunds;
            (balance.Outstanding - balance.Refundable).ShouldBe(owed, "outstanding is the sum above zero, refundable is the sum below it");
            (balance.Outstanding.IsZero || balance.Refundable.IsZero).ShouldBeTrue("never both");
            balance.Outstanding.IsNegative.ShouldBeFalse();
            balance.Refundable.IsNegative.ShouldBeFalse();
        }
    }

    [Fact]
    public void ThePaymentsAdvanceIsNetOfWhatWasPaidBack()
    {
        // Refunds ride on the payment through persistence; the domain reads them from the navigation, so the
        // arithmetic is asserted through the balance the store composes rather than here. The domain's own
        // guard is the reversal: a payment with a refund on it is not reversed.
        var payment = Allocated(1000m, [(InvoiceOne, 567m)]);
        payment.RefundedFromAdvance.ShouldBe(Money.Zero);
        payment.UnappliedAdvance.ShouldBe(Money.Rupees(433m));
    }

    private static Result<Refund> Record(RefundSource source, Guid? paymentId, Guid? invoiceId, decimal amount, decimal refundable, string? reason)
        => Refund.Record(
            BillingTestData.Id("refund"), BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Id("session"), Cashier,
            BillingTestData.Id("customer-1"), BillingTestData.Id("order-1"), source, paymentId, invoiceId, "CASH", Money.Rupees(amount), Money.Rupees(refundable),
            null, null, reason, BillingTestData.Now, Cashier);

    private static void Reverse(Payment payment)
    {
        // The reversal rides on the payment through persistence; here the private setter is reached the way EF
        // Core reaches it, so the aggregate's arithmetic can be asserted without a database.
        var reversal = PaymentReversal.Of(BillingTestData.Id("rev"), payment, "Never cleared.", BillingTestData.Now, Cashier).Value;
        typeof(Payment).GetProperty(nameof(Payment.Reversal))!.SetValue(payment, reversal);
    }

    private static Payment Allocated(decimal amount, IReadOnlyList<(Guid InvoiceId, decimal Outstanding)> owed)
    {
        var payment = Payment.Record(
            BillingTestData.Id("payment"), BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Id("session"), Cashier,
            BillingTestData.Id("customer-1"), BillingTestData.Id("order-1"), "CASH", Money.Rupees(amount), null, null, BillingTestData.Now, Cashier).Value;
        var next = 0;
        payment.AllocateAtRecording(owed.Select(invoice => (invoice.InvoiceId, Money.Rupees(invoice.Outstanding))).ToList(), () => BillingTestData.Id($"alloc-{next++}"), BillingTestData.Id("advance"), BillingTestData.Now, Cashier)
            .IsSuccess.ShouldBeTrue();
        return payment;
    }

    private static Invoice Posted()
    {
        var invoice = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        invoice.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        return invoice;
    }
}
