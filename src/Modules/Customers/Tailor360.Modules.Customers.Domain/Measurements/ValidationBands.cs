using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The two bands that guard a numeric field: one that refuses, one that asks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Hard bounds reject; the confirmation band asks and accepts.</strong> The distinction is the point
/// (<c>docs/prd/measurement-templates.md</c> section 7). Hard bounds are deliberately wide and exist only to catch
/// the impossible — a three-metre shoulder, a two-millimetre waist, a centimetre value typed into an inch field.
/// The confirmation band is where accuracy is actually checked, and it must never block: an unusual customer is
/// still a customer, and a system that refuses to record them teaches staff to type a lie.
/// </para>
/// <para>
/// The seeded numbers are open decision <strong>OD-MEA-08</strong>, to be reviewed against the paper register.
/// </para>
/// </remarks>
/// <param name="MinimumMillimetres">Below this the value is refused.</param>
/// <param name="MaximumMillimetres">Above this the value is refused.</param>
/// <param name="WarnBelowMillimetres">Below this the value is accepted after an acknowledgement. Null for no warning.</param>
/// <param name="WarnAboveMillimetres">Above this the value is accepted after an acknowledgement. Null for no warning.</param>
public sealed record ValidationBands(
    decimal MinimumMillimetres,
    decimal MaximumMillimetres,
    decimal? WarnBelowMillimetres,
    decimal? WarnAboveMillimetres)
{
    /// <summary>A field with no numeric bands at all — a choice field.</summary>
    public static ValidationBands None { get; } = new(0m, 0m, null, null);

    /// <summary>Whether this field declares bands.</summary>
    public bool AreDeclared => this != None;

    /// <summary>Checks the bands are orderable and the warning band sits inside the hard bounds.</summary>
    /// <param name="key">The field, for the problem detail's field name.</param>
    /// <returns>Success, or the first thing wrong with them.</returns>
    /// <remarks>
    /// A warning band outside the hard bounds is unreachable: the value is refused before anybody is asked to
    /// confirm it. That is a template a reviewer believed had a confirmation step and does not, so it is a
    /// publish-time error rather than a warning.
    /// </remarks>
    public Result Validate(FieldKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!AreDeclared)
        {
            return Result.Success();
        }

        if (MinimumMillimetres > MaximumMillimetres)
        {
            return Result.Failure(MeasurementErrors.BoundsOutOfOrder(key.Value));
        }

        if (WarnBelowMillimetres is { } low
            && (low < MinimumMillimetres || low > MaximumMillimetres))
        {
            return Result.Failure(MeasurementErrors.WarningOutsideBounds(key.Value, "warnBelow"));
        }

        if (WarnAboveMillimetres is { } high
            && (high < MinimumMillimetres || high > MaximumMillimetres))
        {
            return Result.Failure(MeasurementErrors.WarningOutsideBounds(key.Value, "warnAbove"));
        }

        return WarnBelowMillimetres is { } below
               && WarnAboveMillimetres is { } above
               && below > above
            ? Result.Failure(MeasurementErrors.WarningBandOutOfOrder(key.Value))
            : Result.Success();
    }

    /// <summary>Where a value falls.</summary>
    /// <param name="millimetres">The canonical value.</param>
    /// <returns>The verdict.</returns>
    public BandVerdict Judge(decimal millimetres)
    {
        if (!AreDeclared)
        {
            return BandVerdict.Inside;
        }

        if (millimetres < MinimumMillimetres || millimetres > MaximumMillimetres)
        {
            return BandVerdict.Refused;
        }

        return (WarnBelowMillimetres is { } low && millimetres < low)
               || (WarnAboveMillimetres is { } high && millimetres > high)
            ? BandVerdict.NeedsConfirmation
            : BandVerdict.Inside;
    }
}

/// <summary>What the bands say about one value.</summary>
public enum BandVerdict
{
    /// <summary>Ordinary. Nothing to say.</summary>
    Inside = 0,

    /// <summary>Unusual but possible. Accepted once somebody confirms they meant it.</summary>
    NeedsConfirmation = 1,

    /// <summary>Impossible. Refused with a field-level problem detail.</summary>
    Refused = 2,
}
