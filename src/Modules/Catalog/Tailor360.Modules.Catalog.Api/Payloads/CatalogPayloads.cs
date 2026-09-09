using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Api.Payloads;

/// <summary>One catalogue version, without its contents.</summary>
/// <param name="CatalogVersionId">The version, and what every route and pin refers to.</param>
/// <param name="VersionNumber">The number an administrator reads and says out loud.</param>
/// <param name="Name">What the version is for.</param>
/// <param name="Notes">Longer notes for the reviewer.</param>
/// <param name="Status">Draft, Published or Retired.</param>
/// <param name="ClonedFromVersionId">The version it was copied from, or null.</param>
/// <param name="CreatedAt">When the draft was started.</param>
/// <param name="PublishedAt">When it was published, or null.</param>
/// <param name="RetiredAt">When it was retired, or null.</param>
public sealed record CatalogVersionSummaryPayload(
    Guid CatalogVersionId,
    int VersionNumber,
    string Name,
    string? Notes,
    string Status,
    Guid? ClonedFromVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt)
{
    /// <summary>Projects a version.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The payload.</returns>
    public static CatalogVersionSummaryPayload From(CatalogVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new CatalogVersionSummaryPayload(
            version.Id,
            version.VersionNumber,
            version.Name,
            version.Notes,
            version.Status.ToString(),
            version.ClonedFromVersionId,
            version.CreatedAt,
            version.PublishedAt,
            version.RetiredAt);
    }
}

/// <summary>One catalogue version and everything in it.</summary>
/// <param name="Version">The version itself.</param>
/// <param name="Categories">Its categories, parents before children.</param>
/// <param name="ServiceTypes">Its service types.</param>
public sealed record CatalogVersionPayload(
    CatalogVersionSummaryPayload Version,
    IReadOnlyList<CategoryPayload> Categories,
    IReadOnlyList<ServiceTypePayload> ServiceTypes)
{
    /// <summary>Projects a version and its tree.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The payload.</returns>
    public static CatalogVersionPayload From(CatalogVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new CatalogVersionPayload(
            CatalogVersionSummaryPayload.From(version),
            [.. version.InParentFirstOrder().Select(
                category => CategoryPayload.From(category, version.IsGroupingNode(category.Id)))],
            [.. version.ServiceTypes
                .OrderBy(service => service.DisplayOrder)
                .ThenBy(service => service.Code, StringComparer.Ordinal)
                .Select(ServiceTypePayload.From)]);
    }
}

/// <summary>One category of a version.</summary>
/// <param name="CategoryId">The row, which is what the administration routes address.</param>
/// <param name="Code">The machine key, fixed once its version is published.</param>
/// <param name="Name">The label.</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the category covers.</param>
/// <param name="ParentCategoryId">The parent in the same version, or null at top level.</param>
/// <param name="DisplayOrder">Where it sits among its siblings.</param>
/// <param name="IsGroupingNode">
/// Whether it has sub-categories and is therefore never ordered against directly. Derived from the
/// hierarchy, so a screen never has to work it out from the list.
/// </param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="FeatureFlagKey">The flag that can switch it off, or null.</param>
/// <param name="BranchIds">The branches offering it. Empty means nowhere, not everywhere.</param>
public sealed record CategoryPayload(
    Guid CategoryId,
    string Code,
    string Name,
    string? NameTamil,
    string? Description,
    Guid? ParentCategoryId,
    int DisplayOrder,
    bool IsGroupingNode,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    string? FeatureFlagKey,
    IReadOnlyList<Guid> BranchIds)
{
    /// <summary>Projects a category.</summary>
    /// <param name="category">The category.</param>
    /// <param name="isGroupingNode">Whether anything names it as a parent.</param>
    /// <returns>The payload.</returns>
    public static CategoryPayload From(Category category, bool isGroupingNode)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new CategoryPayload(
            category.Id,
            category.Code,
            category.Name,
            category.NameTamil,
            category.Description,
            category.ParentId,
            category.DisplayOrder,
            isGroupingNode,
            category.ActiveFrom,
            category.ActiveTo,
            category.FeatureFlagKey,
            [.. category.BranchIds]);
    }
}

/// <summary>One service type of a version, with its five links.</summary>
/// <param name="ServiceTypeId">The row, which is what a garment job is pinned to.</param>
/// <param name="CategoryId">The category that offers it.</param>
/// <param name="Code">The machine key, unique within its category.</param>
/// <param name="Name">The label.</param>
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
/// <param name="AllowIncomplete">Whether publication may proceed with links missing.</param>
/// <param name="NotOrderable">Whether it was published with a link missing.</param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="BranchIds">The branches offering it.</param>
public sealed record ServiceTypePayload(
    Guid ServiceTypeId,
    Guid CategoryId,
    string Code,
    string Name,
    string? NameTamil,
    string? Description,
    int DisplayOrder,
    int ExpectedDurationDays,
    string? IntakeWarning,
    Guid? MeasurementTemplateId,
    Guid? WorkflowDefinitionId,
    IReadOnlyList<Guid> DesignOptionGroupIds,
    string? PriceListItemCode,
    Guid? QcChecklistTemplateId,
    bool AllowIncomplete,
    bool NotOrderable,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyList<Guid> BranchIds)
{
    /// <summary>Projects a service type.</summary>
    /// <param name="service">The service type.</param>
    /// <returns>The payload.</returns>
    public static ServiceTypePayload From(ServiceType service)
    {
        ArgumentNullException.ThrowIfNull(service);

        return new ServiceTypePayload(
            service.Id,
            service.CategoryId,
            service.Code,
            service.Name,
            service.NameTamil,
            service.Description,
            service.DisplayOrder,
            service.ExpectedDurationDays,
            service.IntakeWarning,
            service.MeasurementTemplateId,
            service.WorkflowDefinitionId,
            [.. service.DesignOptionGroupIds],
            service.PriceListItemCode,
            service.QcChecklistTemplateId,
            service.AllowIncomplete,
            service.NotOrderable,
            service.ActiveFrom,
            service.ActiveTo,
            [.. service.BranchIds]);
    }
}

/// <summary>One thing a validator found.</summary>
/// <param name="Validator">Which module's checks made it.</param>
/// <param name="Severity">Error or Warning. An error stops the command; a warning does not.</param>
/// <param name="Code">The stable dotted code a screen branches on.</param>
/// <param name="Message">What is wrong, in the shop's words.</param>
/// <param name="Target">
/// Which part of the draft it is about, as a path the screen can focus — for example
/// <c>serviceTypes[BLOUSE_AARI.STITCHING].measurementTemplateId</c>.
/// </param>
public sealed record CatalogFindingPayload(
    string Validator,
    string Severity,
    string Code,
    string Message,
    string? Target)
{
    /// <summary>Projects one attributed finding.</summary>
    /// <param name="finding">The finding and the validator that made it.</param>
    /// <returns>The payload.</returns>
    public static CatalogFindingPayload From(AttributedFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new CatalogFindingPayload(
            finding.Validator,
            finding.Finding.Severity.ToString(),
            finding.Finding.Code,
            finding.Finding.Message,
            finding.Finding.Target);
    }

    /// <summary>Projects a whole report.</summary>
    /// <param name="findings">The findings.</param>
    /// <returns>The payloads, in the order they were made.</returns>
    public static IReadOnlyList<CatalogFindingPayload> From(IEnumerable<AttributedFinding> findings)
        => [.. (findings ?? []).Select(From)];
}

/// <summary>What every validator said about one version, changing nothing.</summary>
/// <param name="CatalogVersionId">The version that was checked.</param>
/// <param name="Publishable">Whether it would publish as it stands.</param>
/// <param name="ErrorCount">How many findings stop publication.</param>
/// <param name="WarningCount">How many are worth saying and do not stop it.</param>
/// <param name="Findings">Everything found.</param>
public sealed record CatalogValidationReportPayload(
    Guid CatalogVersionId,
    bool Publishable,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CatalogFindingPayload> Findings)
{
    /// <summary>Projects a report.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The payload.</returns>
    public static CatalogValidationReportPayload From(CatalogValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var findings = CatalogFindingPayload.From(report.Findings);

        return new CatalogValidationReportPayload(
            report.VersionId,
            !report.HasErrors,
            findings.Count(finding => finding.Severity == nameof(CatalogFindingSeverity.Error)),
            findings.Count(finding => finding.Severity == nameof(CatalogFindingSeverity.Warning)),
            findings);
    }
}

/// <summary>What a publication did.</summary>
/// <param name="Version">The version that is now current.</param>
/// <param name="SupersededVersionId">The version it replaced, or null when it is the first.</param>
/// <param name="Findings">
/// What the validators said. Free of errors by construction, and the warnings are the sentence an
/// administrator needs to read: "published, and three services are not orderable".
/// </param>
public sealed record CatalogPublicationPayload(
    CatalogVersionSummaryPayload Version,
    Guid? SupersededVersionId,
    IReadOnlyList<CatalogFindingPayload> Findings)
{
    /// <summary>Projects a publication.</summary>
    /// <param name="publication">The publication.</param>
    /// <returns>The payload.</returns>
    public static CatalogPublicationPayload From(CatalogPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        return new CatalogPublicationPayload(
            CatalogVersionSummaryPayload.From(publication.Published.Version),
            publication.SupersededVersionId,
            CatalogFindingPayload.From(publication.Findings));
    }
}

/// <summary>What a branch may order today.</summary>
/// <param name="CatalogVersionId">The published version this answer came from, or null when none is.</param>
/// <param name="BranchId">The branch the answer is for.</param>
/// <param name="Services">
/// The orderable services, by category code then service code. A grouping node's services, a category
/// outside its active period, a branch that does not offer it, a category behind a flag that is off and
/// a service published with a link missing are all absent — intake neither lists nor accepts them.
/// </param>
public sealed record OrderableCatalogPayload(
    Guid? CatalogVersionId,
    Guid BranchId,
    IReadOnlyList<OrderableServicePayload> Services);

/// <summary>One service a branch may order, as its published version fixed it.</summary>
/// <param name="ServiceTypeId">What an order pins.</param>
/// <param name="CategoryId">Its category in the same version.</param>
/// <param name="CategoryCode">The category's machine key.</param>
/// <param name="CategoryName">The category's label.</param>
/// <param name="ServiceCode">The service's machine key.</param>
/// <param name="ServiceName">The service's label.</param>
/// <param name="QualifiedReference">The <c>CATEGORY.SERVICE</c> form price lists and exports use.</param>
/// <param name="ExpectedDurationDays">The default due-date offset in working days.</param>
/// <param name="IntakeWarning">What the counter is warned about, or null.</param>
/// <param name="MeasurementTemplateId">Link 1 — the field set the capture wizard uses (#27).</param>
/// <param name="WorkflowDefinitionId">Link 2 (#33).</param>
/// <param name="DesignOptionGroupIds">Link 3, in display order (#30).</param>
/// <param name="PriceListItemCode">Link 4 (#41).</param>
/// <param name="QcChecklistTemplateId">Link 5 (#34).</param>
public sealed record OrderableServicePayload(
    Guid ServiceTypeId,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    string ServiceCode,
    string ServiceName,
    string QualifiedReference,
    int ExpectedDurationDays,
    string? IntakeWarning,
    Guid? MeasurementTemplateId,
    Guid? WorkflowDefinitionId,
    IReadOnlyList<Guid> DesignOptionGroupIds,
    string? PriceListItemCode,
    Guid? QcChecklistTemplateId)
{
    /// <summary>Projects one orderable service.</summary>
    /// <param name="snapshot">The snapshot the contract answered with.</param>
    /// <returns>The payload.</returns>
    public static OrderableServicePayload From(CatalogServiceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new OrderableServicePayload(
            snapshot.ServiceTypeId,
            snapshot.CategoryId,
            snapshot.CategoryCode,
            snapshot.CategoryName,
            snapshot.ServiceCode,
            snapshot.ServiceName,
            snapshot.QualifiedReference,
            snapshot.ExpectedDurationDays,
            snapshot.IntakeWarning,
            snapshot.MeasurementTemplateId,
            snapshot.WorkflowDefinitionId,
            snapshot.DesignOptionGroupIds,
            snapshot.PriceListItemCode,
            snapshot.QcChecklistTemplateId);
    }
}
