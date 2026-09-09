namespace Tailor360.Modules.Customers.Contracts.Measurements;

/// <summary>
/// What a measurement template asks for, and whether it is ready to be asked with.
/// </summary>
/// <remarks>
/// <para>
/// The only way another module reads a template. Two questions, and they are deliberately different.
/// <see cref="GetPublishedAsync"/> is asked before measurements are captured and answers from the
/// <em>currently published</em> version; <see cref="GetVersionAsync"/> is asked about a measurement that was
/// already taken and answers from whatever version it was pinned to, published or retired. Collapsing them would
/// mean either refusing to render a measurement whose template has since been superseded, or capturing against a
/// version that has been withdrawn.
/// </para>
/// <para>
/// <strong>A catalogue service type references the template, not one of its versions.</strong> That is what makes
/// a template change configuration rather than a release: publishing version 4 changes what the counter asks for
/// without touching a single catalogue row. The historical pin lives where it is actually needed — on the
/// measurement, which records the version it was captured under and renders through that forever
/// (<c>docs/prd/measurement-templates.md</c> sections 2 and 11).
/// </para>
/// </remarks>
public interface IMeasurementTemplateQuery
{
    /// <summary>The version measurements taken now would be captured against.</summary>
    /// <param name="templateId">The template a catalogue service type points at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or null when the template has none.</returns>
    Task<MeasurementTemplateSnapshot?> GetPublishedAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>One version by its own identity, whatever state it is in.</summary>
    /// <param name="versionId">The version a measurement was pinned to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or null when no template holds it.</returns>
    Task<MeasurementTemplateSnapshot?> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>Which of these templates have a published version, and which do not.</summary>
    /// <param name="templateIds">The templates to ask about.</param>
    /// <param name="organisationId">The organisation they must belong to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifiers that are ready to capture against.</returns>
    /// <remarks>
    /// Asked in a batch because the catalogue's publish validation asks about every service type at once, and one
    /// round trip per service type would make a large catalogue slow to publish for no reason.
    /// </remarks>
    Task<IReadOnlySet<Guid>> WithPublishedVersionAsync(
        IReadOnlyCollection<Guid> templateIds,
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>One template version, as another module reads it.</summary>
/// <param name="TemplateId">The template.</param>
/// <param name="VersionId">The version this answer came from.</param>
/// <param name="OrganisationId">The owning organisation.</param>
/// <param name="Code">The template's stable machine key, such as <c>MT_BLOUSE_PATTERN</c>.</param>
/// <param name="Name">What the template is called.</param>
/// <param name="VersionNumber">Which version this is.</param>
/// <param name="IsPublished">Whether measurements may still be captured against it.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in, as the template declares it.</param>
/// <param name="Fields">The fields, in the order a tailor measures them.</param>
public sealed record MeasurementTemplateSnapshot(
    Guid TemplateId,
    Guid VersionId,
    Guid OrganisationId,
    string Code,
    string Name,
    int VersionNumber,
    bool IsPublished,
    string DefaultDisplayUnit,
    IReadOnlyList<MeasurementFieldSnapshot> Fields);

/// <summary>One field, as the capture wizard and the printed sheet need it.</summary>
/// <remarks>
/// Carries the bands in millimetres, because the caller renders them in whatever unit the reader chose and a
/// pre-rounded bound would refuse a value that is inside it.
/// </remarks>
/// <param name="Key">What captured values are filed under.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="GroupName">The wizard step.</param>
/// <param name="DisplayOrder">The order the tape runs in.</param>
/// <param name="CanonicalUnit">What the stored value is: <c>Millimetre</c>, <c>Count</c> or <c>None</c>.</param>
/// <param name="DisplayUnits">The units it may be entered in.</param>
/// <param name="InchFraction">The inch step as a denominator, or zero.</param>
/// <param name="CentimetreDecimals">Decimal places in centimetres, or zero.</param>
/// <param name="IsRequired">Whether it must be answered while shown.</param>
/// <param name="MinimumMillimetres">Below this the value is refused.</param>
/// <param name="MaximumMillimetres">Above this the value is refused.</param>
/// <param name="WarnBelowMillimetres">Below this the value needs an acknowledgement.</param>
/// <param name="WarnAboveMillimetres">Above this the value needs an acknowledgement.</param>
/// <param name="HelpText">Where the tape starts and ends, and whether the value is a body or a finished measurement.</param>
/// <param name="DiagramReference">The sheet and callout, as <c>&lt;diagram_key&gt;#&lt;field_key&gt;</c>.</param>
/// <param name="DiagramAlt">The measuring path in words.</param>
/// <param name="Rule">When the field is shown, or null when it always is.</param>
/// <param name="Options">The choices, for a field that is chosen rather than measured.</param>
public sealed record MeasurementFieldSnapshot(
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
    MeasurementRuleSnapshot? Rule,
    IReadOnlyList<MeasurementOptionSnapshot> Options);

/// <summary>When a conditional field is shown, in a form the caller can actually evaluate.</summary>
/// <remarks>
/// <para>
/// The rule travels rather than a rendered sentence because the module that has to obey it is not the one
/// that stores it. A clause may name a design selection — <c>design.sleeve_style</c> — and design
/// selections only exist inside an order, so Orders is the only place the rule can be evaluated at all.
/// A prose description cannot be evaluated, and no other published contract carries the clauses.
/// </para>
/// <para>
/// The disjunction is inclusive: the field is decided the moment any one clause matches. A caller that
/// cannot read an operand at all treats the rule as undecided and shows the field, because hiding on
/// missing information drops a measurement the tailor needs.
/// </para>
/// </remarks>
/// <param name="Effect">What a match does: <c>ShownWhen</c> or <c>HiddenWhen</c>.</param>
/// <param name="AnyOf">The clauses, any one of which satisfies the rule.</param>
public sealed record MeasurementRuleSnapshot(string Effect, IReadOnlyList<MeasurementRuleClauseSnapshot> AnyOf);

/// <summary>One clause of a conditional rule.</summary>
/// <param name="Scope">Where the operand is read from: <c>Field</c> or <c>DesignSelection</c>.</param>
/// <param name="Name">The field key, or the design option-group code.</param>
/// <param name="Operator">How the values are compared: <c>IsAnyOf</c> or <c>Excludes</c>.</param>
/// <param name="Values">The values compared against.</param>
public sealed record MeasurementRuleClauseSnapshot(
    string Scope,
    string Name,
    string Operator,
    IReadOnlyList<string> Values);

/// <summary>One choice on a field that is chosen rather than measured.</summary>
/// <remarks>
/// The labels travel with the code because the caller renders the choice, and there is no other contract
/// from which the Tamil label or the intended order could be recovered.
/// </remarks>
/// <param name="Code">What the captured value is stored as.</param>
/// <param name="Label">What staff read.</param>
/// <param name="LabelTamil">The Tamil label, or null.</param>
/// <param name="DisplayOrder">The order the choices are offered in.</param>
public sealed record MeasurementOptionSnapshot(string Code, string Label, string? LabelTamil, int DisplayOrder);
