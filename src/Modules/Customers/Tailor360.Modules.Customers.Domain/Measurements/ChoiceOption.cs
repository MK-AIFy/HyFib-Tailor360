using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// One choice a non-numeric field offers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The code is stored; the label is shown.</strong> A measurement version records <c>ELASTIC</c>, never
/// "Elastic" — because the label is editable and localisable, and a record that stored the label would change
/// meaning the day somebody corrected a spelling (<c>docs/prd/measurement-templates.md</c> section 10).
/// </para>
/// <para>
/// Choice fields exist here at all only until open decision <strong>OD-MEA-03</strong> settles whether
/// <c>waist_finish</c> and <c>age_band</c> belong to the template or to the design options. Section 10 instructs
/// the seed to keep them here meanwhile, treated as a field whose canonical unit is
/// <see cref="CanonicalUnit.None"/>.
/// </para>
/// </remarks>
/// <param name="Code">The stored value.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, where one has been supplied (OD-MEA-10).</param>
/// <param name="DisplayOrder">Where it sits in the list.</param>
public sealed partial record ChoiceOption(string Code, string Label, string? LabelTamil, int DisplayOrder)
{
    /// <summary>The longest code the column holds.</summary>
    public const int MaximumCodeLength = 40;

    /// <summary>The longest label the column holds.</summary>
    public const int MaximumLabelLength = 120;

    /// <summary>Checks the option is storable and readable.</summary>
    /// <param name="key">The field it belongs to, for the problem detail's field name.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Validate(FieldKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (string.IsNullOrWhiteSpace(Code) || Code.Length > MaximumCodeLength || !CodePattern().IsMatch(Code))
        {
            return Result.Failure(MeasurementErrors.ChoiceCodeMalformed(key.Value, Code));
        }

        if (string.IsNullOrWhiteSpace(Label) || Label.Length > MaximumLabelLength)
        {
            return Result.Failure(MeasurementErrors.Required($"fields[{key.Value}].options[{Code}].label"));
        }

        return DisplayOrder < 0
            ? Result.Failure(MeasurementErrors.DisplayOrderNegative($"fields[{key.Value}].options[{Code}]"))
            : Result.Success();
    }

    // Upper case, digits and underscores. Digits lead in the seeded age bands (`0_1`, `12_14`), so the first
    // character is not restricted to a letter the way a field key's is.
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
