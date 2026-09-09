using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// How finely one field is entered and shown, per display unit.
/// </summary>
/// <remarks>
/// <para>
/// Precision belongs to the field and to the display unit at once, which is why it is one value object rather than
/// a number: a tailor works to eighths of an inch on a sleeve and sixteenths on a neckline, and the same field is
/// one decimal place in centimetres either way (<c>docs/prd/measurement-templates.md</c> section 2).
/// </para>
/// <para>
/// The inch step is a fraction denominator rather than a decimal count because that is how the measurement is
/// spoken and written: "fourteen and a half", "three and three-sixteenths". Rounding an inch value to two decimals
/// would produce 3.19 in, which is not a number anybody can find on a tape.
/// </para>
/// <para>
/// Which step the tailors actually work to is open decision <strong>OD-MEA-07</strong>; the seeded values are
/// eighths generally and sixteenths for the neckline and shaping fields.
/// </para>
/// </remarks>
/// <param name="InchFraction">
/// The inch step as a denominator: 8 means the field moves in eighths. Zero for a field with no inch display.
/// </param>
/// <param name="CentimetreDecimals">Decimal places in centimetres. Zero for a field with no centimetre display.</param>
public sealed record FieldPrecision(int InchFraction, int CentimetreDecimals)
{
    /// <summary>The inch denominators a field may use.</summary>
    /// <remarks>
    /// Powers of two, because a tape is divided by halving. A denominator of 3 or 10 has no marking to read against,
    /// and 32 is finer than the tape itself; either would be a template that cannot be captured accurately.
    /// </remarks>
    public static readonly IReadOnlySet<int> PermittedInchFractions = new HashSet<int> { 2, 4, 8, 16 };

    /// <summary>The most decimal places a centimetre field may declare.</summary>
    /// <remarks>
    /// One decimal place is a millimetre, which is already finer than a tailor's tape can be read. Two is permitted
    /// so that a field converted from a sixteenth-inch template does not lose its step; beyond that the number is
    /// longer than the accuracy behind it.
    /// </remarks>
    public const int MaximumCentimetreDecimals = 2;

    /// <summary>The precision of a length in the seeded templates: eighths of an inch, one decimal centimetre.</summary>
    public static FieldPrecision Eighths { get; } = new(8, 1);

    /// <summary>The precision of the neckline and shaping fields: sixteenths, one decimal centimetre.</summary>
    public static FieldPrecision Sixteenths { get; } = new(16, 1);

    /// <summary>The precision of a count or a choice: no fraction, no decimals.</summary>
    public static FieldPrecision Whole { get; } = new(0, 0);

    /// <summary>Whether this field is shown in the given unit at all.</summary>
    /// <param name="unit">The display unit.</param>
    /// <returns>True when the field declares a precision for that unit.</returns>
    public bool Supports(DisplayUnit unit) => unit switch
    {
        DisplayUnit.Inch => InchFraction > 0,
        DisplayUnit.Centimetre => CentimetreDecimals > 0,
        DisplayUnit.Count => this == Whole,
        _ => false,
    };

    /// <summary>Checks the precision is one a tape can be read to.</summary>
    /// <returns>Success, or why it is not usable.</returns>
    public Result Validate()
    {
        if (this == Whole)
        {
            return Result.Success();
        }

        if (InchFraction != 0 && !PermittedInchFractions.Contains(InchFraction))
        {
            return Result.Failure(MeasurementErrors.PrecisionNotPermitted(InchFraction));
        }

        if (CentimetreDecimals is < 0 or > MaximumCentimetreDecimals)
        {
            return Result.Failure(MeasurementErrors.DecimalsNotPermitted(CentimetreDecimals));
        }

        return InchFraction == 0 && CentimetreDecimals == 0
            ? Result.Failure(MeasurementErrors.PrecisionMissing)
            : Result.Success();
    }
}
