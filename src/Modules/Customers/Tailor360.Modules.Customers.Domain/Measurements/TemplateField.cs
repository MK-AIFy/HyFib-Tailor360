using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// One thing a tailor measures, and everything the wizard needs to ask for it.
/// </summary>
/// <remarks>
/// <para>
/// A field is configuration: an administrator adds one, changes a label, widens a band or points it at a new
/// diagram, and no deployment follows. What a field may <em>not</em> change once its version is published is its
/// key — that is what captured values are filed under.
/// </para>
/// <para>
/// Two things are derived rather than stored, so that a field and its description cannot disagree. The display
/// units come from the canonical unit and the precision: a length shown in inches is exactly a length whose
/// precision declares an inch step. The diagram reference comes from the sheet key and the field key, because
/// <c>docs/prd/measurement-templates.md</c> section 8 defines it as <c>&lt;diagram_key&gt;#&lt;field_key&gt;</c> —
/// storing the anchor separately would let it drift from the field it anchors.
/// </para>
/// <para>
/// Whether a value is a body or a finished measurement is stated in the help text rather than in a column. That is
/// the proposed default of open decision <strong>OD-MEA-04</strong>, which asks whether the ease convention should
/// become structured data; until it is settled, adding the column would be answering it.
/// </para>
/// </remarks>
public sealed class TemplateField
{
    /// <summary>The longest label the column holds.</summary>
    public const int MaximumLabelLength = 120;

    /// <summary>The longest group name the column holds.</summary>
    public const int MaximumGroupLength = 60;

    /// <summary>The longest help text the column holds.</summary>
    public const int MaximumHelpTextLength = 400;

    /// <summary>The longest diagram key the column holds.</summary>
    public const int MaximumDiagramKeyLength = 80;

    /// <summary>The longest alternative text the column holds.</summary>
    public const int MaximumDiagramAltLength = 400;

    private TemplateField()
    {
        // Entity Framework materialises through this; Key is set by the property initialiser below.
        Key = null!;
        Label = null!;
        GroupName = null!;
        HelpText = null!;
        Precision = FieldPrecision.Whole;
        Bands = ValidationBands.None;
    }

    /// <summary>The field's row identity within its version.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation that owns it.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The version it belongs to.</summary>
    public Guid TemplateVersionId { get; private set; }

    /// <summary>What captured values are filed under.</summary>
    public FieldKey Key { get; private set; }

    /// <summary>What staff read.</summary>
    public string Label { get; private set; }

    /// <summary>The Tamil label, where one has been supplied (OD-MEA-10).</summary>
    public string? LabelTamil { get; private set; }

    /// <summary>The wizard step and printed-sheet section this field sits in.</summary>
    public string GroupName { get; private set; }

    /// <summary>The order a tailor measures in, not the order the alphabet gives.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>What the stored value is.</summary>
    public CanonicalUnit CanonicalUnit { get; private set; }

    /// <summary>How finely the field is entered, per display unit.</summary>
    public FieldPrecision Precision { get; private set; }

    /// <summary>The bands that refuse and the bands that ask.</summary>
    public ValidationBands Bands { get; private set; }

    /// <summary>Whether a value is needed before a capture can be confirmed, while the field is shown.</summary>
    public bool IsRequired { get; private set; }

    /// <summary>One sentence saying where the tape starts and ends.</summary>
    public string HelpText { get; private set; }

    /// <summary>The bundled line-drawing sheet, until uploaded media exists (#31).</summary>
    public string? DiagramKey { get; private set; }

    /// <summary>The uploaded diagram, once #31 exists.</summary>
    public Guid? DiagramMediaId { get; private set; }

    /// <summary>The measuring path in words, for a screen reader and the printed sheet.</summary>
    public string? DiagramAlt { get; private set; }

    /// <summary>When the field is shown, or null when it always is.</summary>
    public ConditionalRule? Rule { get; private set; }

    /// <summary>The choices, for a field that is chosen rather than measured.</summary>
    /// <remarks>
    /// Replaced wholesale rather than mutated, so that the property can be mapped directly to the one JSON column
    /// that holds it. A list edited in place would need a backing field the mapping then has to reach through.
    /// </remarks>
    public IReadOnlyList<ChoiceOption> Options { get; private set; } = [];

    /// <summary>Whether this field is chosen from a list rather than measured with a tape.</summary>
    public bool IsChoice => CanonicalUnit == CanonicalUnit.None;

    /// <summary>The units this field may be shown and entered in.</summary>
    /// <remarks>Derived from the canonical unit and the precision, so the two cannot disagree.</remarks>
    public IReadOnlyList<DisplayUnit> DisplayUnits => CanonicalUnit switch
    {
        CanonicalUnit.Millimetre =>
        [
            .. new[] { DisplayUnit.Inch, DisplayUnit.Centimetre }.Where(unit => Precision.Supports(unit)),
        ],
        CanonicalUnit.Count => [DisplayUnit.Count],
        _ => [],
    };

    /// <summary>The diagram reference, as section 8 defines it.</summary>
    public string? DiagramReference => DiagramKey is null ? null : $"{DiagramKey}#{Key.Value}";

    /// <summary>Creates a field.</summary>
    /// <param name="id">The row identity.</param>
    /// <param name="organisationId">The owning organisation.</param>
    /// <param name="versionId">The version it belongs to.</param>
    /// <param name="definition">Everything the administrator supplied.</param>
    /// <returns>The field, or the first reason it was refused.</returns>
    public static Result<TemplateField> Create(
        Guid id,
        Guid organisationId,
        Guid versionId,
        TemplateFieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var key = FieldKey.Create(definition.Key);

        if (key.IsFailure)
        {
            return Result.Failure<TemplateField>(key.Error);
        }

        var field = new TemplateField
        {
            Id = id,
            OrganisationId = organisationId,
            TemplateVersionId = versionId,
            Key = key.Value,
        };

        var applied = field.Apply(definition);

        return applied.IsFailure ? Result.Failure<TemplateField>(applied.Error) : Result.Success(field);
    }

    /// <summary>Replaces everything about the field except its key.</summary>
    /// <param name="definition">The new definition.</param>
    /// <returns>Success, or the first reason it was refused.</returns>
    public Result Apply(TemplateFieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var checkedDefinition = Check(Key, definition);

        if (checkedDefinition.IsFailure)
        {
            return checkedDefinition;
        }

        Label = definition.Label.Trim();
        LabelTamil = string.IsNullOrWhiteSpace(definition.LabelTamil) ? null : definition.LabelTamil.Trim();
        GroupName = definition.GroupName.Trim();
        DisplayOrder = definition.DisplayOrder;
        CanonicalUnit = definition.CanonicalUnit;
        Precision = definition.Precision;
        Bands = definition.Bands;
        IsRequired = definition.IsRequired;
        HelpText = definition.HelpText.Trim();
        DiagramKey = string.IsNullOrWhiteSpace(definition.DiagramKey) ? null : definition.DiagramKey.Trim();
        DiagramMediaId = definition.DiagramMediaId;
        DiagramAlt = string.IsNullOrWhiteSpace(definition.DiagramAlt) ? null : definition.DiagramAlt.Trim();
        Rule = definition.Rule;

        Options =
        [
            .. definition.Options
                .OrderBy(option => option.DisplayOrder)
                .ThenBy(option => option.Code, StringComparer.Ordinal),
        ];

        return Result.Success();
    }

    private static Result Check(FieldKey key, TemplateFieldDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Label))
        {
            return Result.Failure(MeasurementErrors.Required($"fields[{key.Value}].label"));
        }

        if (definition.Label.Length > MaximumLabelLength)
        {
            return Result.Failure(MeasurementErrors.TooLong($"fields[{key.Value}].label", MaximumLabelLength));
        }

        if (string.IsNullOrWhiteSpace(definition.GroupName))
        {
            return Result.Failure(MeasurementErrors.Required($"fields[{key.Value}].group"));
        }

        if (definition.GroupName.Length > MaximumGroupLength)
        {
            return Result.Failure(MeasurementErrors.TooLong($"fields[{key.Value}].group", MaximumGroupLength));
        }

        if (definition.DisplayOrder < 0)
        {
            return Result.Failure(MeasurementErrors.DisplayOrderNegative($"fields[{key.Value}]"));
        }

        // Required for every field, because it is the only place the difference between a body and a finished
        // measurement is written down (section 5), and confusing the two is the commonest cause of a re-make.
        if (string.IsNullOrWhiteSpace(definition.HelpText))
        {
            return Result.Failure(MeasurementErrors.Required($"fields[{key.Value}].helpText"));
        }

        if (definition.HelpText.Length > MaximumHelpTextLength)
        {
            return Result.Failure(MeasurementErrors.TooLong($"fields[{key.Value}].helpText", MaximumHelpTextLength));
        }

        var shape = CheckShape(key, definition);

        if (shape.IsFailure)
        {
            return shape;
        }

        var diagram = CheckDiagram(key, definition);

        return diagram.IsFailure ? diagram : definition.Rule?.Validate(key) ?? Result.Success();
    }

    private static Result CheckShape(FieldKey key, TemplateFieldDefinition definition)
    {
        if (definition.CanonicalUnit == CanonicalUnit.None)
        {
            if (definition.Options.Count == 0)
            {
                return Result.Failure(MeasurementErrors.ChoiceFieldHasNoOptions(key.Value));
            }

            foreach (var option in definition.Options)
            {
                var validated = option.Validate(key);

                if (validated.IsFailure)
                {
                    return validated;
                }
            }

            var duplicate = definition.Options
                .GroupBy(option => option.Code, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            return duplicate is null
                ? Result.Success()
                : Result.Failure(MeasurementErrors.ChoiceCodeMalformed(key.Value, duplicate.Key));
        }

        if (definition.Options.Count > 0)
        {
            return Result.Failure(MeasurementErrors.NumericFieldHasOptions(key.Value));
        }

        var precision = definition.Precision.Validate();

        if (precision.IsFailure)
        {
            return precision;
        }

        // A count is whole by definition, so an inch step or a decimal place on one is a rule that could never be
        // applied — there is no unit to apply it in.
        if (definition.CanonicalUnit == CanonicalUnit.Count && definition.Precision != FieldPrecision.Whole)
        {
            return Result.Failure(MeasurementErrors.PrecisionNotAllowedForUnit(key.Value, DisplayUnit.Count));
        }

        if (definition.CanonicalUnit == CanonicalUnit.Millimetre && definition.Precision == FieldPrecision.Whole)
        {
            return Result.Failure(MeasurementErrors.PrecisionMissing);
        }

        return definition.Bands.Validate(key);
    }

    private static Result CheckDiagram(FieldKey key, TemplateFieldDefinition definition)
    {
        var hasDiagram = !string.IsNullOrWhiteSpace(definition.DiagramKey) || definition.DiagramMediaId is not null;

        if (!hasDiagram)
        {
            return Result.Success();
        }

        if (definition.DiagramKey is { Length: > MaximumDiagramKeyLength })
        {
            return Result.Failure(
                MeasurementErrors.TooLong($"fields[{key.Value}].diagramKey", MaximumDiagramKeyLength));
        }

        if (string.IsNullOrWhiteSpace(definition.DiagramAlt))
        {
            return Result.Failure(MeasurementErrors.DiagramAltMissing(key.Value));
        }

        return definition.DiagramAlt.Length > MaximumDiagramAltLength
            ? Result.Failure(MeasurementErrors.TooLong($"fields[{key.Value}].diagramAlt", MaximumDiagramAltLength))
            : Result.Success();
    }
}

/// <summary>Everything an administrator supplies about one field.</summary>
/// <param name="Key">The stable key. Ignored when editing: a published key never changes, and a draft's key is set once.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="GroupName">The wizard step this field sits in.</param>
/// <param name="DisplayOrder">The order the tape runs in.</param>
/// <param name="CanonicalUnit">What the stored value is.</param>
/// <param name="Precision">How finely it is entered.</param>
/// <param name="Bands">The refusing and asking bands.</param>
/// <param name="IsRequired">Whether it must be answered while shown.</param>
/// <param name="HelpText">Where the tape starts and ends, and whether the value is a body or a finished measurement.</param>
/// <param name="DiagramKey">The bundled sheet, or null.</param>
/// <param name="DiagramMediaId">The uploaded diagram, or null (#31).</param>
/// <param name="DiagramAlt">The measuring path in words. Required whenever a diagram is referenced.</param>
/// <param name="Rule">When the field is shown, or null when it always is.</param>
/// <param name="Options">The choices, for a choice field.</param>
public sealed record TemplateFieldDefinition(
    string Key,
    string Label,
    string? LabelTamil,
    string GroupName,
    int DisplayOrder,
    CanonicalUnit CanonicalUnit,
    FieldPrecision Precision,
    ValidationBands Bands,
    bool IsRequired,
    string HelpText,
    string? DiagramKey,
    Guid? DiagramMediaId,
    string? DiagramAlt,
    ConditionalRule? Rule,
    IReadOnlyList<ChoiceOption> Options);
