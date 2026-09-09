using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Api.Payloads;

/// <summary>Starts a draft catalogue version.</summary>
/// <param name="Name">What this version is for.</param>
/// <param name="Notes">Longer notes for the reviewer.</param>
/// <param name="CloneFromVersionId">
/// The version to copy, or null to start empty. Cloning the published version is the ordinary way to
/// change a published catalogue, because a published version is immutable.
/// </param>
public sealed record CreateCatalogDraftRequest(
    string? Name,
    string? Notes,
    Guid? CloneFromVersionId);

/// <summary>Everything an administrator says about a category.</summary>
/// <param name="Code">The machine key. Fixed once its version is published.</param>
/// <param name="Name">The label staff and customers read.</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the category covers.</param>
/// <param name="ParentCategoryId">The parent category in this version, or null for top level.</param>
/// <param name="DisplayOrder">Where it sits among its siblings.</param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="FeatureFlagKey">The flag that can switch it off, or null.</param>
/// <param name="BranchIds">The branches offering it. Empty means nowhere, not everywhere.</param>
/// <param name="Reason">Why, on an edit. Recorded in the trail.</param>
public sealed record CategoryRequest(
    string? Code,
    string? Name,
    string? NameTamil,
    string? Description,
    Guid? ParentCategoryId,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    string? FeatureFlagKey,
    IReadOnlyList<Guid>? BranchIds,
    string? Reason)
{
    /// <summary>The domain value this request describes.</summary>
    /// <returns>The details, unvalidated: the aggregate is what validates them.</returns>
    public CategoryDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Name ?? string.Empty,
            NameTamil,
            Description,
            DisplayOrder,
            ActiveFrom,
            ActiveTo,
            FeatureFlagKey,
            BranchIds ?? []);
}

/// <summary>Everything an administrator says about a service type.</summary>
/// <param name="Code">The machine key, unique within its category.</param>
/// <param name="Name">The label staff and customers read.</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the service covers.</param>
/// <param name="DisplayOrder">Where it sits among its category's services.</param>
/// <param name="ExpectedDurationDays">The default due-date offset in working days.</param>
/// <param name="IntakeWarning">What the counter is warned about, or null.</param>
/// <param name="MeasurementTemplateId">Link 1 — a published measurement template version (#27).</param>
/// <param name="WorkflowDefinitionId">Link 2 — a workflow definition (#33).</param>
/// <param name="DesignOptionGroupIds">Link 3 — the design option groups, in order (#30).</param>
/// <param name="PriceListItemCode">Link 4 — an item code in a published price list (#41).</param>
/// <param name="QcChecklistTemplateId">Link 5 — a published QC checklist version (#34).</param>
/// <param name="AllowIncomplete">
/// Whether publication may proceed with links missing, which flags the service not orderable. It
/// exists so a category can be created and reviewed before its price list is ready, never as a way to
/// take orders against a half-configured service.
/// </param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="BranchIds">The branches offering it.</param>
/// <param name="Reason">Why, on an edit. Recorded in the trail.</param>
public sealed record ServiceTypeRequest(
    string? Code,
    string? Name,
    string? NameTamil,
    string? Description,
    int DisplayOrder,
    int ExpectedDurationDays,
    string? IntakeWarning,
    Guid? MeasurementTemplateId,
    Guid? WorkflowDefinitionId,
    IReadOnlyList<Guid>? DesignOptionGroupIds,
    string? PriceListItemCode,
    Guid? QcChecklistTemplateId,
    bool AllowIncomplete,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyList<Guid>? BranchIds,
    string? Reason)
{
    /// <summary>The domain value this request describes.</summary>
    /// <returns>The details, unvalidated: the aggregate is what validates them.</returns>
    public ServiceTypeDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Name ?? string.Empty,
            NameTamil,
            Description,
            DisplayOrder,
            ExpectedDurationDays,
            IntakeWarning,
            MeasurementTemplateId,
            WorkflowDefinitionId,
            DesignOptionGroupIds ?? [],
            PriceListItemCode,
            QcChecklistTemplateId,
            AllowIncomplete,
            ActiveFrom,
            ActiveTo,
            BranchIds ?? []);
}

/// <summary>A correction to what a published version shows, and nothing else.</summary>
/// <param name="Name">The corrected label.</param>
/// <param name="NameTamil">The corrected Tamil label.</param>
/// <param name="Description">The corrected description.</param>
/// <param name="DisplayOrder">The corrected position among siblings.</param>
/// <param name="Reason">Why. A change to a published version demands one.</param>
public sealed record CatalogPresentationRequest(
    string? Name,
    string? NameTamil,
    string? Description,
    int DisplayOrder,
    string? Reason)
{
    /// <summary>The domain value this request describes.</summary>
    /// <returns>The correction, unvalidated.</returns>
    public CatalogPresentation ToPresentation()
        => new(Name ?? string.Empty, NameTamil, Description, DisplayOrder);
}

/// <summary>A command whose only body is the reason the administrator gave.</summary>
/// <param name="Reason">Why.</param>
public sealed record CatalogReasonRequest(string? Reason);
