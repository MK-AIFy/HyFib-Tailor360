using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Snapshots;

/// <summary>
/// One measured or chosen value inside a frozen measurement snapshot.
/// </summary>
/// <remarks>
/// <para>
/// A <strong>copy</strong> of what Customers recorded, not a reference to it (INV-JOB-01). It carries
/// the field key, the canonical millimetres, the unit the tailor entered in and the chosen option, so a
/// job card reprinted two years later renders exactly as it did at the counter — even though the
/// template has been republished, the field renamed and the customer measured again since.
/// </para>
/// <para>
/// Every value here is personal data of the most sensitive kind
/// (<c>docs/nfr/data-classification.md</c>). Nothing in this type is ever written to a log line, a
/// trace attribute, a metric or a problem detail, which is why the failures it produces name the field
/// key and never the number beside it.
/// </para>
/// </remarks>
public sealed record MeasuredValue
{
    /// <summary>The longest field key the column holds. Matches Customers' <c>FieldKey.MaximumLength</c>.</summary>
    public const int MaximumKeyLength = 60;

    /// <summary>The longest unit the column holds.</summary>
    public const int MaximumUnitLength = 20;

    /// <summary>The longest option code the column holds. Matches Catalog's <c>CatalogCode.MaximumLength</c>.</summary>
    public const int MaximumChoiceLength = 60;

    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, deliberately: a positional record generates a <em>public</em>
    /// constructor, so any caller could have built a value carrying both a reading and a choice, or
    /// neither. The properties are get-only rather than <c>init</c> for the same reason — it closes the
    /// <c>with</c> expression, which would otherwise be a second way past the factory and, on a frozen
    /// snapshot, a way to change what a confirmed job says (INV-JOB-01).
    /// </remarks>
    private MeasuredValue(string key, decimal? millimetres, string? enteredUnit, string? choice, bool acknowledged)
    {
        Key = key;
        Millimetres = millimetres;
        EnteredUnit = enteredUnit;
        Choice = choice;
        Acknowledged = acknowledged;
    }

    /// <summary>The field key values are filed under across template versions.</summary>
    public string Key { get; }

    /// <summary>The canonical value in millimetres, or null for a field that is chosen rather than measured.</summary>
    public decimal? Millimetres { get; }

    /// <summary>
    /// The unit the reading was entered in, so a sheet reads back the way it was taken. Null on a field
    /// that was chosen rather than measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A string rather than an enumeration, because the vocabulary belongs to Customers (INV-MTV-05) and
    /// copying an enumeration across the boundary would tie one module's release to another's — the
    /// opposite of what ARCH-004 is for. Orders stores the word it was given and never interprets it.
    /// </para>
    /// <para>
    /// <strong>Null for a choice.</strong> Customers' own <c>MeasurementValue.Chosen</c> says it in as
    /// many words — "a choice carries no unit" — and stores a placeholder only because its
    /// <c>DisplayUnit</c> is not nullable. Carrying that placeholder across the boundary would print
    /// "regular, in centimetres" on a job card, so a unit offered alongside a choice is dropped here
    /// rather than frozen onto the garment for ever (INV-JOB-01).
    /// </para>
    /// </remarks>
    public string? EnteredUnit { get; }

    /// <summary>The chosen option's code, or null for a measured field.</summary>
    public string? Choice { get; }

    /// <summary>
    /// Whether an unusual value was explicitly accepted by the person who took it.
    /// </summary>
    /// <remarks>
    /// Copied from the measurement rather than re-derived, because the range that made it unusual lives
    /// on a template version that may since have changed. A reading a tailor deliberately accepted and
    /// one nobody looked at are different facts about the same number.
    /// </remarks>
    public bool Acknowledged { get; }

    /// <summary>True when this answers a choice field rather than a measured one.</summary>
    public bool IsChoice => Choice is not null;

    /// <summary>
    /// Validates one value of a snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one of <paramref name="millimetres"/> and <paramref name="choice"/> may be set. Neither
    /// or both is <see cref="OrdersErrors.MeasuredValueAmbiguous"/>: a job card that cannot say whether
    /// a field was measured or chosen is a job card a tailor has to guess from.
    /// </para>
    /// <para>
    /// <strong>A reading is a positive length.</strong> Zero and below are refused rather than stored,
    /// because the snapshot is immutable from confirmation (INV-JOB-01) and a garment cut to a chest of
    /// −500&#160;mm has no correction path at all. There is no upper bound: what counts as unusual is a
    /// band on the template version Customers owns, and a reading the person who took it deliberately
    /// accepted is carried as <see cref="Acknowledged"/>.
    /// </para>
    /// <para>
    /// <paramref name="enteredUnit"/> belongs to a reading and is required with one; a choice has no
    /// unit, so a unit offered alongside one is dropped rather than printed.
    /// </para>
    /// </remarks>
    /// <param name="key">The field key the value answers.</param>
    /// <param name="millimetres">The canonical reading, for a measured field.</param>
    /// <param name="enteredUnit">The unit it was entered in. Ignored for a choice field.</param>
    /// <param name="choice">The chosen option's code, for a choice field.</param>
    /// <param name="acknowledged">Whether an unusual value was explicitly accepted.</param>
    /// <returns>The value, or the first failure found.</returns>
    public static Result<MeasuredValue> Create(
        string? key,
        decimal? millimetres,
        string? enteredUnit,
        string? choice,
        bool acknowledged)
    {
        var fieldKey = key?.Trim();

        if (string.IsNullOrEmpty(fieldKey))
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.Required("key"));
        }

        if (fieldKey.Length > MaximumKeyLength)
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.TooLong("key", MaximumKeyLength));
        }

        var chosen = choice?.Trim();
        chosen = string.IsNullOrEmpty(chosen) ? null : chosen;

        if (chosen is { Length: > MaximumChoiceLength })
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.TooLong("choice", MaximumChoiceLength));
        }

        // Both or neither. The field key travels as the error's target so the screen can point at the
        // row that is wrong; the reading itself never travels anywhere.
        if (millimetres.HasValue == (chosen is not null))
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.MeasuredValueAmbiguous(fieldKey));
        }

        // A length is positive. Asked before the unit, because a reading of zero is wrong whatever unit
        // somebody typed it in.
        if (millimetres is { } reading && reading <= 0m)
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.MeasurementNotPositive(fieldKey));
        }

        var unit = enteredUnit?.Trim();

        // A chosen option was not entered in anything: "regular" is not centimetres, and a job card that
        // prints a unit beside it asks the tailor to read a measurement that was never taken.
        if (chosen is not null)
        {
            return Result.Success(new MeasuredValue(fieldKey, millimetres, null, chosen, acknowledged));
        }

        if (string.IsNullOrEmpty(unit))
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.Required("enteredUnit"));
        }

        if (unit.Length > MaximumUnitLength)
        {
            return Result.Failure<MeasuredValue>(OrdersErrors.TooLong("enteredUnit", MaximumUnitLength));
        }

        return Result.Success(new MeasuredValue(fieldKey, millimetres, unit, chosen, acknowledged));
    }
}
