using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>Creates a measurement template with no versions.</summary>
/// <param name="Code">The stable machine key, such as <c>MT_BLOUSE_PATTERN</c>.</param>
/// <param name="Name">What administrators read.</param>
/// <param name="Description">What it is for.</param>
public sealed record CreateMeasurementTemplateRequest(string Code, string Name, string? Description);

/// <summary>Starts a draft version, empty or copied from an existing one.</summary>
/// <param name="Name">What to call the draft.</param>
/// <param name="Notes">What is changing and why.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in: <c>Inch</c> or <c>Centimetre</c>.</param>
/// <param name="CloneFromVersionId">The version to copy, or null for an empty draft.</param>
public sealed record StartTemplateDraftRequest(
    string Name,
    string? Notes,
    string DefaultDisplayUnit,
    Guid? CloneFromVersionId);

/// <summary>A reason for an act the trail has to be able to explain.</summary>
/// <param name="Reason">Why.</param>
public sealed record TemplateReasonRequest(string? Reason);

/// <summary>One clause of a visibility rule.</summary>
/// <param name="Scope">Where the left-hand side is read from: <c>Field</c> or <c>DesignSelection</c>.</param>
/// <param name="Name">The field key, or the design option-group code.</param>
/// <param name="Operator">How it is compared: <c>IsAnyOf</c> or <c>Excludes</c>.</param>
/// <param name="Values">The codes compared against.</param>
public sealed record TemplateRuleClause(
    string Scope,
    string Name,
    string Operator,
    IReadOnlyList<string> Values);

/// <summary>When a field is shown.</summary>
/// <param name="Effect">Whether matching hides or shows: <c>HiddenWhen</c> or <c>ShownWhen</c>.</param>
/// <param name="AnyOf">The clauses, combined with <em>or</em>.</param>
public sealed record TemplateRule(string Effect, IReadOnlyList<TemplateRuleClause> AnyOf);

/// <summary>One choice a non-numeric field offers.</summary>
/// <param name="Code">The stored value.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="DisplayOrder">Where it sits in the list.</param>
public sealed record TemplateChoiceOption(
    string Code,
    string Label,
    string? LabelTamil,
    int DisplayOrder);

/// <summary>Everything an administrator supplies about one field.</summary>
/// <param name="Key">The stable key. Ignored when editing: a key is what captured values are filed under.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="GroupName">The wizard step this field sits in.</param>
/// <param name="DisplayOrder">The order the tape runs in.</param>
/// <param name="CanonicalUnit">What the stored value is: <c>Millimetre</c>, <c>Count</c> or <c>None</c>.</param>
/// <param name="InchFraction">The inch step as a denominator: 8 means eighths. Zero for no inch display.</param>
/// <param name="CentimetreDecimals">Decimal places in centimetres. Zero for no centimetre display.</param>
/// <param name="IsRequired">Whether it must be answered while shown.</param>
/// <param name="MinimumMillimetres">Below this the value is refused.</param>
/// <param name="MaximumMillimetres">Above this the value is refused.</param>
/// <param name="WarnBelowMillimetres">Below this the value needs an acknowledgement.</param>
/// <param name="WarnAboveMillimetres">Above this the value needs an acknowledgement.</param>
/// <param name="HelpText">Where the tape starts and ends, and whether the value is body or finished.</param>
/// <param name="DiagramKey">The bundled sheet, or null.</param>
/// <param name="DiagramMediaId">The uploaded diagram, or null (#31).</param>
/// <param name="DiagramAlt">The measuring path in words. Required whenever a diagram is referenced.</param>
/// <param name="Rule">When the field is shown, or null when it always is.</param>
/// <param name="Options">The choices, for a choice field.</param>
public sealed record TemplateFieldRequest(
    string Key,
    string Label,
    string? LabelTamil,
    string GroupName,
    int DisplayOrder,
    string CanonicalUnit,
    int InchFraction,
    int CentimetreDecimals,
    bool IsRequired,
    decimal MinimumMillimetres,
    decimal MaximumMillimetres,
    decimal? WarnBelowMillimetres,
    decimal? WarnAboveMillimetres,
    string HelpText,
    string? DiagramKey,
    Guid? DiagramMediaId,
    string? DiagramAlt,
    TemplateRule? Rule,
    IReadOnlyList<TemplateChoiceOption>? Options)
{
    /// <summary>Reads the request into the domain's definition, or says which value could not be read.</summary>
    /// <returns>The definition, or the reason it was refused.</returns>
    /// <remarks>
    /// The enumerations arrive as strings because a wire format that carried their numbers would silently
    /// reinterpret every field the day somebody inserted a member. Parsing them here rather than binding them
    /// directly is also what turns "3 is not a CanonicalUnit" into a field-level problem detail rather than a
    /// model-binding failure with no field name on it.
    /// </remarks>
    public Result<TemplateFieldDefinition> ToDefinition()
    {
        if (!EnumText.TryRead<CanonicalUnit>(CanonicalUnit, out var unit))
        {
            return Result.Failure<TemplateFieldDefinition>(
                MeasurementApiErrors.NotAValidValue("canonicalUnit", CanonicalUnit));
        }

        var rule = ReadRule();

        if (rule.IsFailure)
        {
            return Result.Failure<TemplateFieldDefinition>(rule.Error);
        }

        return Result.Success(new TemplateFieldDefinition(
            Key,
            Label,
            LabelTamil,
            GroupName,
            DisplayOrder,
            unit,
            new FieldPrecision(InchFraction, CentimetreDecimals),
            unit == Domain.Measurements.CanonicalUnit.None
                ? ValidationBands.None
                : new ValidationBands(
                    MinimumMillimetres, MaximumMillimetres, WarnBelowMillimetres, WarnAboveMillimetres),
            IsRequired,
            HelpText,
            DiagramKey,
            DiagramMediaId,
            DiagramAlt,
            rule.Value,
            [
                .. (Options ?? []).Select(option => new ChoiceOption(
                    option.Code, option.Label, option.LabelTamil, option.DisplayOrder)),
            ]));
    }

    private Result<ConditionalRule?> ReadRule()
    {
        if (Rule is null)
        {
            return Result.Success<ConditionalRule?>(null);
        }

        if (!EnumText.TryRead<RuleEffect>(Rule.Effect, out var effect))
        {
            return Result.Failure<ConditionalRule?>(
                MeasurementApiErrors.NotAValidValue("rule.effect", Rule.Effect));
        }

        var clauses = new List<RuleClause>();

        foreach (var clause in Rule.AnyOf)
        {
            if (!EnumText.TryRead<RuleScope>(clause.Scope, out var scope))
            {
                return Result.Failure<ConditionalRule?>(
                    MeasurementApiErrors.NotAValidValue("rule.anyOf.scope", clause.Scope));
            }

            if (!EnumText.TryRead<RuleOperator>(clause.Operator, out var comparison))
            {
                return Result.Failure<ConditionalRule?>(
                    MeasurementApiErrors.NotAValidValue("rule.anyOf.operator", clause.Operator));
            }

            clauses.Add(new RuleClause(scope, clause.Name, comparison, clause.Values));
        }

        return Result.Success<ConditionalRule?>(new ConditionalRule(effect, clauses));
    }
}

/// <summary>A measurement template and its versions, as an administration screen reads it.</summary>
/// <param name="MeasurementTemplateId">The template.</param>
/// <param name="Code">The stable machine key.</param>
/// <param name="Name">What administrators read.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PublishedVersionId">The version measurements are captured against, or null.</param>
/// <param name="Versions">Every version, newest number first.</param>
public sealed record MeasurementTemplatePayload(
    Guid MeasurementTemplateId,
    string Code,
    string Name,
    string? Description,
    Guid? PublishedVersionId,
    IReadOnlyList<TemplateVersionPayload> Versions)
{
    /// <summary>Projects a template.</summary>
    /// <param name="template">The template.</param>
    /// <param name="withFields">Whether to carry each version's fields.</param>
    /// <returns>The payload.</returns>
    public static MeasurementTemplatePayload From(MeasurementTemplate template, bool withFields)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new MeasurementTemplatePayload(
            template.Id,
            template.Code,
            template.Name,
            template.Description,
            template.PublishedVersion?.Id,
            [
                .. template.Versions
                    .OrderByDescending(version => version.VersionNumber)
                    .Select(version => TemplateVersionPayload.From(version, withFields)),
            ]);
    }
}

/// <summary>One version of a template.</summary>
/// <param name="TemplateVersionId">The version.</param>
/// <param name="VersionNumber">Which version it is.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Notes">What changed and why.</param>
/// <param name="Status">Where it sits in its life.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in.</param>
/// <param name="IsApproved">Whether a second administrator has approved it.</param>
/// <param name="PublishedAt">When it became the version measurements are captured against.</param>
/// <param name="RetiredAt">When it stopped taking new captures.</param>
/// <param name="Fields">Its fields, in the order a tailor measures them.</param>
public sealed record TemplateVersionPayload(
    Guid TemplateVersionId,
    int VersionNumber,
    string Name,
    string? Notes,
    string Status,
    string DefaultDisplayUnit,
    bool IsApproved,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt,
    IReadOnlyList<TemplateFieldPayload>? Fields)
{
    /// <summary>Projects a version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="withFields">Whether to carry its fields.</param>
    /// <returns>The payload.</returns>
    public static TemplateVersionPayload From(TemplateVersion version, bool withFields)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new TemplateVersionPayload(
            version.Id,
            version.VersionNumber,
            version.Name,
            version.Notes,
            version.Status.ToString(),
            version.DefaultDisplayUnit.ToString(),
            version.ApprovedAt is not null,
            version.PublishedAt,
            version.RetiredAt,
            withFields
                ?
                [
                    .. version.Fields
                        .OrderBy(field => field.DisplayOrder)
                        .ThenBy(field => field.Key.Value, StringComparer.Ordinal)
                        .Select(TemplateFieldPayload.From),
                ]
                : null);
    }
}

/// <summary>One field of a version.</summary>
/// <param name="TemplateFieldId">The field.</param>
/// <param name="Key">What captured values are filed under.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="GroupName">The wizard step.</param>
/// <param name="DisplayOrder">The order the tape runs in.</param>
/// <param name="CanonicalUnit">What the stored value is.</param>
/// <param name="DisplayUnits">The units it may be entered in.</param>
/// <param name="InchFraction">The inch step as a denominator.</param>
/// <param name="CentimetreDecimals">Decimal places in centimetres.</param>
/// <param name="IsRequired">Whether it must be answered while shown.</param>
/// <param name="MinimumMillimetres">Below this the value is refused.</param>
/// <param name="MaximumMillimetres">Above this the value is refused.</param>
/// <param name="WarnBelowMillimetres">Below this the value needs an acknowledgement.</param>
/// <param name="WarnAboveMillimetres">Above this the value needs an acknowledgement.</param>
/// <param name="HelpText">Where the tape starts and ends.</param>
/// <remarks>
/// Everything an administration screen has to send back travels in the same shape it arrived in, so that a
/// field can be read, edited and replaced without the client having to reconstruct anything. That is why
/// <see cref="Rule"/> and <see cref="Options"/> are the very types the update accepts rather than a rendered
/// sentence and a list of codes: a screen that had to rebuild a rule from prose, or invent labels for the
/// options it just read, would silently drop configuration on every save.
/// </remarks>
/// <param name="DiagramReference">The sheet and callout, as <c>&lt;diagram_key&gt;#&lt;field_key&gt;</c>.</param>
/// <param name="DiagramAlt">The measuring path in words.</param>
/// <param name="DiagramKey">The bundled sheet as it must be sent back, or null.</param>
/// <param name="DiagramMediaId">The uploaded diagram as it must be sent back, or null.</param>
/// <param name="Rule">When the field is shown, as the update accepts it, or null when it always is.</param>
/// <param name="RuleDescription">The same rule written out as a sentence, for a screen to show.</param>
/// <param name="Options">The choices, as the update accepts them.</param>
public sealed record TemplateFieldPayload(
    Guid TemplateFieldId,
    string Key,
    string Label,
    string? LabelTamil,
    string GroupName,
    int DisplayOrder,
    string CanonicalUnit,
    IReadOnlyList<string> DisplayUnits,
    int InchFraction,
    int CentimetreDecimals,
    bool IsRequired,
    decimal MinimumMillimetres,
    decimal MaximumMillimetres,
    decimal? WarnBelowMillimetres,
    decimal? WarnAboveMillimetres,
    string HelpText,
    string? DiagramReference,
    string? DiagramAlt,
    string? DiagramKey,
    Guid? DiagramMediaId,
    TemplateRule? Rule,
    string? RuleDescription,
    IReadOnlyList<TemplateChoiceOption> Options)
{
    /// <summary>Projects a field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The payload.</returns>
    public static TemplateFieldPayload From(TemplateField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new TemplateFieldPayload(
            field.Id,
            field.Key.Value,
            field.Label,
            field.LabelTamil,
            field.GroupName,
            field.DisplayOrder,
            field.CanonicalUnit.ToString(),
            [.. field.DisplayUnits.Select(unit => unit.ToString())],
            field.Precision.InchFraction,
            field.Precision.CentimetreDecimals,
            field.IsRequired,
            field.Bands.MinimumMillimetres,
            field.Bands.MaximumMillimetres,
            field.Bands.WarnBelowMillimetres,
            field.Bands.WarnAboveMillimetres,
            field.HelpText,
            field.DiagramReference,
            field.DiagramAlt,
            field.DiagramKey,
            field.DiagramMediaId,
            Describe(field.Rule),
            TemplateFieldSnapshot.Describe(field.Rule),
            [
                .. field.Options
                    .OrderBy(option => option.DisplayOrder)
                    .ThenBy(option => option.Code, StringComparer.Ordinal)
                    .Select(option => new TemplateChoiceOption(
                        option.Code, option.Label, option.LabelTamil, option.DisplayOrder)),
            ]);
    }

    private static TemplateRule? Describe(ConditionalRule? rule)
        => rule is null
            ? null
            : new TemplateRule(
                rule.Effect.ToString(),
                [
                    .. rule.AnyOf.Select(clause => new TemplateRuleClause(
                        clause.Scope.ToString(),
                        clause.Name,
                        clause.Operator.ToString(),
                        [.. clause.Values])),
                ]);
}

/// <summary>What publish validation found.</summary>
/// <param name="TemplateVersionId">The version checked.</param>
/// <param name="IsReadyToPublish">Whether anything refuses publication.</param>
/// <param name="Findings">Everything wrong with it, empty when it is ready.</param>
public sealed record TemplateValidationPayload(
    Guid TemplateVersionId,
    bool IsReadyToPublish,
    IReadOnlyList<TemplateFindingPayload> Findings)
{
    /// <summary>Projects a report.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The payload.</returns>
    public static TemplateValidationPayload From(TemplateValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new TemplateValidationPayload(
            report.VersionId,
            !report.HasErrors,
            [
                .. report.Findings.Select(finding => new TemplateFindingPayload(
                    finding.Severity.ToString(), finding.Code, finding.Message, finding.Target)),
            ]);
    }
}

/// <summary>One thing publish validation noticed.</summary>
/// <param name="Severity">Whether it refuses publication: <c>Error</c> or <c>Warning</c>.</param>
/// <param name="Code">The stable code the screen branches on.</param>
/// <param name="Message">What is wrong, in words an administrator can act on.</param>
/// <param name="Target">The field or rule it is about.</param>
public sealed record TemplateFindingPayload(string Severity, string Code, string Message, string Target);
