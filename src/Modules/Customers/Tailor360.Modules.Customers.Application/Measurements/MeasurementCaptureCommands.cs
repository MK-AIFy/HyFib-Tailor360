using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>One value as a caller sends it, before it is anything.</summary>
/// <remarks>
/// The unit travels with the number because the number alone is ambiguous: 36 is a chest in inches and a wrist in
/// centimetres. The canonical millimetres are computed here rather than trusted from the caller, so a client that
/// converts differently from the server cannot store a value the server would not have produced.
/// </remarks>
/// <param name="Key">The field key.</param>
/// <param name="Entered">The number as it was typed, in <paramref name="Unit"/>. Null for a chosen field.</param>
/// <param name="Unit">The unit it was typed in.</param>
/// <param name="Choice">The option's code, for a chosen field. Null for a measured one.</param>
/// <param name="Acknowledged">Whether an unusual value was explicitly accepted.</param>
public sealed record CapturedValue(
    string Key,
    decimal? Entered,
    DisplayUnit Unit,
    string? Choice,
    bool Acknowledged);

/// <summary>Start measuring a garment, or pick up the measuring already under way.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="MeasurementTemplateId">The template to measure against.</param>
/// <param name="ReuseFromVersionId">An earlier version to pre-fill from, or null to measure fresh.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch measuring.</param>
/// <param name="By">Who is measuring.</param>
public sealed record StartMeasurementDraftCommand(
    Guid CustomerId,
    Guid MeasurementTemplateId,
    Guid? ReuseFromVersionId,
    Guid OrganisationId,
    Guid BranchId,
    Guid? By);

/// <summary>Save one wizard step.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="GroupName">The step being saved.</param>
/// <param name="Values">Everything measured in that step.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the section was read against.</param>
/// <param name="By">Who saved it.</param>
public sealed record SaveMeasurementSectionCommand(
    Guid DraftId,
    string GroupName,
    IReadOnlyList<CapturedValue> Values,
    Guid OrganisationId,
    EntityTag? ExpectedVersion,
    Guid? By);

/// <summary>Turn a draft into the record of a measurement.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="Reason">Why, where one was given. Required on a correction.</param>
/// <param name="CorrectsVersionId">The version this corrects, or null.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the draft was read against.</param>
/// <param name="By">Who confirmed it.</param>
public sealed record ConfirmMeasurementsCommand(
    Guid DraftId,
    string? Reason,
    Guid? CorrectsVersionId,
    Guid OrganisationId,
    EntityTag? ExpectedVersion,
    Guid? By);

/// <summary>A draft and the tag an edit to it must be made against.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Tag">Its <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record CapturedDraft(MeasurementDraft Draft, EntityTag Tag);

/// <summary>A draft together with the template version it is pinned to.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Template">The template the draft answers.</param>
/// <param name="Version">The version the draft will be confirmed against.</param>
public sealed record CapturedTemplate(MeasurementDraft Draft, MeasurementTemplate Template, TemplateVersion Version);

/// <summary>A confirmed measurement together with the template version it renders through.</summary>
/// <param name="Version">The measurement.</param>
/// <param name="Template">The template it answers.</param>
/// <param name="TemplateVersion">The version it was captured under, and renders through forever.</param>
public sealed record MeasuredTemplate(
    MeasurementVersion Version,
    MeasurementTemplate Template,
    TemplateVersion TemplateVersion);

/// <summary>A measurement as a sheet carries it: the values, the version they render through, and who took them.</summary>
/// <param name="Version">The measurement.</param>
/// <param name="Template">The template it answers.</param>
/// <param name="TemplateVersion">The version it renders through.</param>
/// <param name="TakenByName">Who took it, as the directory names them, or null when it no longer knows.</param>
public sealed record MeasurementSheet(
    MeasurementVersion Version,
    MeasurementTemplate Template,
    TemplateVersion TemplateVersion,
    string? TakenByName);
