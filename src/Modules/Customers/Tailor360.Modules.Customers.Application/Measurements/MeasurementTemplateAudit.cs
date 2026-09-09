using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// What the audit trail records about a measurement template.
/// </summary>
/// <remarks>
/// <para>
/// A template holds no personal data — it is the shop's own list of what to measure — so the snapshots here carry
/// the field keys, labels and bands themselves rather than only their shape. That is the opposite of the customer
/// trail beside it, and the reason is the classification of the data rather than a difference of opinion about
/// audit: nothing in a template identifies anybody.
/// </para>
/// <para>
/// What a snapshot deliberately does not carry is every field's help text and diagram alt text. The trail exists
/// to answer "what changed about the rules", and a diff dominated by two paragraphs of prose per field answers it
/// worse than one that names the fields, their bands and their rules.
/// </para>
/// </remarks>
public static class MeasurementTemplateAudit
{
    /// <summary>The entity type these entries are filed under.</summary>
    public const string EntityType = "customers.measurement_template";

    /// <summary>Writes one entry and commits the trail.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="action">What happened.</param>
    /// <param name="templateId">The template it happened to.</param>
    /// <param name="summary">One sentence a reader can act on.</param>
    /// <param name="reason">Why, where the administrator gave one.</param>
    /// <param name="before">The version before, or null when it did not exist.</param>
    /// <param name="after">The version after, or null when it was removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid templateId,
        string summary,
        string? reason,
        TemplateVersionSnapshot? before,
        TemplateVersionSnapshot? after,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);

        await audit.WriteAsync(
            new AuditEntry(action, EntityType, templateId, summary, reason, before, after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>A template version as the trail records it.</summary>
/// <param name="VersionId">The version.</param>
/// <param name="VersionNumber">Which version it is.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Status">Where it sits in its life.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in.</param>
/// <param name="Fields">Its fields, in key order.</param>
public sealed record TemplateVersionSnapshot(
    Guid VersionId,
    int VersionNumber,
    string Name,
    string Status,
    string DefaultDisplayUnit,
    IReadOnlyList<TemplateFieldSnapshot> Fields)
{
    /// <summary>Takes a snapshot of a version.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The snapshot.</returns>
    public static TemplateVersionSnapshot Of(TemplateVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new TemplateVersionSnapshot(
            version.Id,
            version.VersionNumber,
            version.Name,
            version.Status.ToString(),
            version.DefaultDisplayUnit.ToString(),
            [
                .. version.Fields
                    .OrderBy(field => field.Key.Value, StringComparer.Ordinal)
                    .Select(TemplateFieldSnapshot.Of),
            ]);
    }
}

/// <summary>One field as the trail records it.</summary>
/// <param name="Key">What captured values are filed under.</param>
/// <param name="Label">What staff read.</param>
/// <param name="GroupName">The wizard step.</param>
/// <param name="DisplayOrder">The order the tape runs in.</param>
/// <param name="CanonicalUnit">What the stored value is.</param>
/// <param name="InchFraction">The inch step as a denominator.</param>
/// <param name="CentimetreDecimals">Decimal places in centimetres.</param>
/// <param name="IsRequired">Whether it must be answered while shown.</param>
/// <param name="MinimumMillimetres">Below this the value is refused.</param>
/// <param name="MaximumMillimetres">Above this the value is refused.</param>
/// <param name="WarnBelowMillimetres">Below this the value needs an acknowledgement.</param>
/// <param name="WarnAboveMillimetres">Above this the value needs an acknowledgement.</param>
/// <param name="Rule">The visibility rule, written out, or null.</param>
/// <param name="OptionCodes">The choices, for a choice field.</param>
public sealed record TemplateFieldSnapshot(
    string Key,
    string Label,
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
    string? Rule,
    IReadOnlyList<string> OptionCodes)
{
    /// <summary>Takes a snapshot of a field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The snapshot.</returns>
    public static TemplateFieldSnapshot Of(TemplateField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new TemplateFieldSnapshot(
            field.Key.Value,
            field.Label,
            field.GroupName,
            field.DisplayOrder,
            field.CanonicalUnit.ToString(),
            field.Precision.InchFraction,
            field.Precision.CentimetreDecimals,
            field.IsRequired,
            field.Bands.MinimumMillimetres,
            field.Bands.MaximumMillimetres,
            field.Bands.WarnBelowMillimetres,
            field.Bands.WarnAboveMillimetres,
            Describe(field.Rule),
            [.. field.Options.Select(option => option.Code)]);
    }

    /// <summary>Writes a rule out as the sentence it is, so a trail diff reads.</summary>
    /// <param name="rule">The rule, or null.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Describe(ConditionalRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        if (rule.AnyOf.Count == 0)
        {
            return rule.Effect == RuleEffect.HiddenWhen ? "always hidden" : "always shown";
        }

        var clauses = rule.AnyOf.Select(clause =>
        {
            var operand = clause.Scope == RuleScope.DesignSelection
                ? ConditionalRule.DesignScopePrefix + clause.Name
                : clause.Name;
            var comparison = clause.Operator == RuleOperator.Excludes ? "excludes" : "is";

            return $"{operand} {comparison} {string.Join(" or ", clause.Values)}";
        });

        var effect = rule.Effect == RuleEffect.HiddenWhen ? "hidden when" : "shown when";

        return $"{effect} {string.Join(" or ", clauses)}";
    }
}
