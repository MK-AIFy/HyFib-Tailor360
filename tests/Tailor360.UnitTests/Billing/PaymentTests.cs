using Shouldly;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The payment in the aggregate (#162): recorded once with a well-formed amount and a reference that is
/// not a card number, allocated at recording oldest invoice first with the rest held as an advance
/// (INV-PAY-04, INV-PAY-05), and never allocated beyond what it carries or an invoice owes (INV-PAY-03).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaymentTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");
    private static readonly Guid Session = BillingTestData.Id("session");
    private static readonly Guid Customer = BillingTestData.Id("customer-1");
    private static readonly Guid Order = BillingTestData.Id("order-1");
    private static readonly Guid InvoiceOne = BillingTestData.Id("invoice-1");
    private static readonly Guid InvoiceTwo = BillingTestData.Id("invoice-2");
    private static readonly Guid AdvanceId = BillingTestData.Id("advance");

    [Fact]
    public void RecordsAPositiveAmountToThePaisaInRupeesAndKeepsTheTrimmedReferenceAndKey()
    {
        var payment = Record(1134m, "UPI", "  UPI-426114-8QX2 ", "  key-1 ").Value;

        payment.Status.ShouldBe(PaymentStatus.Recorded);
        payment.Amount.ShouldBe(Money.Rupees(1134m));
        payment.Reference.ShouldBe("UPI-426114-8QX2");
        payment.ClientKey.ShouldBe("key-1");
        payment.CashierSessionId.ShouldBe(Session);
        payment.Allocations.ShouldBeEmpty();
        payment.Advance.ShouldBeNull();
        payment.Allocated.ShouldBe(Money.Zero);
        payment.UnappliedAdvance.ShouldBe(Money.Zero);

        Record(1134m, "CASH", null, null).Value.Reference.ShouldBeNull();
        Record(1134m, "CASH", "   ", "").Value.ClientKey.ShouldBeNull();
        Record(1134m, "CASH", null, new string('k', 80)).Value.ClientKey!.Length.ShouldBe(Payment.MaximumClientKeyLength, "a long key is kept to the column, not refused");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(100_000_000_000_000)]
    public void RefusesAnAmountThatIsNotPositiveToThePaisaOrExceedsTheColumn(decimal amount)
        => Record(amount, "CASH", null, null).Error.Code.ShouldBe("billing.amount-not-well-formed");

    [Fact]
    public void RefusesAForeignCurrencyAndAMalformedModeCode()
    {
        Payment.Record(BillingTestData.Id("p"), BillingTestData.Organisation, BillingTestData.MainBranch, Session, Cashier, Customer, Order, "CASH", new Money(10m, "USD"), null, null, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        Record(10m, "cash", null, null).Error.Code.ShouldBe("billing.code-not-well-formed");
        Record(10m, "", null, null).Error.Code.ShouldBe("billing.code-not-well-formed");
    }

    [Theory]
    [InlineData("4111111111111111", "billing.reference-looks-like-a-card")]
    [InlineData("4111 1111 1111 1111", "billing.reference-looks-like-a-card")]
    [InlineData("auth 4111-1111-1111-1111 ok", "billing.reference-looks-like-a-card")]
    [InlineData("1234567890123", "billing.reference-looks-like-a-card")]
    [InlineData("1234567890123456789", "billing.reference-looks-like-a-card")]
    [InlineData("refbell", "billing.reference-not-well-formed")]
    public void RefusesAReferenceThatReadsAsACardNumberOrCarriesAControlCharacter(string reference, string code)
        => Record(10m, "CARD", reference, null).Error.Code.ShouldBe(code);

    [Fact]
    public void AcceptsAReferenceOfDigitsTooShortOrTooLongToBeACardAndBoundsItsLength()
    {
        Record(10m, "CARD", "123456789012", null).IsSuccess.ShouldBeTrue("twelve digits is an authorisation code, not a card");
        Record(10m, "CARD", "12345678901234567890", null).IsSuccess.ShouldBeTrue("twenty digits is a bank UTR, not a card");
        Record(10m, "CARD", "A426114B", null).IsSuccess.ShouldBeTrue();
        Record(10m, "CARD", new string('x', PaymentReferences.MaximumLength), null).IsSuccess.ShouldBeTrue();
        Record(10m, "CARD", new string('x', PaymentReferences.MaximumLength + 1), null).Error.Code.ShouldBe("billing.value-too-long");
    }

    [Fact]
    public void AllocatesOldestInvoiceFirstAndHoldsTheRestAsAnAdvance()
    {
        var payment = Record(1000m, "CASH", null, null).Value;
        var ids = new Queue<Guid>([BillingTestData.Id("a1"), BillingTestData.Id("a2"), BillingTestData.Id("a3")]);

        var applied = payment.AllocateAtRecording(
            [(InvoiceOne, Money.Rupees(300m)), (InvoiceTwo, Money.Rupees(500m))], ids.Dequeue, AdvanceId, BillingTestData.Now, Cashier);

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Select(allocation => (allocation.InvoiceId, allocation.Amount.Amount, allocation.Kind, allocation.AdvanceId))
            .ShouldBe([(InvoiceOne, 300m, AllocationKind.Automatic, null), (InvoiceTwo, 500m, AllocationKind.Automatic, null)]);
        payment.Allocated.ShouldBe(Money.Rupees(800m));
        payment.Advance.ShouldNotBeNull();
        payment.Advance.Id.ShouldBe(AdvanceId);
        payment.Advance.Amount.ShouldBe(Money.Rupees(200m));
        payment.UnappliedAdvance.ShouldBe(Money.Rupees(200m));

        // Once: a second allocation at recording is a conflict, and nothing moved.
        payment.AllocateAtRecording([(InvoiceOne, Money.Rupees(300m))], ids.Dequeue, AdvanceId, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.payment-already-allocated");
        payment.Allocations.Count.ShouldBe(2);
    }

    [Fact]
    public void StopsWhenTheMoneyRunsOutAndSkipsAnInvoiceThatOwesNothing()
    {
        var payment = Record(400m, "CASH", null, null).Value;
        var ids = new Queue<Guid>([BillingTestData.Id("a1"), BillingTestData.Id("a2")]);

        var applied = payment.AllocateAtRecording(
            [(BillingTestData.Id("paid"), Money.Zero), (InvoiceOne, Money.Rupees(300m)), (InvoiceTwo, Money.Rupees(500m))], ids.Dequeue, AdvanceId, BillingTestData.Now, Cashier);

        applied.Value.Select(allocation => (allocation.InvoiceId, allocation.Amount.Amount)).ShouldBe([(InvoiceOne, 300m), (InvoiceTwo, 100m)]);
        payment.Advance.ShouldBeNull("every rupee found an invoice");
        payment.UnappliedAdvance.ShouldBe(Money.Zero);

        var whole = Record(500m, "CASH", null, null).Value;
        whole.AllocateAtRecording([], ids.Dequeue, AdvanceId, BillingTestData.Now, Cashier).Value.ShouldBeEmpty();
        whole.Advance!.Amount.ShouldBe(Money.Rupees(500m), "no invoice yet: the whole payment is an advance");
    }

    [Fact]
    public void AppliesAHeldAdvanceNeverBeyondWhatIsHeldOrWhatTheInvoiceOwes()
    {
        var payment = Record(1000m, "CASH", null, null).Value;
        payment.AllocateAtRecording([(InvoiceOne, Money.Rupees(300m))], () => BillingTestData.Id("a1"), AdvanceId, BillingTestData.Now, Cashier).IsSuccess.ShouldBeTrue();
        payment.UnappliedAdvance.ShouldBe(Money.Rupees(700m));

        payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Rupees(700.01m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.advance-exceeded");
        payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Rupees(500.01m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.allocation-exceeds-invoice");
        payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Zero, AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Rupees(0.001m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");
        payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Rupees(100m), AllocationKind.Automatic, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.allocation-kind-not-for-an-advance");
        payment.Allocations.Count.ShouldBe(1, "a refusal stages nothing");

        var manual = payment.ApplyAdvance(BillingTestData.Id("m1"), InvoiceTwo, Money.Rupees(500m), Money.Rupees(500m), AllocationKind.Manual, BillingTestData.Now, Cashier).Value;
        manual.AdvanceId.ShouldBe(AdvanceId);
        manual.Kind.ShouldBe(AllocationKind.Manual);
        payment.UnappliedAdvance.ShouldBe(Money.Rupees(200m));

        var byRule = payment.ApplyAdvance(BillingTestData.Id("r1"), BillingTestData.Id("invoice-3"), Money.Rupees(900m), Money.Rupees(200m), AllocationKind.AdvanceApplied, BillingTestData.Now, null).Value;
        byRule.AllocatedBy.ShouldBeNull("the rule has no user");
        payment.UnappliedAdvance.ShouldBe(Money.Zero);
        payment.Allocated.ShouldBe(payment.Amount, "held plus applied is the payment, no more");

        payment.ApplyAdvance(BillingTestData.Id("r2"), InvoiceTwo, Money.Rupees(1m), Money.Rupees(1m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.advance-exceeded", "nothing is held any more");
        Record(10m, "CASH", null, null).Value.ApplyAdvance(BillingTestData.Id("x"), InvoiceOne, Money.Rupees(1m), Money.Rupees(1m), AllocationKind.Manual, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.no-advance-held");
    }

    [Fact]
    public void NeverAllocatesMoreThanItCarriesOverRandomInvoiceSequences()
    {
        // INV-PAY-03 as a property: over random amounts and random outstanding lists, the sum of the
        // allocations plus what is held equals the payment, no allocation exceeds its invoice, and the
        // order is the order given.
        var random = new Random(162_041);
        for (var run = 0; run < 500; run++)
        {
            var amount = Money.Rupees(random.Next(1, 500_000) / 100m);
            var payment = Record(amount.Amount, "CASH", null, null).Value;
            var outstanding = Enumerable.Range(0, random.Next(0, 6))
                .Select(index => (InvoiceId: BillingTestData.Id($"inv-{run}-{index}"), Outstanding: Money.Rupees(random.Next(0, 300_000) / 100m)))
                .ToList();
            var next = 0;

            var applied = payment.AllocateAtRecording(outstanding, () => BillingTestData.Id($"alloc-{run}-{next++}"), BillingTestData.Id($"adv-{run}"), BillingTestData.Now, Cashier).Value;

            (payment.Allocated + payment.UnappliedAdvance).ShouldBe(amount);
            payment.Allocated.ShouldBeLessThanOrEqualTo(amount);
            foreach (var allocation in applied)
            {
                allocation.Amount.ShouldBeLessThanOrEqualTo(outstanding.Single(invoice => invoice.InvoiceId == allocation.InvoiceId).Outstanding);
                allocation.Amount.ShouldBeGreaterThan(Money.Zero);
            }

            applied.Select(allocation => allocation.InvoiceId).ShouldBe(outstanding.Where(invoice => !invoice.Outstanding.IsZero).Select(invoice => invoice.InvoiceId).Take(applied.Count));
            var owedInAll = outstanding.Aggregate(Money.Zero, (sum, invoice) => sum + invoice.Outstanding);
            payment.UnappliedAdvance.ShouldBe(amount > owedInAll ? amount - owedInAll : Money.Zero);
        }
    }

    private static Result<Payment> Record(decimal amount, string modeCode, string? reference, string? clientKey)
        => Payment.Record(
            BillingTestData.Id("payment"), BillingTestData.Organisation, BillingTestData.MainBranch, Session, Cashier, Customer, Order,
            modeCode, Money.Rupees(amount), reference, clientKey, BillingTestData.Now, Cashier);
}
