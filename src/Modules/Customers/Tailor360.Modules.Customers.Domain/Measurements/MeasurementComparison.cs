using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>What happened to one field between two measurements.</summary>
public enum MeasurementChange
{
    /// <summary>Measured in both, and the same.</summary>
    Unchanged = 0,

    /// <summary>Measured in both, and different.</summary>
    Changed = 1,

    /// <summary>Measured in the newer one only.</summary>
    Added = 2,

    /// <summary>Measured in the older one only.</summary>
    Dropped = 3,
}

/// <summary>
/// One field, as it stood in each of two measurements.
/// </summary>
/// <remarks>
/// Both sides are kept even when only one holds a value, because the interesting rows are exactly the ones where
/// a side is missing: a field that was measured last time and is not offered now is a field a reviewer has to be
/// told about, and one reported only as "absent" reads as an oversight.
/// </remarks>
/// <param name="Key">The field key, which is what values are filed under across template versions.</param>
/// <param name="Change">What happened to it.</param>
/// <param name="Before">What the older measurement held, or null when it did not hold this field.</param>
/// <param name="After">What the newer measurement held, or null when it does not hold this field.</param>
public sealed record MeasurementDifference(
    string Key,
    MeasurementChange Change,
    MeasurementValue? Before,
    MeasurementValue? After);

/// <summary>
/// What changed between two measurements of one customer against one template.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Matched on the field key, never on identity.</strong> A template version mints a new identifier for
/// every field it carries, so two measurements taken against different versions share no field identity at all —
/// matching on it would report every field as dropped and re-added, which is the opposite of useful on the case
/// this exists for. The key is what captured values are filed under precisely so that it survives a version
/// change (<c>docs/prd/measurement-templates.md</c> section 3).
/// </para>
/// <para>
/// <strong>A renamed field therefore reads as one dropped and one added</strong>, which is what a rename *is*
/// once values are filed under a key: the old values stay under the old key forever, and the new key has none.
/// Reporting it as a single renamed row would imply the numbers moved across, and they did not.
/// </para>
/// <para>
/// <strong>Only two measurements of the same template may be compared.</strong> A chest measured for a blouse and
/// a chest measured for a salwar kameez are the same word and not the same measurement — the bands, the precision
/// and the point the tape starts from all belong to the template.
/// </para>
/// </remarks>
public static class MeasurementComparison
{
    /// <summary>Compares two measurements, oldest first.</summary>
    /// <param name="before">The older measurement.</param>
    /// <param name="after">The newer measurement.</param>
    /// <returns>Every field either holds, in key order, or the reason they cannot be compared.</returns>
    public static Result<IReadOnlyList<MeasurementDifference>> Compare(
        MeasurementVersion before,
        MeasurementVersion after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.CustomerId != after.CustomerId || before.TemplateId != after.TemplateId)
        {
            return Result.Failure<IReadOnlyList<MeasurementDifference>>(
                MeasurementErrors.ComparisonSubjectsDoNotMatch);
        }

        var older = before.Values.ToDictionary(value => value.Key.Value, StringComparer.Ordinal);
        var newer = after.Values.ToDictionary(value => value.Key.Value, StringComparer.Ordinal);

        // Key order rather than display order: the two measurements may have been taken against different
        // template versions, whose display orders disagree, and a comparison that flipped rows depending on which
        // side it read from would be unreadable. Alphabetical is arbitrary and stable, which is what this needs.
        var keys = older.Keys.Union(newer.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        return Result.Success<IReadOnlyList<MeasurementDifference>>(
            [
                .. keys.Select(key =>
                {
                    var held = older.GetValueOrDefault(key);
                    var now = newer.GetValueOrDefault(key);

                    return new MeasurementDifference(key, ChangeOf(held, now), held, now);
                }),
            ]);
    }

    private static MeasurementChange ChangeOf(MeasurementValue? before, MeasurementValue? after)
        => (before, after) switch
        {
            (null, not null) => MeasurementChange.Added,
            (not null, null) => MeasurementChange.Dropped,
            (not null, not null) when Same(before, after) => MeasurementChange.Unchanged,
            _ => MeasurementChange.Changed,
        };

    /// <summary>
    /// Whether two recorded values are the same measurement.
    /// </summary>
    /// <remarks>
    /// The canonical value and the choice decide it. The unit it was entered in and whether an unusual value was
    /// acknowledged are both facts about the <em>taking</em> rather than about the measurement: a chest recorded
    /// as 36 in and the same chest recorded as 91.4 cm are one measurement, and reporting them as a change would
    /// send a reviewer looking for a difference that is not there.
    /// </remarks>
    private static bool Same(MeasurementValue before, MeasurementValue after)
        => before.Millimetres == after.Millimetres
           && string.Equals(before.Choice, after.Choice, StringComparison.Ordinal);
}
