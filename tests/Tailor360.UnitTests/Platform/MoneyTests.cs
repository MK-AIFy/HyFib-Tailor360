using Shouldly;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// Money arithmetic. These tests exist because a rounding mistake here becomes a mismatch between an
/// invoice and a cash drawer, which is the kind of defect a shop notices long after it is cheap to fix.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MoneyTests
{
    [Fact]
    public void DefaultsToIndianRupees()
        => Money.Rupees(10m).Currency.ShouldBe("INR");

    [Fact]
    public void RetainsFourDecimalPlacesInternally()
        => new Money(12.34567m).Amount.ShouldBe(12.3457m);

    [Fact]
    public void RoundsToTwoDecimalPlacesForDocuments()
        => new Money(12.345m).ToDocumentPrecision().Amount.ShouldBe(12.35m);

    [Fact]
    public void RoundsHalfAwayFromZeroAsTheConventionAndTheAccountantRequire()
    {
        // docs/architecture/conventions.md section 1.2: "half away from zero" is what half-up means for
        // the positive amounts this system handles, and it is what the golden master asserts. A borderline
        // line is rounded exactly once, so there is no repeated rounding for banker's rounding to guard.
        new Money(2.345m).ToDocumentPrecision().Amount.ShouldBe(2.35m);
        new Money(2.355m).ToDocumentPrecision().Amount.ShouldBe(2.36m);
        new Money(-2.345m).ToDocumentPrecision().Amount.ShouldBe(-2.35m);
        new Money(12.34565m).Amount.ShouldBe(12.3457m);
    }

    [Fact]
    public void AddsAndSubtractsWithinOneCurrency()
    {
        (Money.Rupees(1200.50m) + Money.Rupees(299.50m)).Amount.ShouldBe(1500.00m);
        (Money.Rupees(1500m) - Money.Rupees(500m)).Amount.ShouldBe(1000m);
    }

    [Fact]
    public void MultipliesByAQuantity()
        => (Money.Rupees(450m) * 3).Amount.ShouldBe(1350m);

    [Fact]
    public void RefusesToCombineDifferentCurrencies()
    {
        var rupees = Money.Rupees(100m);
        var dollars = new Money(100m, "USD");

        Should.Throw<InvalidOperationException>(() => rupees + dollars);
        Should.Throw<InvalidOperationException>(() => rupees - dollars);
        Should.Throw<InvalidOperationException>(() => rupees.CompareTo(dollars));
    }

    [Fact]
    public void RejectsAMalformedCurrencyCode()
    {
        Should.Throw<ArgumentException>(() => new Money(1m, "inr"));
        Should.Throw<ArgumentException>(() => new Money(1m, "RUPEE"));
        Should.Throw<ArgumentException>(() => new Money(1m, " "));
    }

    [Fact]
    public void ComparesAmounts()
    {
        (Money.Rupees(10m) < Money.Rupees(20m)).ShouldBeTrue();
        (Money.Rupees(20m) >= Money.Rupees(20m)).ShouldBeTrue();
    }

    [Fact]
    public void ReportsZeroAndNegativeAmounts()
    {
        Money.Zero.IsZero.ShouldBeTrue();
        Money.Rupees(-1m).IsNegative.ShouldBeTrue();
        (-Money.Rupees(5m)).Amount.ShouldBe(-5m);
    }

    [Fact]
    public void FormatsInvariantlyForLogsAndFixtures()
        => Money.Rupees(1234.5m).ToString().ShouldBe("INR 1234.5");
}
