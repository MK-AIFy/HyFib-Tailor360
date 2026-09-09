using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>Creates a template with no versions.</summary>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="Code">The stable machine key.</param>
/// <param name="Name">What administrators read.</param>
/// <param name="Description">What it is for.</param>
/// <param name="By">The administrator.</param>
public sealed record CreateMeasurementTemplateCommand(
    Guid OrganisationId,
    string Code,
    string Name,
    string? Description,
    Guid? By);

/// <summary>Starts a draft version, empty or copied from an existing one.</summary>
/// <param name="TemplateId">The template.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="Name">What to call the draft.</param>
/// <param name="Notes">What is changing and why.</param>
/// <param name="DefaultDisplayUnit">The unit the wizard opens in.</param>
/// <param name="CloneFromVersionId">The version to copy, or null for an empty draft.</param>
/// <param name="By">The administrator.</param>
public sealed record StartTemplateDraftCommand(
    Guid TemplateId,
    Guid OrganisationId,
    string Name,
    string? Notes,
    DisplayUnit DefaultDisplayUnit,
    Guid? CloneFromVersionId,
    Guid? By);

/// <summary>Adds or replaces a field of a draft.</summary>
/// <param name="TemplateId">The template.</param>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="FieldId">The field to replace, or null to add one.</param>
/// <param name="Definition">The field.</param>
/// <param name="ExpectedVersion">The concurrency token the caller read, or null.</param>
/// <param name="By">The administrator.</param>
public sealed record SaveTemplateFieldCommand(
    Guid TemplateId,
    Guid VersionId,
    Guid OrganisationId,
    Guid? FieldId,
    TemplateFieldDefinition Definition,
    EntityTag? ExpectedVersion,
    Guid? By);

/// <summary>Removes a field from a draft.</summary>
/// <param name="TemplateId">The template.</param>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="FieldId">The field.</param>
/// <param name="Reason">Why.</param>
/// <param name="By">The administrator.</param>
public sealed record RemoveTemplateFieldCommand(
    Guid TemplateId,
    Guid VersionId,
    Guid OrganisationId,
    Guid FieldId,
    string? Reason,
    Guid? By);

/// <summary>Moves a version through its lifecycle.</summary>
/// <param name="TemplateId">The template.</param>
/// <param name="VersionId">The version.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="Reason">Why. Publication and retirement demand one.</param>
/// <param name="ExpectedVersion">The concurrency token the caller read, or null.</param>
/// <param name="By">The administrator.</param>
public sealed record TemplateLifecycleCommand(
    Guid TemplateId,
    Guid VersionId,
    Guid OrganisationId,
    string Reason,
    EntityTag? ExpectedVersion,
    Guid? By);

/// <summary>A template and one of its versions, as an administration screen reads them.</summary>
/// <param name="Template">The template.</param>
/// <param name="Version">The version in question, or null when the template has none.</param>
/// <param name="Tag">The concurrency token, for <c>If-Match</c>.</param>
public sealed record AdministeredTemplate(
    MeasurementTemplate Template,
    TemplateVersion? Version,
    EntityTag Tag);

/// <summary>What publish validation found.</summary>
/// <param name="VersionId">The version checked.</param>
/// <param name="Findings">Everything wrong with it, empty when it is ready.</param>
public sealed record TemplateValidationReport(Guid VersionId, IReadOnlyList<TemplateFinding> Findings)
{
    /// <summary>Whether anything refuses publication.</summary>
    public bool HasErrors => TemplateValidation.HasErrors(Findings);
}
