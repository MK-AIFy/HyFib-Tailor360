using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The conversion guarantees issue #27 lists as acceptance criteria.
/// </summary>
/// <remarks>
/// <para>
/// These are property tests written as exhaustive sweeps rather than random samples, because the space is finite
/// and small: a field measured in eighths of an inch between 250 mm and 900 mm has a few hundred representable
/// values, and checking all of them proves the property instead of failing to disprove it. A generator would be
/// weaker here, not stronger.
/// </para>
/// <para>
/// The round trip is the one that matters. If <c>toDisplay(fromDisplay(x))</c> ever differed from <c>x</c>, a
/// tailor would enter 14 1/2, save, reopen and read 14 3/8 — and would stop trusting every number on the screen,
/// including the ones that were right.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class UnitConversionTests
{
    [Fact]
    public void TheConstantsAreTheExactDefinitions()
    {
        // Definitions, not measurements. A test looks redundant until somebody "simplifies" 25.4 to 25 or reaches
        // for a double, at which point every stored measurement in the shop moves by up to 1.6%.
        UnitConversion.MillimetresPerInch.ShouldBe(25.4m);
        UnitConversion.MillimetresPerCentimetre.ShouldBe(10m);
    }

    [Theory]
    [InlineData(8, 250, 900)]
    [InlineData(8, 40, 800)]
    [InlineData(16, 30, 500)]
    [InlineData(16, 100, 350)]
    public void EveryRepresentableInchValueSurvivesTheRoundTrip(int fraction, int minimumMm, int maximumMm)
    {
        var precision = new FieldPrecision(fraction, 1);
        var step = 1m / fraction;
        var first = Math.Ceiling(minimumMm / UnitConversion.MillimetresPerInch / step) * step;
        var last = Math.Floor(maximumMm / UnitConversion.MillimetresPerInch / step) * step;
        var checkedValues = 0;

        for (var entered = first; entered <= last; entered += step)
        {
            var stored = UnitConversion.ToMillimetres(entered, DisplayUnit.Inch);
            var shown = UnitConversion.ToDisplay(stored, DisplayUnit.Inch, precision);

            shown.ShouldBe(entered, $"{entered} in stored as {stored} mm came back as {shown} in");
            checkedValues++;
        }

        checkedValues.ShouldBeGreaterThan(20, "the sweep should cover the field's whole range");
    }

    [Theory]
    [InlineData(250, 900)]
    [InlineData(550, 1500)]
    [InlineData(1500, 8000)]
    public void EveryRepresentableCentimetreValueSurvivesTheRoundTrip(int minimumMm, int maximumMm)
    {
        var precision = FieldPrecision.Eighths;
        var checkedValues = 0;

        // One decimal place in centimetres is a millimetre, so the sweep is every millimetre of the range.
        for (var millimetres = minimumMm; millimetres <= maximumMm; millimetres++)
        {
            var entered = millimetres / 10m;
            var stored = UnitConversion.ToMillimetres(entered, DisplayUnit.Centimetre);
            var shown = UnitConversion.ToDisplay(stored, DisplayUnit.Centimetre, precision);

            shown.ShouldBe(entered, $"{entered} cm stored as {stored} mm came back as {shown} cm");
            checkedValues++;
        }

        checkedValues.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void ConversionIsMonotonic()
    {
        // A bigger measurement is a bigger number, in both directions and in both units. Without this a warning
        // band could be crossed by a value that is inside it.
        decimal? previousFromInches = null;
        decimal? previousFromCentimetres = null;

        for (var eighths = 1; eighths <= 400; eighths++)
        {
            var inches = eighths / 8m;
            var fromInches = UnitConversion.ToMillimetres(inches, DisplayUnit.Inch);
            var fromCentimetres = UnitConversion.ToMillimetres(eighths / 10m, DisplayUnit.Centimetre);

            if (previousFromInches is { } lastInch)
            {
                fromInches.ShouldBeGreaterThan(lastInch);
            }

            if (previousFromCentimetres is { } lastCentimetre)
            {
                fromCentimetres.ShouldBeGreaterThan(lastCentimetre);
            }

            previousFromInches = fromInches;
            previousFromCentimetres = fromCentimetres;
        }
    }

    [Fact]
    public void TheWorkedExampleFromTheSpecificationHolds()
    {
        // docs/prd/measurement-templates.md section 2: Reception enters 14 1/2 in, the client sends 368.30 mm, a
        // tailor working in centimetres sees 36.8 cm, and the same record printed in inches shows 14 1/2 again.
        var stored = UnitConversion.ToMillimetres(14.5m, DisplayUnit.Inch);

        stored.ShouldBe(368.30m);
        UnitConversion.ToDisplay(stored, DisplayUnit.Centimetre, FieldPrecision.Eighths).ShouldBe(36.8m);
        UnitConversion.ToDisplay(stored, DisplayUnit.Inch, FieldPrecision.Eighths).ShouldBe(14.5m);
    }

    [Fact]
    public void RoundingIsHalfUpRatherThanToEven()
    {
        // 3.25 cm is exactly on the halfway mark for one decimal place. Half-up gives 3.3; banker's rounding would
        // give 3.2, and "the halfway mark sometimes goes down" is not a rule anybody can state at the counter.
        UnitConversion.ToDisplay(32.5m, DisplayUnit.Centimetre, FieldPrecision.Eighths).ShouldBe(3.3m);
        UnitConversion.ToDisplay(42.5m, DisplayUnit.Centimetre, FieldPrecision.Eighths).ShouldBe(4.3m);
    }

    [Fact]
    public void AValueFinerThanTheFieldIsNotOnItsStep()
    {
        // 3.1 in is not a mark on a tape divided into eighths. Rounding it silently to 3 1/8 is how a wrong
        // measurement reaches the cutting table looking deliberate, so the field refuses it instead.
        UnitConversion.IsOnStep(3.1m, DisplayUnit.Inch, FieldPrecision.Eighths).ShouldBeFalse();
        UnitConversion.IsOnStep(3.125m, DisplayUnit.Inch, FieldPrecision.Eighths).ShouldBeTrue();
        UnitConversion.IsOnStep(3.0625m, DisplayUnit.Inch, FieldPrecision.Eighths).ShouldBeFalse();
        UnitConversion.IsOnStep(3.0625m, DisplayUnit.Inch, FieldPrecision.Sixteenths).ShouldBeTrue();
    }

    [Theory]
    [InlineData(DisplayUnit.Inch)]
    [InlineData(DisplayUnit.Centimetre)]
    public void ConvertedBandsKeepTheirOrder(DisplayUnit unit)
    {
        // The plan states this as a property to test: min ≤ warn_low ≤ warn_high ≤ max has to survive conversion,
        // or a band that reads correctly in millimetres would refuse a value it warns about in inches.
        var bands = new ValidationBands(250m, 900m, 330m, 520m);
        var precision = FieldPrecision.Eighths;

        var minimum = UnitConversion.ToDisplay(bands.MinimumMillimetres, unit, precision);
        var warnBelow = UnitConversion.ToDisplay(bands.WarnBelowMillimetres!.Value, unit, precision);
        var warnAbove = UnitConversion.ToDisplay(bands.WarnAboveMillimetres!.Value, unit, precision);
        var maximum = UnitConversion.ToDisplay(bands.MaximumMillimetres, unit, precision);

        minimum.ShouldBeLessThanOrEqualTo(warnBelow);
        warnBelow.ShouldBeLessThanOrEqualTo(warnAbove);
        warnAbove.ShouldBeLessThanOrEqualTo(maximum);
    }

    [Fact]
    public void ACountIsNeitherConvertedNorRounded()
    {
        // kali_count is a number of panels. Multiplying it by 25.4 would be absurd, and the type system cannot say
        // so — the canonical unit does.
        UnitConversion.ToMillimetres(12m, DisplayUnit.Count).ShouldBe(12m);
        UnitConversion.ToDisplay(12m, DisplayUnit.Count, FieldPrecision.Whole).ShouldBe(12m);
    }
}
