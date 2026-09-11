using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// One thing a tailor recorded against one field.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Stored in millimetres, always.</strong> INV-MTV-05: the display unit is presentation, inch fractions
/// are converted on entry and never stored as text. What is kept beside the number is the unit it was
/// <em>entered</em> in, so a sheet can be rendered back in the unit the person actually measured in — a tailor who
/// took a chest in inches and reads it back in centimetres will re-measure, and be right to.
/// </para>
/// <para>
/// A choice field has no number at all. Splitting the two into one type with two nullable halves rather than two
/// types is deliberate: a captured value is addressed, ordered, validated and rendered as one list, and a
/// hierarchy would make every one of those a type test. What keeps it honest is that exactly one half may be set,
/// checked here rather than trusted.
/// </para>
/// <para>
/// <see cref="Acknowledged"/> is the person's answer to a confirmation-band value, and it is kept on the value
/// rather than on the draft because it is about <em>this measurement</em>: "yes, the shoulder really is 62 cm".
/// Recording it once for the whole draft would leave nobody able to say which unusual number was accepted, which
/// is the only question ever asked of it afterwards.
/// </para>
/// </remarks>
public sealed class MeasurementValue
{
    private MeasurementValue()
    {
        // The persistence layer materialises instances through this constructor.
        Key = null!;
    }

    private MeasurementValue(
        FieldKey key,
        decimal? millimetres,
        string? choice,
        DisplayUnit enteredUnit,
        bool acknowledged)
    {
        Key = key;
        Millimetres = millimetres;
        Choice = choice;
        EnteredUnit = enteredUnit;
        Acknowledged = acknowledged;
    }

    /// <summary>The field this answers, by the key values are filed under across template versions.</summary>
    public FieldKey Key { get; private set; }

    /// <summary>The canonical value, or null for a field that is chosen rather than measured.</summary>
    public decimal? Millimetres { get; private set; }

    /// <summary>The chosen option's code, or null for a field that is measured.</summary>
    public string? Choice { get; private set; }

    /// <summary>The unit it was entered in, so a sheet reads back the way it was taken.</summary>
    public DisplayUnit EnteredUnit { get; private set; }

    /// <summary>Whether an unusual value was explicitly accepted by the person who took it.</summary>
    public bool Acknowledged { get; private set; }

    /// <summary>Whether this is an answer to a choice field.</summary>
    public bool IsChoice => Choice is not null;

    /// <summary>Records a measured value.</summary>
    /// <param name="key">The field.</param>
    /// <param name="millimetres">The canonical value.</param>
    /// <param name="enteredUnit">The unit it was entered in.</param>
    /// <param name="acknowledged">Whether an unusual value was explicitly accepted.</param>
    /// <returns>The value.</returns>
    public static MeasurementValue Measured(
        FieldKey key,
        decimal millimetres,
        DisplayUnit enteredUnit,
        bool acknowledged = false)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new MeasurementValue(key, millimetres, null, enteredUnit, acknowledged);
    }

    /// <summary>Records a chosen option.</summary>
    /// <param name="key">The field.</param>
    /// <param name="code">The option's code.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// A choice carries no unit and can never be unusual, so it is never acknowledged: the confirmation band is a
    /// numeric idea, and offering one for "sleeve style: puff" would ask a question with no meaning.
    /// </remarks>
    public static MeasurementValue Chosen(FieldKey key, string code)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new MeasurementValue(key, null, code, DisplayUnit.Inch, acknowledged: false);
    }

    /// <summary>
    /// Checks this value against the field it answers, on the version it will be filed under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is INV-MSR-04, and the reason it runs at confirmation rather than at draft time is that a draft is a
    /// half-measured garment: refusing an out-of-range shoulder while somebody is still holding the tape would
    /// teach them to type a plausible lie and correct it later, which is exactly the evidence this record exists
    /// to be. A draft accepts anything; a version accepts only what the template admits.
    /// </para>
    /// <para>
    /// The precision check is on the value as entered, not on the millimetres: 6⅛ inches is on the step and
    /// 155.575 mm is what it converts to, so checking the canonical number would refuse every fraction.
    /// </para>
    /// </remarks>
    /// <param name="field">The field on the template version being confirmed against.</param>
    /// <returns>Success, or the first thing wrong with it.</returns>
    public Result Validate(TemplateField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.IsChoice)
        {
            return ValidateChoice(field);
        }

        if (Choice is not null)
        {
            return Result.Failure(MeasurementErrors.ValueIsNotAChoice(Key.Value));
        }

        if (Millimetres is not { } millimetres)
        {
            return Result.Failure(MeasurementErrors.Required(Key.Value));
        }

        if (!field.Precision.Supports(EnteredUnit))
        {
            return Result.Failure(MeasurementErrors.UnitNotOffered(Key.Value, EnteredUnit));
        }

        // Bounds first, and the order is the point. A four-metre chest is out of bounds *and* off an eighth-inch
        // step, and telling somebody to round it to the nearest eighth is absurd advice about a value that was
        // read off the wrong tape or typed in the wrong unit. The wider mistake is named first.
        var verdict = field.Bands.AreDeclared ? field.Bands.Judge(millimetres) : BandVerdict.Inside;

        if (verdict == BandVerdict.Refused)
        {
            return Result.Failure(MeasurementErrors.ValueOutOfBounds(Key.Value));
        }

        // The canonical value has to survive being shown and read back. `ToDisplay` rounds to the field's step,
        // so a value that came off a tape returns unchanged and one that did not moves — which is the whole test.
        //
        // Asking `IsOnStep` about `ToDisplay`'s own output would be vacuous: it rounds first, so the answer is
        // always yes. That mistake is why this is written out rather than delegated.
        if (UnitConversion.ToMillimetres(
                UnitConversion.ToDisplay(millimetres, EnteredUnit, field.Precision), EnteredUnit)
            != millimetres)
        {
            return Result.Failure(MeasurementErrors.ValueOffStep(Key.Value, EnteredUnit));
        }

        return verdict == BandVerdict.NeedsConfirmation && !Acknowledged
            ? Result.Failure(MeasurementErrors.ValueNeedsAcknowledgement(Key.Value))
            : Result.Success();
    }

    private Result ValidateChoice(TemplateField field)
    {
        if (Millimetres is not null)
        {
            return Result.Failure(MeasurementErrors.ValueIsNotMeasured(Key.Value));
        }

        if (Choice is not { Length: > 0 } code)
        {
            return Result.Failure(MeasurementErrors.Required(Key.Value));
        }

        return field.Options.Any(option => string.Equals(option.Code, code, StringComparison.Ordinal))
            ? Result.Success()
            : Result.Failure(MeasurementErrors.ChoiceNotOffered(Key.Value, code));
    }
}
