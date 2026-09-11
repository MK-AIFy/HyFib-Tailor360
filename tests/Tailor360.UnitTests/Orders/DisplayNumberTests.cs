using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The three numbers this module prints: <c>O-&lt;branch&gt;-&lt;FY&gt;-000001</c>,
/// <c>E-…</c> and <c>J-…-01</c>.
/// </summary>
/// <remarks>
/// <para>
/// They are display numbers and never identities: a path, a deep link and a customer link carry the
/// UUIDv7, and a sequential integer is never exposed as one (<c>CLAUDE.md</c> section 4 rule 8). They
/// are also never a barcode payload, which is opaque and carries no meaning at all
/// (<c>docs/architecture/conventions.md</c> section 3.3, INV-BID-04).
/// </para>
/// <para>
/// A garment number carries the order number it was minted from rather than repeating its parts, so the
/// relationship between the two is structural: a job card printed for one order cannot be filed against
/// another, and that is checked by comparing objects and not text.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DisplayNumberTests
{
    /* Branch codes ------------------------------------------------------------------------------ */

    [Theory]
    [InlineData("CBE01", "CBE01")]
    [InlineData("cbe01", "CBE01")]
    [InlineData("  cbe01  ", "CBE01")]
    [InlineData("M", "M")]
    public void ABranchCodeIsFoldedToUpperCaseSoOneBranchHasOneSeries(string given, string expected)
    {
        var branchCode = DisplayNumberFormat.NormaliseBranchCode(given);

        branchCode.IsSuccess.ShouldBeTrue();
        branchCode.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADisplayNumberWithoutABranchIsRefused(string? branchCode)
    {
        // Branch scope is evaluated, never inferred: the branch is the sequence key every display number
        // is allocated from, so there is no number without one.
        var normalised = DisplayNumberFormat.NormaliseBranchCode(branchCode);

        normalised.IsFailure.ShouldBeTrue();
        normalised.Error.Code.ShouldBe("orders.value-required");
        normalised.Error.Target.ShouldBe("branchCode");
    }

    [Theory]
    [InlineData("CBE 01")]
    [InlineData("CBE-01")]
    [InlineData("CBE_01")]
    [InlineData("CBE.01")]
    [InlineData("CBE0123456789012345")]
    public void ABranchCodeThatWouldBreakTheNumberApartIsRefused(string branchCode)
    {
        // A separator or a space inside the branch code would make the printed number unparseable, and a
        // number that cannot be read back off a document is one nobody can look up at the counter.
        var normalised = DisplayNumberFormat.NormaliseBranchCode(branchCode);

        normalised.IsFailure.ShouldBeTrue();
        normalised.Error.Code.ShouldBe("orders.branch-code-not-well-formed");
    }

    [Theory]
    [InlineData(1, "000001")]
    [InlineData(4182, "004182")]
    [InlineData(1_234_567, "1234567")]
    public void ASequenceIsPaddedToSixDigitsAndGrowsBeyondThemRatherThanWrapping(long sequence, string expected)
        => DisplayNumberFormat.FormatSequence(sequence).ShouldBe(expected);

    /* Order numbers ----------------------------------------------------------------------------- */

    [Fact]
    public void AnOrderNumberIsItsNamespaceBranchYearAndSequence()
    {
        var number = OrderNumber.Create("CBE01", OrdersTestData.Year(), 4182);

        number.IsSuccess.ShouldBeTrue();
        number.Value.Value.ShouldBe("O-CBE01-2627-004182");
        number.Value.BranchCode.ShouldBe("CBE01");
        number.Value.FinancialYear.Token.ShouldBe("2627");
        number.Value.Sequence.ShouldBe(4182);
    }

    [Fact]
    public void AnEstimateNumberIsTheSameShapeInItsOwnSeries()
    {
        // The estimate series is its own; an estimate is never posted and never consumes an invoice
        // number (state-transitions.md section 2.2).
        var number = EstimateNumber.Create("CBE01", OrdersTestData.Year(), 12);

        number.IsSuccess.ShouldBeTrue();
        number.Value.Value.ShouldBe("E-CBE01-2627-000012");
    }

    /// <summary>
    /// INV-ORD-03: a display-number sequence starts at one and is never reused, so a zero is a caller
    /// that never asked the allocator for anything.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASequenceBelowTheFirstPositionIsRefused(long sequence)
    {
        var number = OrderNumber.Create("CBE01", OrdersTestData.Year(), sequence);

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.sequence-out-of-range");
        number.Error.Target.ShouldBe("sequence");
    }

    [Fact]
    public void AnEstimateSequenceBelowTheFirstPositionIsRefusedToo()
    {
        var number = EstimateNumber.Create("CBE01", OrdersTestData.Year(), 0);

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.sequence-out-of-range");
    }

    [Fact]
    public void AnOrderNumberCannotBeComposedWithoutABranch()
    {
        var number = OrderNumber.Create("  ", OrdersTestData.Year(), 1);

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.value-required");
    }

    /* Reading a printed number back ------------------------------------------------------------- */

    [Fact]
    public void APrintedOrderNumberReadsBackIntoItsParts()
    {
        var number = OrderNumber.Parse("O-CBE01-2627-004182");

        number.IsSuccess.ShouldBeTrue();
        number.Value.BranchCode.ShouldBe("CBE01");
        number.Value.FinancialYear.Token.ShouldBe("2627");
        number.Value.Sequence.ShouldBe(4182);
    }

    [Fact]
    public void ANumberTypedInLowerCaseOrWithStraySpacesStillFindsTheOrder()
    {
        // Somebody reading a number off a printed slip at the counter should not be refused over the
        // shift key.
        OrderNumber.Parse("  o-cbe01-2627-004182  ").Value.Value.ShouldBe("O-CBE01-2627-004182");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNumberIsRefusedAsMissingRatherThanAsMalformed(string? value)
    {
        var number = OrderNumber.Parse(value);

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.value-required");
        number.Error.Target.ShouldBe("orderNumber");
    }

    [Theory]
    [InlineData("E-CBE01-2627-004182")]
    [InlineData("J-CBE01-2627-004182-01")]
    [InlineData("O-CBE01-2627-4182")]
    [InlineData("O-CBE01-2627")]
    [InlineData("O--2627-004182")]
    [InlineData("O-CBE01-2627-004182-01")]
    [InlineData("OCBE012627004182")]
    public void SomethingThatIsNotANumberThisSystemIssuesIsRefused(string value)
    {
        var number = OrderNumber.Parse(value);

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.display-number-malformed");
        number.Error.Target.ShouldBe("orderNumber");
    }

    /// <summary>
    /// An estimate number is not an order number and never resolves as one, which is what keeps the two
    /// series from being read across at a counter.
    /// </summary>
    [Fact]
    public void TheThreeSeriesNeverReadAsEachOther()
    {
        OrderNumber.IsWellFormed("E-CBE01-2627-000012").ShouldBeFalse();
        EstimateNumber.IsWellFormed("O-CBE01-2627-004182").ShouldBeFalse();
        GarmentJobNumber.IsWellFormed("O-CBE01-2627-004182").ShouldBeFalse();
    }

    [Fact]
    public void ANumberWhoseYearIsTransposedIsRefusedByTheYearRatherThanTheShape()
    {
        var number = OrderNumber.Parse("O-CBE01-2726-004182");

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.financial-year-not-well-formed");
    }

    [Fact]
    public void AnOrderNumberLongerThanTheColumnHoldsIsRefused()
    {
        var number = OrderNumber.Parse("O-CBE01-2627-" + new string('9', OrderNumber.MaximumLength));

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.value-too-long");
    }

    /// <summary>
    /// A sequence of more digits than a <c>long</c> holds is malformed rather than out of range: nothing
    /// this system allocated could have produced it.
    /// </summary>
    [Fact]
    public void ASequenceNoAllocatorCouldHaveIssuedIsMalformedRatherThanOutOfRange()
    {
        var number = OrderNumber.Parse("O-CBE01-2627-" + new string('9', 25));

        number.IsFailure.ShouldBeTrue();
        number.Error.Code.ShouldBe("orders.display-number-malformed");
    }

    /* Garment numbers --------------------------------------------------------------------------- */

    [Fact]
    public void AGarmentNumberIsMintedFromItsOrdersOwnNumber()
    {
        var orderNumber = OrdersTestData.Number(4182);

        var jobNumber = GarmentJobNumber.For(orderNumber, 1);

        jobNumber.IsSuccess.ShouldBeTrue();
        jobNumber.Value.Value.ShouldBe("J-CBE01-2627-004182-01");
        jobNumber.Value.OrderNumber.ShouldBe(orderNumber);
        jobNumber.Value.JobIndex.ShouldBe(1);
        jobNumber.Value.BranchCode.ShouldBe("CBE01");
        jobNumber.Value.FinancialYear.Token.ShouldBe("2627");
        jobNumber.Value.Sequence.ShouldBe(4182);
    }

    [Theory]
    [InlineData(1, "01")]
    [InlineData(9, "09")]
    [InlineData(12, "12")]
    [InlineData(120, "120")]
    public void AGarmentIndexIsPaddedToTwoDigitsAndGrowsBeyondThem(int jobIndex, string expected)
        => DisplayNumberFormat.FormatJobIndex(jobIndex).ShouldBe(expected);

    /// <summary>
    /// A garment's position within its order starts at one, so a zero is the default of an integer
    /// nobody filled in rather than the first garment.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AGarmentPositionBelowTheFirstIsRefused(int jobIndex)
    {
        var jobNumber = GarmentJobNumber.For(OrdersTestData.Number(), jobIndex);

        jobNumber.IsFailure.ShouldBeTrue();
        jobNumber.Error.Code.ShouldBe("orders.job-index-out-of-range");
    }

    [Fact]
    public void APrintedGarmentNumberReadsBackIntoItsOrderAndItsPosition()
    {
        var jobNumber = GarmentJobNumber.Parse("J-CBE01-2627-004182-03");

        jobNumber.IsSuccess.ShouldBeTrue();
        jobNumber.Value.OrderNumber.Value.ShouldBe("O-CBE01-2627-004182");
        jobNumber.Value.JobIndex.ShouldBe(3);
    }

    [Theory]
    [InlineData("J-CBE01-2627-004182")]
    [InlineData("J-CBE01-2627-004182-1")]
    [InlineData("J-CBE01-2627-004182-01-02")]
    [InlineData("O-CBE01-2627-004182-01")]
    public void SomethingThatIsNotAGarmentNumberIsRefused(string value)
    {
        var jobNumber = GarmentJobNumber.Parse(value);

        jobNumber.IsFailure.ShouldBeTrue();
        jobNumber.Error.Code.ShouldBe("orders.display-number-malformed");
        jobNumber.Error.Target.ShouldBe("jobNumber");
    }

    [Fact]
    public void AGarmentNumberWhosePositionIsZeroIsRefusedByThePositionRule()
    {
        var jobNumber = GarmentJobNumber.Parse("J-CBE01-2627-004182-00");

        jobNumber.IsFailure.ShouldBeTrue();
        jobNumber.Error.Code.ShouldBe("orders.job-index-out-of-range");
    }

    /// <summary>
    /// The relationship is structural rather than a convention two call sites share: the garment number
    /// holds the order number object, so a garment minted for another order is refused by comparing
    /// numbers and never by comparing the text of a printed card.
    /// </summary>
    [Fact]
    public void TwoGarmentsOfTheSameOrderShareOneOrderNumber()
    {
        var orderNumber = OrdersTestData.Number(4182);

        var first = GarmentJobNumber.For(orderNumber, 1).Value;
        var second = GarmentJobNumber.For(orderNumber, 2).Value;

        first.OrderNumber.ShouldBe(second.OrderNumber);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void TwoNumbersForTheSameOrderAreTheSameValue()
        => OrderNumber.Create("CBE01", OrdersTestData.Year(), 4182).Value
            .ShouldBe(OrderNumber.Parse("O-CBE01-2627-004182").Value);

    [Fact]
    public void EachNumberPrintsAsItself()
    {
        var orderNumber = OrdersTestData.Number(4182);

        orderNumber.ToString().ShouldBe("O-CBE01-2627-004182");
        EstimateNumber.Create("CBE01", OrdersTestData.Year(), 12).Value.ToString()
            .ShouldBe("E-CBE01-2627-000012");
        GarmentJobNumber.For(orderNumber, 1).Value.ToString().ShouldBe("J-CBE01-2627-004182-01");
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// The factories are the only ways in. A public constructor or an <c>init</c> setter would let a
    /// number be built that the allocator never issued, and a display number that was not allocated is
    /// one that may already be on another document.
    /// </summary>
    [Fact]
    public void ANumberCannotBeConstructedOrRewrittenAroundItsFactory()
    {
        foreach (var type in new[] { typeof(OrderNumber), typeof(EstimateNumber), typeof(GarmentJobNumber) })
        {
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ShouldAllBe(property => property.SetMethod == null);
        }
    }
}
