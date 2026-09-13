using Shouldly;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The balance of an invoice from rows alone (#162, INV-PAY-06): posted charges minus the credit notes
/// plus the debit notes minus what is allocated plus what is refunded, never below zero, and the paid
/// status that follows from it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InvoiceBalanceTests
{
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid Cashier = BillingTestData.Id("cashier");

    [Fact]
    public void AnUntouchedPostedInvoiceOwesItsGrandTotalAndIsUnpaid()
    {
        var balance = InvoiceBalance.Of(Posted(), Money.Zero, Money.Zero);

        balance.Charges.ShouldBe(Money.Rupees(567m));
        balance.Credits.ShouldBe(Money.Zero);
        balance.Debits.ShouldBe(Money.Zero);
        balance.Outstanding.ShouldBe(Money.Rupees(567m));
        balance.Status.ShouldBe(InvoicePaidStatus.Unpaid);
    }

    [Fact]
    public void AllocationsMoveItThroughPartlyPaidToPaidAndNeverBelowZero()
    {
        var invoice = Posted();

        var partly = InvoiceBalance.Of(invoice, Money.Rupees(100m), Money.Zero);
        partly.Outstanding.ShouldBe(Money.Rupees(467m));
        partly.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);

        var paid = InvoiceBalance.Of(invoice, Money.Rupees(567m), Money.Zero);
        paid.Outstanding.ShouldBe(Money.Zero);
        paid.Status.ShouldBe(InvoicePaidStatus.Paid);

        // Over-allocated by a defect elsewhere: the balance floors at zero rather than owing the customer.
        var over = InvoiceBalance.Of(invoice, Money.Rupees(600m), Money.Zero);
        over.Outstanding.ShouldBe(Money.Zero);
        over.Status.ShouldBe(InvoicePaidStatus.Paid);
    }

    [Fact]
    public void CreditNotesReduceAndDebitNotesIncreaseWhatIsOwed()
    {
        var invoice = Posted();
        invoice.PostNote(BillingTestData.Id("cn"), AdjustmentNoteKind.Credit, "CN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 90m)], "Lining twice.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        invoice.PostNote(BillingTestData.Id("dn"), AdjustmentNoteKind.Debit, "DN-MAIN-2627-000001", [new AdjustmentNoteLineRequest(JobOne, 200m)], "Express finishing.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();

        var balance = InvoiceBalance.Of(invoice, Money.Rupees(100m), Money.Zero);

        // 90 taxable at 5% is 94.50 credited; 200 taxable at 5% is 210 debited.
        balance.Credits.ShouldBe(Money.Rupees(94.5m));
        balance.Debits.ShouldBe(Money.Rupees(210m));
        balance.Outstanding.ShouldBe(Money.Rupees(567m - 94.5m + 210m - 100m));
        balance.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);
    }

    [Fact]
    public void ACancelledInvoiceIsCancelledWhenNothingWasAllocatedAndPaidByItsCreditOtherwise()
    {
        var untouched = Posted();
        untouched.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "Wrong customer.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        var cancelled = InvoiceBalance.Of(untouched, Money.Zero, Money.Zero);
        cancelled.Credits.ShouldBe(Money.Rupees(567m));
        cancelled.Outstanding.ShouldBe(Money.Zero);
        cancelled.Status.ShouldBe(InvoicePaidStatus.Cancelled);

        // Money had been taken against it: the credit relieves it and the allocation stands until E09-F03-3
        // refunds or re-applies it — the status says Paid, and the refund column says what is owed back.
        var paidThenCancelled = Posted();
        paidThenCancelled.Cancel(BillingTestData.Id("cancel"), BillingTestData.Id("cn"), "CN-MAIN-2627-000001", "Wrong customer.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        var balance = InvoiceBalance.Of(paidThenCancelled, Money.Rupees(200m), Money.Zero);
        balance.Status.ShouldBe(InvoicePaidStatus.Paid);
        balance.Outstanding.ShouldBe(Money.Zero);
    }

    [Fact]
    public void ARefundReopensWhatItRefunds()
    {
        var balance = InvoiceBalance.Of(Posted(), Money.Rupees(567m), Money.Rupees(100m));

        balance.Outstanding.ShouldBe(Money.Rupees(100m));
        balance.Status.ShouldBe(InvoicePaidStatus.PartlyPaid);
    }

    [Fact]
    public void HoldsTheDocumentedRuleOverRandomSequencesOfNotesAllocationsAndRefunds()
    {
        // The acceptance criterion as a property: over random sequences, outstanding is exactly
        // max(0, charges - credits + debits - allocated + refunds) and the status agrees with it.
        var random = new Random(162_106);
        for (var run = 0; run < 300; run++)
        {
            var invoice = Posted();
            var creditable = 540m;
            for (var note = 0; note < random.Next(0, 4); note++)
            {
                if (random.Next(2) == 0 && creditable >= 1m)
                {
                    var taxable = random.Next(1, (int)creditable);
                    invoice.PostNote(BillingTestData.Id($"cn-{run}-{note}"), AdjustmentNoteKind.Credit, $"CN-{run}-{note}", [new AdjustmentNoteLineRequest(JobOne, taxable)], "Because.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
                    creditable -= taxable;
                }
                else
                {
                    invoice.PostNote(BillingTestData.Id($"dn-{run}-{note}"), AdjustmentNoteKind.Debit, $"DN-{run}-{note}", [new AdjustmentNoteLineRequest(JobOne, random.Next(1, 1000))], "Because.", BillingTestData.Today, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
                }
            }

            // The notes' own grand totals, tax rounding and all: the balance sums what was posted, it does not re-tax.
            var credits = invoice.Notes.Where(note => note.Kind == AdjustmentNoteKind.Credit).Sum(note => note.Totals.GrandTotal.Amount);
            var debits = invoice.Notes.Where(note => note.Kind == AdjustmentNoteKind.Debit).Sum(note => note.Totals.GrandTotal.Amount);
            var allocated = Money.Rupees(random.Next(0, 200_000) / 100m);
            var refunds = Money.Rupees(random.Next(0, 50_000) / 100m);
            var balance = InvoiceBalance.Of(invoice, allocated, refunds);

            var expected = 567m - credits + debits - allocated.Amount + refunds.Amount;
            balance.Credits.Amount.ShouldBe(credits);
            balance.Debits.Amount.ShouldBe(debits);
            balance.Outstanding.Amount.ShouldBe(Math.Max(0m, expected));
            balance.Outstanding.IsNegative.ShouldBeFalse();
            balance.Status.ShouldBe(
                balance.Outstanding.IsZero ? InvoicePaidStatus.Paid
                : allocated.IsZero && refunds.IsZero ? InvoicePaidStatus.Unpaid
                : InvoicePaidStatus.PartlyPaid);
        }
    }

    private static Invoice Posted()
    {
        var invoice = InvoiceTests.Draft([InvoiceTests.Line(JobOne, "BLOUSE")]).Value;
        invoice.Post("INV-MAIN-2627-000001", "I-7K3M9QW2XZ4B", "2627", new DateOnly(2026, 9, 12), BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        return invoice;
    }
}
