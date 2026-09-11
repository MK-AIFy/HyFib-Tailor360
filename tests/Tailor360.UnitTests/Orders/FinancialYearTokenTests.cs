using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The four-digit financial-year token every display number carries.
/// </summary>
/// <remarks>
/// <para>
/// The token is the two-digit year the financial year opens in followed by the two-digit year it closes
/// in — <c>2627</c> for 2026-27 — which <c>docs/architecture/conventions.md</c> section 2.3 records as
/// <strong>proposed, to be confirmed</strong> under COD-03. It is not settled, so these tests pin the
/// shape the domain implements rather than asserting a decided format.
/// </para>
/// <para>
/// A transposed year is caught here rather than printed on a document, which is the whole point of
/// having a type: <c>2726</c> reads as a year, sorts as a year and is not one.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FinancialYearTokenTests
{
    [Theory]
    [InlineData(2026, "2627")]
    [InlineData(2000, "0001")]
    [InlineData(2098, "9899")]
    public void AYearIsWrittenAsTheYearItOpensInFollowedByTheYearItCloses(int startYear, string expected)
        => FinancialYear.FromStartYear(startYear).Value.Token.ShouldBe(expected);

    [Theory]
    [InlineData(2026, 4, 1, "2627")]
    [InlineData(2026, 9, 4, "2627")]
    [InlineData(2027, 3, 31, "2627")]
    [InlineData(2027, 4, 1, "2728")]
    [InlineData(2026, 3, 31, "2526")]
    public void TheYearABranchLocalDateFallsInStartsOnTheFirstOfApril(
        int year,
        int month,
        int day,
        string expected)
    {
        // Business dates are evaluated in the branch timezone, never in UTC, so the date arrives already
        // resolved (conventions.md section 2.2) and this type never touches a clock.
        var financialYear = FinancialYear.Containing(new DateOnly(year, month, day));

        financialYear.Value.Token.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingTokenIsRefused(string? token)
    {
        var financialYear = FinancialYear.Create(token);

        financialYear.IsFailure.ShouldBeTrue();
        financialYear.Error.Code.ShouldBe("orders.value-required");
        financialYear.Error.Target.ShouldBe("financialYear");
    }

    [Theory]
    [InlineData("262")]
    [InlineData("26277")]
    [InlineData("26-27")]
    [InlineData("ab27")]
    [InlineData("26 27")]
    public void ATokenThatIsNotFourDigitsIsRefused(string token)
    {
        var financialYear = FinancialYear.Create(token);

        financialYear.IsFailure.ShouldBeTrue();
        financialYear.Error.Code.ShouldBe("orders.financial-year-not-well-formed");
    }

    /// <summary>
    /// The year it closes in is the year it opens in plus one. Anything else is a transposition or a typo
    /// on a document somebody will later try to match against a ledger.
    /// </summary>
    [Theory]
    [InlineData("2726")]
    [InlineData("2628")]
    [InlineData("2626")]
    [InlineData("2026")]
    public void ATokenWhoseTwoHalvesAreNotConsecutiveIsRefused(string token)
    {
        var financialYear = FinancialYear.Create(token);

        financialYear.IsFailure.ShouldBeTrue();
        financialYear.Error.Code.ShouldBe("orders.financial-year-not-well-formed");
    }

    /// <summary>
    /// <c>9900</c> would have to mean 2099-2100, which the two-digit token cannot say and the addressable
    /// range therefore refuses. The range is what stops a document being numbered in a year this format
    /// cannot express.
    /// </summary>
    [Fact]
    public void ATokenOutsideTheAddressableRangeIsRefused()
    {
        var financialYear = FinancialYear.Create("9900");

        financialYear.IsFailure.ShouldBeTrue();
        financialYear.Error.Code.ShouldBe("orders.financial-year-not-well-formed");
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2099)]
    public void AYearOutsideTheAddressableRangeCannotBeComposedEither(int startYear)
    {
        var financialYear = FinancialYear.FromStartYear(startYear);

        financialYear.IsFailure.ShouldBeTrue();
        financialYear.Error.Code.ShouldBe("orders.financial-year-not-well-formed");
    }

    [Fact]
    public void ATokenIsTrimmedBeforeItIsRead()
    {
        FinancialYear.Create(" 2627 ").Value.Token.ShouldBe("2627");
    }

    [Theory]
    [InlineData("2627", true)]
    [InlineData("2726", false)]
    [InlineData(null, false)]
    public void TheWellFormedCheckAgreesWithTheFactory(string? token, bool expected)
        => FinancialYear.IsWellFormed(token).ShouldBe(expected);

    [Fact]
    public void TwoTokensForTheSameYearAreTheSameValue()
    {
        // A value object, so two copies of one year compare equal and a dictionary keyed by one works.
        FinancialYear.Create("2627").Value.ShouldBe(FinancialYear.FromStartYear(2026).Value);
    }

    [Fact]
    public void AYearPrintsAsItsToken()
        => FinancialYear.FromStartYear(2026).Value.ToString().ShouldBe("2627");

    /// <summary>
    /// <see cref="FinancialYear.Create"/> and <see cref="FinancialYear.FromStartYear"/> are the only ways
    /// in: a public constructor or an <c>init</c> setter would let a transposed year reach a printed
    /// document, which is the one thing this type exists to prevent.
    /// </summary>
    [Fact]
    public void AYearCannotBeConstructedOrRewrittenAroundItsFactory()
    {
        typeof(FinancialYear)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();

        typeof(FinancialYear)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.SetMethod == null);
    }
}
