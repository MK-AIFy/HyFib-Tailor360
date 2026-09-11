using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The priced result frozen onto an order and onto each of its garments.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot for display and printing. Billing's <c>IFinancialTotalsQuery</c> holds the authoritative
/// money position (INV-ORD-07) and this module performs no money arithmetic at all: it stores what
/// Billing returned, and it refuses to store a figure that could not have come from one calculation.
/// </para>
/// <para>
/// Three rules make that checkable rather than a matter of trust — every amount in one currency,
/// INV-ORD-02's three configuration versions present, and INV-INV-04's two tax schemes never carried
/// side by side.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PriceSnapshotTests
{
    /* Configuration versions -------------------------------------------------------------------- */

    [Fact]
    public void APricedResultRecordsTheThreeVersionsItWasCalculatedFrom()
    {
        var price = Price();

        price.IsSuccess.ShouldBeTrue();
        price.Value.CatalogVersionId.ShouldBe(OrdersTestData.CatalogVersion);
        price.Value.PriceListVersionId.ShouldBe(OrdersTestData.PriceListVersion);
        price.Value.TaxConfigurationVersionId.ShouldBe(OrdersTestData.TaxConfigurationVersion);
        price.Value.CalculatedAt.ShouldBe(OrdersTestData.Now);
    }

    /// <summary>
    /// INV-ORD-02: a confirmed order records exactly one catalogue version, one price-list version and
    /// one tax configuration version. A figure that cannot be recomputed from its own snapshot is a
    /// defect, and a missing version is what would make it one.
    /// </summary>
    [Theory]
    [InlineData("catalogVersionId")]
    [InlineData("priceListVersionId")]
    [InlineData("taxConfigurationVersionId")]
    public void APricedResultThatCannotBeRecomputedFromItsOwnSnapshotIsRefused(string field)
    {
        var price = PriceSnapshot.Create(
            field == "catalogVersionId" ? Guid.Empty : OrdersTestData.CatalogVersion,
            field == "priceListVersionId" ? Guid.Empty : OrdersTestData.PriceListVersion,
            field == "taxConfigurationVersionId" ? Guid.Empty : OrdersTestData.TaxConfigurationVersion,
            Money.Rupees(2400m),
            Money.Zero,
            Money.Rupees(2400m),
            Money.Rupees(60m),
            Money.Rupees(60m),
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Rupees(2520m),
            OrdersTestData.Now);

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.configuration-version-missing");
        price.Error.Target.ShouldBe(field);
    }

    /* Currency ---------------------------------------------------------------------------------- */

    /// <summary>
    /// A default <c>Money</c> never went through its constructor, so it carries no currency code at all.
    /// Left alone it reaches <see cref="PriceSnapshot.TaxTotal"/> and throws there, deep inside a job
    /// card being rendered, rather than being refused at the boundary where the caller can still be told.
    /// </summary>
    [Fact]
    public void APricedResultWithNoCurrencyAtAllIsRefusedAtTheBoundary()
    {
        var price = Price(subtotal: default(Money));

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.value-required");
        price.Error.Target.ShouldBe("currency");
    }

    [Fact]
    public void EveryAmountOnOneDocumentIsInTheSameCurrency()
    {
        var price = Price(discountTotal: new Money(100m, "USD"));

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.currency-mismatch");
    }

    /// <summary>
    /// An amount that was never filled in is caught by the same rule, because a default carries no
    /// currency and therefore cannot be in the document's one.
    /// </summary>
    [Fact]
    public void AnAmountNobodyFilledInIsCaughtAsACurrencyMismatch()
    {
        var price = Price(cess: default(Money));

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.currency-mismatch");
    }

    /* Amounts ----------------------------------------------------------------------------------- */

    [Theory]
    [InlineData("subtotal")]
    [InlineData("discountTotal")]
    [InlineData("taxableValue")]
    [InlineData("grandTotal")]
    public void AnAmountBelowZeroIsRefusedAndTheFieldIsNamed(string field)
    {
        var negative = Money.Rupees(-1m);

        var price = PriceSnapshot.Create(
            OrdersTestData.CatalogVersion,
            OrdersTestData.PriceListVersion,
            OrdersTestData.TaxConfigurationVersion,
            field == "subtotal" ? negative : Money.Rupees(2400m),
            field == "discountTotal" ? negative : Money.Zero,
            field == "taxableValue" ? negative : Money.Rupees(2400m),
            Money.Rupees(60m),
            Money.Rupees(60m),
            Money.Zero,
            Money.Zero,
            Money.Zero,
            field == "grandTotal" ? negative : Money.Rupees(2520m),
            OrdersTestData.Now);

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.amount-negative");
        price.Error.Target.ShouldBe(field);
    }

    /// <summary>
    /// A discount is carried as a positive amount and subtracted, so a negative discount is a defect
    /// rather than a surcharge — which is why the rule above names it as well as the totals.
    /// </summary>
    [Fact]
    public void ADiscountIsCarriedAsAPositiveAmountAndNeverAsASurcharge()
    {
        var price = Price(discountTotal: Money.Rupees(-250m));

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.amount-negative");
        price.Error.Target.ShouldBe("discountTotal");
    }

    /// <summary>
    /// The document round-off is the one amount that may be negative: rounding a total down is shown,
    /// never absorbed (<c>docs/architecture/conventions.md</c> section 1.2).
    /// </summary>
    [Fact]
    public void TheRoundOffIsTheOneAmountThatMayBeBelowZero()
    {
        var price = Price(roundOff: Money.Rupees(-0.35m));

        price.IsSuccess.ShouldBeTrue();
        price.Value.RoundOff.ShouldBe(Money.Rupees(-0.35m));
    }

    /* Tax --------------------------------------------------------------------------------------- */

    /// <summary>
    /// INV-INV-04: place of supply decides the scheme, so a supply is either within the state or between
    /// states and never both. A document carrying CGST or SGST beside IGST claims it was both.
    /// </summary>
    [Theory]
    [InlineData(60, 0)]
    [InlineData(0, 60)]
    [InlineData(60, 60)]
    public void ADocumentNeverCarriesTheWithinStateTaxesBesideTheBetweenStatesOne(
        int centralTax,
        int stateTax)
    {
        var price = Price(
            centralTax: Money.Rupees(centralTax),
            stateTax: Money.Rupees(stateTax),
            integratedTax: Money.Rupees(120m));

        price.IsFailure.ShouldBeTrue();
        price.Error.Code.ShouldBe("orders.both-tax-schemes-present");
    }

    [Fact]
    public void ASupplyBetweenStatesCarriesTheIntegratedTaxAlone()
    {
        var price = Price(
            centralTax: Money.Zero,
            stateTax: Money.Zero,
            integratedTax: Money.Rupees(120m));

        price.IsSuccess.ShouldBeTrue();
        price.Value.IsIntegratedSupply.ShouldBeTrue();
        price.Value.TaxTotal.ShouldBe(Money.Rupees(120m));
    }

    [Fact]
    public void ASupplyWithinTheStateCarriesTheTwoHalvesAndIsNotAnIntegratedSupply()
    {
        var price = Price();

        price.IsSuccess.ShouldBeTrue();
        price.Value.IsIntegratedSupply.ShouldBeFalse();
        price.Value.TaxTotal.ShouldBe(Money.Rupees(120m));
    }

    [Fact]
    public void TheTaxTotalCountsTheCessToo()
    {
        // The cess is a tax on the supply like the others; leaving it out of the total would print a
        // document whose parts do not add up to what the customer is asked for.
        var price = Price(cess: Money.Rupees(30m));

        price.Value.TaxTotal.ShouldBe(Money.Rupees(150m));
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// <see cref="PriceSnapshot.Create"/> is the only way in: a public constructor or an <c>init</c>
    /// setter would be a second route past the currency, sign and tax-scheme rules above, and this type
    /// is what a printed document renders from.
    /// </summary>
    [Fact]
    public void APricedResultCannotBeConstructedOrRewrittenAroundItsFactory()
    {
        typeof(PriceSnapshot)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();

        typeof(PriceSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.SetMethod == null);
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    private static Result<PriceSnapshot> Price(
        Money? subtotal = null,
        Money? discountTotal = null,
        Money? taxableValue = null,
        Money? centralTax = null,
        Money? stateTax = null,
        Money? integratedTax = null,
        Money? cess = null,
        Money? roundOff = null,
        Money? grandTotal = null)
        => PriceSnapshot.Create(
            OrdersTestData.CatalogVersion,
            OrdersTestData.PriceListVersion,
            OrdersTestData.TaxConfigurationVersion,
            subtotal ?? Money.Rupees(2400m),
            discountTotal ?? Money.Zero,
            taxableValue ?? Money.Rupees(2400m),
            centralTax ?? Money.Rupees(60m),
            stateTax ?? Money.Rupees(60m),
            integratedTax ?? Money.Zero,
            cess ?? Money.Zero,
            roundOff ?? Money.Zero,
            grandTotal ?? Money.Rupees(2520m),
            OrdersTestData.Now);
}
