namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The one place a measurement changes unit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A value is converted once, on the way in, and rounded only for display.</strong> That single sentence is
/// the whole of <c>docs/prd/measurement-templates.md</c> section 2, and every re-make caused by a measurement error
/// this code can prevent comes from breaking it: converting a value that was already converted, or storing what the
/// screen showed rather than what the tape said.
/// </para>
/// <para>
/// The constants are exact. 25.4 millimetres to the inch is a definition, not a measurement, and 10 millimetres to
/// the centimetre likewise — so <see cref="decimal"/> arithmetic on them is exact too, where <see cref="double"/>
/// would introduce an error in a value that a tailor then cuts fabric against.
/// </para>
/// <para>
/// Rounding is half-up rather than banker's. A tailor asked to explain why 14 1/2 became 14 1/2 and 15 1/2 became
/// 15 1/2 but 16 1/2 became 16 1/2 would be right to distrust the whole system; "the halfway mark goes up" is a
/// rule that can be stated at the counter. Measurements are positive, so half-up and away-from-zero coincide.
/// </para>
/// </remarks>
public static class UnitConversion
{
    /// <summary>Millimetres in one inch. Exact by definition.</summary>
    public const decimal MillimetresPerInch = 25.4m;

    /// <summary>Millimetres in one centimetre. Exact by definition.</summary>
    public const decimal MillimetresPerCentimetre = 10m;

    /// <summary>
    /// Decimal places kept in storage.
    /// </summary>
    /// <remarks>
    /// Two, and the reason is arithmetic rather than taste: the finest step any field may declare is a sixteenth of
    /// an inch, which is 1.5875 mm. Storing to two decimals loses at most 0.005 mm, and half a step is 0.79375 mm —
    /// more than a hundred times the error — so a stored value always rounds back to the step it was entered on.
    /// One decimal place would not: the error would be 0.05 mm, still safe here, but with no margin left for a finer
    /// step somebody adds later.
    /// </remarks>
    public const int StorageDecimals = 2;

    /// <summary>Converts a value a person entered into the millimetres that are stored.</summary>
    /// <param name="entered">The value as typed, in <paramref name="unit"/>.</param>
    /// <param name="unit">The unit it was typed in.</param>
    /// <returns>The canonical value, rounded to <see cref="StorageDecimals"/>.</returns>
    /// <remarks>
    /// This is the only rounding a stored value ever undergoes. <see cref="ToDisplay"/> does not write anything
    /// back, so a value cannot be re-rounded by being looked at.
    /// </remarks>
    public static decimal ToMillimetres(decimal entered, DisplayUnit unit)
    {
        var millimetres = unit switch
        {
            DisplayUnit.Inch => entered * MillimetresPerInch,
            DisplayUnit.Centimetre => entered * MillimetresPerCentimetre,
            DisplayUnit.Count => entered,
            _ => entered,
        };

        return unit == DisplayUnit.Count
            ? decimal.Round(millimetres, 0, MidpointRounding.AwayFromZero)
            : decimal.Round(millimetres, StorageDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Renders a stored value in the unit a reader chose, at the field's precision.</summary>
    /// <param name="millimetres">The canonical value.</param>
    /// <param name="unit">The unit to show it in.</param>
    /// <param name="precision">The field's precision.</param>
    /// <returns>The value to display.</returns>
    public static decimal ToDisplay(decimal millimetres, DisplayUnit unit, FieldPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);

        return unit switch
        {
            DisplayUnit.Inch => RoundToFraction(millimetres / MillimetresPerInch, precision.InchFraction),
            DisplayUnit.Centimetre => decimal.Round(
                millimetres / MillimetresPerCentimetre,
                precision.CentimetreDecimals,
                MidpointRounding.AwayFromZero),
            DisplayUnit.Count => decimal.Round(millimetres, 0, MidpointRounding.AwayFromZero),
            _ => millimetres,
        };
    }

    /// <summary>Rounds to the nearest whole multiple of <c>1/denominator</c>.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="denominator">The fraction denominator; zero or one means whole numbers.</param>
    /// <returns>The rounded value.</returns>
    public static decimal RoundToFraction(decimal value, int denominator)
        => denominator <= 1
            ? decimal.Round(value, 0, MidpointRounding.AwayFromZero)
            : decimal.Round(value * denominator, 0, MidpointRounding.AwayFromZero) / denominator;

    /// <summary>Whether a display value sits exactly on the field's step.</summary>
    /// <param name="entered">The value as typed.</param>
    /// <param name="unit">The unit it was typed in.</param>
    /// <param name="precision">The field's precision.</param>
    /// <returns>True when the value needs no rounding to be expressible at that precision.</returns>
    /// <remarks>
    /// Used to refuse a value that is finer than the field admits rather than silently rounding it. A tailor who
    /// typed 3.1 in on an eighths field meant something; rounding it to 3 1/8 without saying so is how a wrong
    /// measurement reaches the cutting table looking deliberate.
    /// </remarks>
    public static bool IsOnStep(decimal entered, DisplayUnit unit, FieldPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(precision);

        return ToDisplay(ToMillimetres(entered, unit), unit, precision) == entered;
    }
}
