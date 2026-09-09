using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Turns the module's own entities into the contract types a validator is given.
/// </summary>
/// <remarks>
/// One mapping, in one place, and it is what keeps the <c>Domain</c> project on this side of the
/// boundary: a validator lives in another module and may see only <c>Contracts</c> (ARCH-004). Written
/// as a projection rather than as an interface on the entities so that adding a field to the domain is
/// a compile error here — which is the prompt to decide whether validators should see it — rather than
/// a silent widening of what crosses.
/// </remarks>
public static class CatalogProjection
{
    /// <summary>Projects a draft and the organisation's code history into a publication candidate.</summary>
    /// <param name="version">The draft.</param>
    /// <param name="ledger">What codes have meant across every published version.</param>
    /// <returns>The candidate every registered validator is asked about.</returns>
    public static CatalogPublicationCandidate ToCandidate(
        CatalogVersion version,
        CatalogCodeLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(ledger);

        return new CatalogPublicationCandidate(
            version.Id,
            version.OrganisationId,
            version.VersionNumber,
            [.. version.Categories.Select(ToView)],
            [.. version.ServiceTypes.Select(ToView)],
            new CatalogCodeHistory(ledger.CategoryCodeByKey, ledger.CategoryKeyByCode),
            new CatalogCodeHistory(ledger.ServiceCodeByKey, ledger.ServiceKeyByCode));
    }

    /// <summary>Projects one service type into the snapshot other modules read it through.</summary>
    /// <param name="service">The service type.</param>
    /// <param name="category">Its category, in the same version.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogServiceSnapshot ToSnapshot(ServiceType service, Category category)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(category);

        return new CatalogServiceSnapshot(
            service.Id,
            service.Key,
            service.CatalogVersionId,
            category.Id,
            category.Code,
            category.Name,
            service.Code,
            service.Name,
            service.ExpectedDurationDays,
            service.IntakeWarning,
            service.MeasurementTemplateId,
            service.WorkflowDefinitionId,
            [.. service.DesignOptionGroupIds],
            service.PriceListItemCode,
            service.QcChecklistTemplateId,
            service.NotOrderable);
    }

    private static CatalogCategoryView ToView(Category category)
        => new(
            category.Id,
            category.Key,
            category.Code,
            category.Name,
            category.ParentId,
            category.ActiveFrom,
            category.ActiveTo,
            category.FeatureFlagKey,
            [.. category.BranchIds]);

    private static CatalogServiceTypeView ToView(ServiceType service)
        => new(
            service.Id,
            service.Key,
            service.CategoryId,
            service.Code,
            service.Name,
            service.MeasurementTemplateId,
            service.WorkflowDefinitionId,
            [.. service.DesignOptionGroupIds],
            service.PriceListItemCode,
            service.QcChecklistTemplateId,
            service.AllowIncomplete,
            service.ActiveFrom,
            service.ActiveTo,
            [.. service.BranchIds]);
}
