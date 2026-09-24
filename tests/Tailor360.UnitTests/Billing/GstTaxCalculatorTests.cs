using Shouldly;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.UnitTests.Billing;

/// <summary>Financial results that must remain stable after an invoice has been posted.</summary>
[Trait("Category", "Unit")]
public sealed class GstTaxCalculatorTests
{
    [Fact]
    public void SplitsIntraStateGstIntoEqualComponents()
    {
        var line = GstTaxCalculator.Calculate(1000m, 18m, 0m, "33", "33");

        line.Cgst.ShouldBe(90m);
        line.Sgst.ShouldBe(90m);
        line.Igst.ShouldBe(0m);
        line.Total.ShouldBe(1180m);
    }

    [Fact]
    public void UsesIgstForInterStateSupplyAndAddsCess()
    {
        var line = GstTaxCalculator.Calculate(200m, 12m, 1m, "33", "29");

        line.Cgst.ShouldBe(0m);
        line.Sgst.ShouldBe(0m);
        line.Igst.ShouldBe(24m);
        line.Cess.ShouldBe(2m);
        line.Total.ShouldBe(226m);
    }

    [Fact]
    public void RoundsEachTaxComponentHalfAwayFromZeroOnce()
    {
        var line = GstTaxCalculator.Calculate(0.05m, 10m, 0m, "33", "29");

        line.Igst.ShouldBe(0.01m);
        line.Total.ShouldBe(0.06m);
    }

    [Fact]
    public void BillTotalsSumThePostedLineComponents()
    {
        var lines = new[]
        {
            GstTaxCalculator.Calculate(0.05m, 10m, 0m, "33", "29"),
            GstTaxCalculator.Calculate(0.05m, 10m, 0m, "33", "29"),
        };

        var bill = GstBillTotals.FromLines(lines);

        bill.TaxableAmount.ShouldBe(0.10m);
        bill.Igst.ShouldBe(0.02m);
        bill.Total.ShouldBe(0.12m);
    }

    [Fact]
    public void RejectsInvalidInputRatherThanProducingAnInvoiceAmount()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            GstTaxCalculator.Calculate(-1m, 18m, 0m, "33", "33"));
        Should.Throw<ArgumentException>(() =>
            GstTaxCalculator.Calculate(1.001m, 18m, 0m, "33", "33"));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            GstTaxCalculator.Calculate(1m, 100.001m, 0m, "33", "33"));
        Should.Throw<ArgumentException>(() =>
            GstTaxCalculator.Calculate(1m, 18m, 0m, "TN", "33"));
        Should.Throw<ArgumentException>(() => GstBillTotals.FromLines([
            GstTaxCalculator.Calculate(1m, 18m, 0m, "33", "33"),
            GstTaxCalculator.Calculate(1m, 18m, 0m, "33", "29"),
        ]));
        Should.Throw<ArgumentException>(() => GstBillTotals.FromLines([]));
    }
}
