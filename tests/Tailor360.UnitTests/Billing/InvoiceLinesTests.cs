using Shouldly;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// A stored calculation becomes invoice lines (#153) only where every line key is a garment job of the order
/// the invoice is for; the figures are copied, never recomputed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InvoiceLinesTests
{
    private static readonly Guid OrderId = BillingTestData.Id("order-1");
    private static readonly Guid JobOne = BillingTestData.Id("job-1");
    private static readonly Guid JobTwo = BillingTestData.Id("job-2");

    [Fact]
    public void CopiesEveryFigureOfALineWhoseKeyIsAJobOfTheOrder()
    {
        var lines = InvoiceLines.From(Result([Priced(JobOne.ToString(), discount: true)]), Order());

        lines.IsSuccess.ShouldBeTrue(lines.IsFailure ? lines.Error.Message : string.Empty);
        var line = lines.Value.Single();
        line.GarmentJobId.ShouldBe(JobOne);
        line.ItemCode.ShouldBe("BLOUSE");
        line.CatalogueRate.ShouldBe(450m);
        line.AppliedRate.ShouldBe(450m);
        line.Surcharges.Single().Amount.ShouldBe(Money.Rupees(90m));
        line.DiscountRuleCode.ShouldBe("FESTIVE");
        line.DiscountKind.ShouldBe("Percent");
        line.DiscountValue.ShouldBe(10m);
        line.DiscountAmount.ShouldBe(Money.Rupees(54m));
        line.TaxableValue.ShouldBe(Money.Rupees(486m));
        line.Taxes.Select(tax => (tax.Kind, tax.Amount.Amount)).ShouldBe([("CGST", 12.15m), ("SGST", 12.15m)]);
        line.LineTotal.ShouldBe(Money.Rupees(510.3m));

        var totals = InvoiceLines.TotalsOf(Result([Priced(JobOne.ToString())]));
        totals.GrandTotal.ShouldBe(Money.Rupees(567m));
        totals.RoundOff.ShouldBe(Money.Zero);
    }

    [Fact]
    public void RefusesALineWhoseKeyIsNoJobOfTheOrderAndNamesTheLine()
    {
        var stranger = BillingTestData.Id("job-of-another-order");

        var notAJob = InvoiceLines.From(Result([Priced(JobOne.ToString()), Priced(stranger.ToString())]), Order());
        notAJob.IsFailure.ShouldBeTrue();
        notAJob.Error.Code.ShouldBe("billing.line-not-a-garment-job");
        notAJob.Error.Target.ShouldBe($"lines[{stranger}].lineKey");

        var notAGuid = InvoiceLines.From(Result([Priced("g1")]), Order());
        notAGuid.Error.Target.ShouldBe("lines[g1].lineKey");

        var order = Order();
        order.CancelJob(JobTwo, "ORD-1-02", "WRONG_SIZE", BillingTestData.Now);
        var cancelled = InvoiceLines.From(Result([Priced(JobOne.ToString()), Priced(JobTwo.ToString())]), order);
        cancelled.Error.Code.ShouldBe("billing.job-cancelled");
        cancelled.Error.Target.ShouldBe($"lines[{JobTwo}].lineKey");
    }

    [Fact]
    public void ChecksTheJobsOfARequestBeforeAnythingIsPricedOnTheSameGrounds()
    {
        var order = Order();
        order.CancelJob(JobTwo, "ORD-1-02", "WRONG_SIZE", BillingTestData.Now);

        var jobs = InvoiceLines.JobsOf([Request(JobOne.ToString())], order);
        jobs.IsSuccess.ShouldBeTrue();
        jobs.Value.ShouldBe([JobOne]);

        InvoiceLines.JobsOf([Request("g1")], order).Error.Code.ShouldBe("billing.line-not-a-garment-job");
        InvoiceLines.JobsOf([Request(JobTwo.ToString())], order).Error.Code.ShouldBe("billing.job-cancelled");
        var repeated = InvoiceLines.JobsOf([Request(JobOne.ToString()), Request(JobOne.ToString())], order);
        repeated.Error.Code.ShouldBe("billing.job-repeated");
        repeated.Error.Target.ShouldBe($"lines[{JobOne}].lineKey");
    }

    private static PricingLineRequest Request(string key) => new(key, "BLOUSE", 1m, [], null, null);

    private static OrderFact Order()
    {
        var order = OrderFact.Confirmed(OrderId, BillingTestData.Organisation, BillingTestData.MainBranch, BillingTestData.Id("customer-1"), "ORD-1", 1, BillingTestData.Now);
        order.AddJob(JobOne, "ORD-1-01", 1, BillingTestData.Now);
        order.AddJob(JobTwo, "ORD-1-02", 2, BillingTestData.Now);
        return order;
    }

    private static PricingResult Result(IReadOnlyList<PricedLine> lines)
        => new(
            BillingTestData.Id("plv-1"), BillingTestData.Id("tcv-1"), BillingTestData.Id("reg-1"), SupplyScheme.IntraState, false, BillingTestData.Now, lines,
            new PricedDocumentTotals(Money.Rupees(540m), Money.Zero, Money.Rupees(540m), Money.Rupees(13.5m), Money.Rupees(13.5m), Money.Zero, Money.Zero, Money.Zero, Money.Rupees(567m)));

    private static PricedLine Priced(string key, bool discount = false)
        => discount
            ? new PricedLine(
                key, "BLOUSE", "Blouse stitching", 1m, 450m, 450m, Money.Rupees(450m),
                [new PricedSurcharge("LINING", "Lining", 90m, Money.Rupees(90m))],
                new PricedDiscount("FESTIVE", "Percent", 10m, Money.Rupees(54m), false),
                Money.Rupees(486m), Money.Rupees(486m), "STITCHING_5", "998822", "Services",
                [new PricedTaxComponent("CGST", 2.5m, Money.Rupees(12.15m)), new PricedTaxComponent("SGST", 2.5m, Money.Rupees(12.15m))],
                Money.Rupees(24.3m), Money.Rupees(510.3m), Money.Zero, 0m, false)
            : new PricedLine(
                key, "BLOUSE", "Blouse stitching", 1m, 450m, 450m, Money.Rupees(450m),
                [new PricedSurcharge("LINING", "Lining", 90m, Money.Rupees(90m))],
                null, Money.Rupees(540m), Money.Rupees(540m), "STITCHING_5", "998822", "Services",
                [new PricedTaxComponent("CGST", 2.5m, Money.Rupees(13.5m)), new PricedTaxComponent("SGST", 2.5m, Money.Rupees(13.5m))],
                Money.Rupees(27m), Money.Rupees(567m), Money.Zero, 0m, false);
}
