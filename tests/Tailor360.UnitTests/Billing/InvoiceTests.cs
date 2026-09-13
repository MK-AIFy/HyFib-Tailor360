using Shouldly;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The draft invoice (#153): created whole from priced lines, re-lined only while a draft, discarded once
/// with a reason, and the customer copied onto it checked for what a document may carry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InvoiceTests
{
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid JobTwo = BillingTestData.Id("job-2");
    private static readonly Guid Cashier = BillingTestData.Id("cashier");

    [Fact]
    public void ADraftNumbersItsLinesInOrderAndCarriesTheCalculationItWasMadeFrom()
    {
        var created = Draft([Line(JobOne, "BLOUSE"), Line(JobTwo, "SKIRT")]);

        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        var invoice = created.Value;
        invoice.Status.ShouldBe(InvoiceStatus.Draft);
        invoice.IsDraft.ShouldBeTrue();
        invoice.Revision.ShouldBe(1);
        invoice.Lines.Select(line => (line.LineNumber, line.GarmentJobId, line.ItemCode)).ShouldBe([(1, JobOne, "BLOUSE"), (2, JobTwo, "SKIRT")]);
        invoice.Lines[0].Surcharges.Single().Position.ShouldBe(1);
        invoice.Lines[0].Surcharges.Single().GarmentJobId.ShouldBe(JobOne);
        invoice.Lines[1].Taxes.ShouldAllBe(tax => tax.GarmentJobId == JobTwo);
        invoice.Lines[0].Taxes.Select(tax => tax.Kind).ShouldBe(["CGST", "SGST"]);
        invoice.GarmentJobIds.ShouldBe([JobOne, JobTwo]);
        invoice.Calculation.Reference.ShouldBe("order:1:1");
        invoice.Totals.GrandTotal.ShouldBe(Money.Rupees(1134m));
        invoice.CreatedBy.ShouldBe(Cashier);
    }

    [Fact]
    public void ADraftWithoutLinesOrWithALineThatIsNoGarmentJobIsRefused()
    {
        Draft([]).Error.Code.ShouldBe("billing.lines-required");

        var refused = Draft([Line(Guid.Empty, "BLOUSE")]);
        refused.Error.Code.ShouldBe("billing.line-not-a-garment-job");
        refused.Error.Target.ShouldBe("lines");

        // One line per garment job: a line's identity in the store is the job it charges for.
        var repeated = Draft([Line(JobOne, "BLOUSE"), Line(JobOne, "SKIRT")]);
        repeated.Error.Code.ShouldBe("billing.job-repeated");
        repeated.Error.Target.ShouldBe("lines");
    }

    [Fact]
    public void TheCustomerCopiedOntoTheDocumentMustBeNamedAndFit()
    {
        Draft([Line(JobOne, "BLOUSE")], customer: new InvoiceCustomer("C-1", " ", null, null, null)).Error.Code.ShouldBe("billing.value-required");
        Draft([Line(JobOne, "BLOUSE")], customer: new InvoiceCustomer("C-1", "Kavitha", new string('a', InvoiceCustomer.MaximumLength + 1), null, null))
            .Error.Code.ShouldBe("billing.value-too-long");
        Draft([Line(JobOne, "BLOUSE")], customer: new InvoiceCustomer("C-1", "Kavitha", null, null, null)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RelinesReplaceTheLinesWholeAndRaiseTheRevision()
    {
        var invoice = Draft([Line(JobOne, "BLOUSE"), Line(JobTwo, "SKIRT")]).Value;
        var later = BillingTestData.Now.AddMinutes(5);

        var set = invoice.SetLines(Calculation("invoice:x:2"), [Line(JobTwo, "SKIRT")], Totals(567m), later, Cashier);

        set.IsSuccess.ShouldBeTrue();
        invoice.Revision.ShouldBe(2);
        invoice.Lines.Select(line => (line.LineNumber, line.GarmentJobId)).ShouldBe([(1, JobTwo)]);
        invoice.Calculation.Reference.ShouldBe("invoice:x:2");
        invoice.Totals.GrandTotal.ShouldBe(Money.Rupees(567m));
        invoice.UpdatedAt.ShouldBe(later);
        invoice.UpdatedBy.ShouldBe(Cashier);
    }

    [Fact]
    public void DiscardNeedsAReasonHappensOnceAndEndsEditing()
    {
        var invoice = Draft([Line(JobOne, "BLOUSE")]).Value;

        invoice.Discard(BillingTestData.Now, Cashier, "  ").Error.Code.ShouldBe("billing.reason-required");
        invoice.Discard(BillingTestData.Now, Cashier, new string('r', Invoice.MaximumReasonLength + 1)).Error.Code.ShouldBe("billing.value-too-long");

        invoice.Discard(BillingTestData.Now.AddMinutes(1), Cashier, " Customer changed their mind. ").IsSuccess.ShouldBeTrue();
        invoice.Status.ShouldBe(InvoiceStatus.Discarded);
        invoice.IsDraft.ShouldBeFalse();
        invoice.DiscardedAt.ShouldBe(BillingTestData.Now.AddMinutes(1));
        invoice.DiscardedBy.ShouldBe(Cashier);
        invoice.DiscardReason.ShouldBe("Customer changed their mind.");

        invoice.Discard(BillingTestData.Now.AddMinutes(2), Cashier, "Again.").Error.Code.ShouldBe("billing.invoice-not-editable");
        invoice.SetLines(Calculation("invoice:x:2"), [Line(JobTwo, "SKIRT")], Totals(567m), BillingTestData.Now, Cashier).Error.Code.ShouldBe("billing.invoice-not-editable");
        invoice.Revision.ShouldBe(1);
    }

    internal static Tailor360.Platform.Abstractions.Results.Result<Invoice> Draft(IReadOnlyList<InvoicedLine> lines, InvoiceCustomer? customer = null)
        => Invoice.CreateDraft(
            BillingTestData.Id("invoice-1"), BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Id("customer-1"),
            BillingTestData.Id("order-1"), "ORD-1", customer ?? new InvoiceCustomer("C-MAIN-000001", "Kavitha", "12 Second Street", "Peelamedu", "641004"),
            Calculation("order:1:1"), lines, Totals(567m * lines.Count), BillingTestData.Now, Cashier);

    internal static InvoiceCalculation Calculation(string reference)
        => new(reference, BillingTestData.Id("plv-1"), BillingTestData.Id("tcv-1"), BillingTestData.Id("reg-1"), BillingTestData.WellFormedGstin, "33", "33", "IntraState", false, "Example Tailors Private Limited", "Example Tailors");

    /// <summary>The walkthrough blouse: 450 stitching plus 90 lining, 5% GST split in two, 567 all in.</summary>
    internal static InvoicedLine Line(Guid jobId, string itemCode)
        => new(
            jobId, jobId.ToString(), itemCode, "Stitching", 1m, 450m, 450m, Money.Rupees(450m),
            [new InvoicedSurcharge("LINING", "Lining", 90m, Money.Rupees(90m))],
            null, null, null, Money.Zero, Money.Rupees(540m), Money.Rupees(540m), "STITCHING_5", "998822", "Services",
            [new InvoicedTax("CGST", 2.5m, Money.Rupees(13.5m)), new InvoicedTax("SGST", 2.5m, Money.Rupees(13.5m))],
            Money.Rupees(27m), Money.Rupees(567m), Money.Zero);

    internal static InvoiceTotals Totals(decimal grand)
        => new(Money.Rupees(grand), Money.Zero, Money.Rupees(grand), Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Rupees(grand));
}
