using Shouldly;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The receipt in the aggregate (#169): issued for an allocated payment with the number the caller drew
/// and the payload the caller minted, its figures frozen from the payment and whole — allocated plus held
/// is the amount, on a property over random payments — and its model saying exactly those figures.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReceiptTests
{
    private static readonly Guid Cashier = BillingTestData.Id("cashier");
    private static readonly Guid InvoiceOne = BillingTestData.Id("invoice-1");
    private static readonly Guid InvoiceTwo = BillingTestData.Id("invoice-2");
    private static readonly string Payload = BarcodePayload.Mint(BarcodePayload.ReceiptNamespace).Value;

    [Fact]
    public void ComposesTheNumberWithTheReceiptPrefixFromTheGaplessSequence()
    {
        DocumentNumbers.ReceiptPrefix.ShouldBe("RCPT");
        DocumentNumbers.ReceiptSequence.ShouldBe("receipt");
        DocumentNumbers.Compose(DocumentNumbers.ReceiptPrefix, "main", "2627", 1204).Value.ShouldBe("RCPT-MAIN-2627-001204");
    }

    [Fact]
    public void IssuesForAnAllocatedPaymentFreezingItsFigures()
    {
        var payment = Allocated(1000m, [(InvoiceOne, 567m)]);

        var receipt = Receipt.Issue(BillingTestData.Id("rcpt"), payment, " RCPT-MAIN-2627-000001 ", Payload, "2627", BillingTestData.Today, Money.Rupees(0m), BillingTestData.Now, Cashier);

        receipt.IsSuccess.ShouldBeTrue(receipt.IsFailure ? receipt.Error.Code : string.Empty);
        receipt.Value.ReceiptNumber.ShouldBe("RCPT-MAIN-2627-000001");
        receipt.Value.BarcodePayload.ShouldBe(Payload);
        receipt.Value.PaymentId.ShouldBe(payment.Id);
        receipt.Value.BranchId.ShouldBe(payment.BranchId);
        receipt.Value.Amount.ShouldBe(Money.Rupees(1000m));
        receipt.Value.Allocated.ShouldBe(Money.Rupees(567m));
        receipt.Value.UnappliedAdvance.ShouldBe(Money.Rupees(433m));
        receipt.Value.OrderOutstanding.ShouldBe(Money.Zero);
        receipt.Value.IssuedOn.ShouldBe(BillingTestData.Today);
        receipt.Value.IssuedBy.ShouldBe(Cashier);
    }

    [Fact]
    public void RefusesAMissingNumberAPayloadOfAnotherNamespaceANegativeBalanceAndAnUnallocatedPayment()
    {
        var payment = Allocated(1000m, [(InvoiceOne, 567m)]);

        Receipt.Issue(BillingTestData.Id("r"), payment, "  ", Payload, "2627", BillingTestData.Today, Money.Zero, BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.value-required");
        Receipt.Issue(BillingTestData.Id("r"), payment, "RCPT-MAIN-2627-000001", BarcodePayload.Mint(BarcodePayload.InvoiceNamespace).Value, "2627", BillingTestData.Today, Money.Zero, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.value-required", "an I- payload is an invoice's");
        Receipt.Issue(BillingTestData.Id("r"), payment, "RCPT-MAIN-2627-000001", Payload, "2627", BillingTestData.Today, Money.Rupees(-1m), BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.amount-not-well-formed");

        var unallocated = Payment.Record(BillingTestData.Id("p2"), BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Id("s"), Cashier, BillingTestData.Id("c"), BillingTestData.Id("o"), "CASH", Money.Rupees(10m), null, null, BillingTestData.Now, Cashier).Value;
        Receipt.Issue(BillingTestData.Id("r"), unallocated, "RCPT-MAIN-2627-000001", Payload, "2627", BillingTestData.Today, Money.Zero, BillingTestData.Now, Cashier)
            .Error.Code.ShouldBe("billing.payment-already-allocated", "a payment not yet allocated has no figures to freeze");
    }

    [Fact]
    public void TheModelSaysTheFrozenFiguresByInvoiceNumberAndNothingAboutTheCustomer()
    {
        var payment = Allocated(1000m, [(InvoiceOne, 300m), (InvoiceTwo, 500m)]);
        var receipt = Receipt.Issue(BillingTestData.Id("rcpt"), payment, "RCPT-MAIN-2627-000007", Payload, "2627", BillingTestData.Today, Money.Rupees(134m), BillingTestData.Now, Cashier).Value;

        var model = DocumentModels.Receipt(
            receipt, payment,
            new Dictionary<Guid, string> { [InvoiceOne] = "INV-MAIN-2627-000001", [InvoiceTwo] = "INV-MAIN-2627-000002" },
            "O-MAIN-2627-000001", "Cash", "Main branch");

        model["kind"].ShouldBe("Receipt");
        model["number"].ShouldBe("RCPT-MAIN-2627-000007");
        model["issuedOn"].ShouldBe("12-09-2026");
        model["barcodePayload"].ShouldBe(Payload);
        model["branchName"].ShouldBe("Main branch");
        model["orderNumber"].ShouldBe("O-MAIN-2627-000001");
        model["modeName"].ShouldBe("Cash");
        model["amount"].ShouldBe(1000m);
        model["allocated"].ShouldBe(800m);
        model["unappliedAdvance"].ShouldBe(200m);
        model["orderOutstanding"].ShouldBe(134m);
        var allocations = ((IEnumerable<object?>)model["allocations"]!).Cast<Dictionary<string, object?>>().ToList();
        allocations.Select(allocation => (allocation["invoiceNumber"], allocation["amount"])).ShouldBe([("INV-MAIN-2627-000001", 300m), ("INV-MAIN-2627-000002", 500m)]);
        model.Keys.ShouldNotContain("customer");
    }

    [Fact]
    public void TheReceiptAmountEqualsAllocationsPlusTheUnappliedAdvanceOverRandomPayments()
    {
        var random = new Random(169_041);
        for (var run = 0; run < 300; run++)
        {
            var amount = random.Next(1, 500_000) / 100m;
            var owed = Enumerable.Range(0, random.Next(0, 5)).Select(index => (BillingTestData.Id($"inv-{run}-{index}"), random.Next(0, 300_000) / 100m)).ToList();
            var payment = Allocated(amount, owed);

            var receipt = Receipt.Issue(BillingTestData.Id($"rcpt-{run}"), payment, "RCPT-MAIN-2627-000001", Payload, "2627", BillingTestData.Today, Money.Zero, BillingTestData.Now, Cashier).Value;

            (receipt.Allocated + receipt.UnappliedAdvance).ShouldBe(receipt.Amount);
            receipt.Amount.ShouldBe(payment.Amount);
            var model = DocumentModels.Receipt(receipt, payment, new Dictionary<Guid, string>(), "O-1", "Cash", "Main");
            var listed = ((IEnumerable<object?>)model["allocations"]!).Cast<Dictionary<string, object?>>().Sum(allocation => (decimal)allocation["amount"]!);
            (listed + (decimal)model["unappliedAdvance"]!).ShouldBe((decimal)model["amount"]!);
        }
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
}
