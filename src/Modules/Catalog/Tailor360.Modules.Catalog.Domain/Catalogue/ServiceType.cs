namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// One thing the shop will do to one kind of garment — stitch it, alter it, re-stitch it.
/// </summary>
/// <remarks>
/// <para>
/// A service type is always scoped to one category. <c>BLOUSE_PATTERN.STITCHING</c> and
/// <c>SALWAR.STITCHING</c> are different records with different links even though they share a code,
/// which is why the code is unique <em>within its category</em> rather than across the catalogue.
/// </para>
/// <para>
/// It is also the join point for the five configuration links (section 5 of
/// <c>docs/prd/category-hierarchy.md</c>), and it is the only place they meet. Everything downstream —
/// the capture wizard, the phase list, the design picker, the rate, the QC criteria — is reached from
/// here, which is what makes a new category a matter of configuration rather than a deployment.
/// </para>
/// </remarks>
public sealed class ServiceType
{
    private readonly List<ServiceTypeBranch> _branches = [];
    private readonly List<ServiceTypeDesignGroup> _designGroups = [];

    private ServiceType()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal ServiceType(
        Guid id,
        Guid key,
        Guid catalogVersionId,
        Guid organisationId,
        Guid categoryId,
        ServiceTypeDetails details)
    {
        Id = id;
        Key = key;
        CatalogVersionId = catalogVersionId;
        OrganisationId = organisationId;
        CategoryId = categoryId;

        Apply(details);
    }

    /// <summary>Identity of this row. New in every version, and what the API addresses.</summary>
    public Guid Id { get; private set; }

    /// <summary>Identity of the service type as a concept, carried unchanged from version to version.</summary>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The category that offers this service.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>The machine key, unique within its category.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The label staff and customers read.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The Tamil label, where one is confirmed.</summary>
    public string? NameTamil { get; private set; }

    /// <summary>What the service covers.</summary>
    public string? Description { get; private set; }

    /// <summary>Where the service sits among its category's services.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>The default due-date offset in working days.</summary>
    public int ExpectedDurationDays { get; private set; }

    /// <summary>What the counter is warned about before taking this service, or null.</summary>
    public string? IntakeWarning { get; private set; }

    /// <summary>Link 1 — a published measurement template version (#27).</summary>
    public Guid? MeasurementTemplateId { get; private set; }

    /// <summary>Link 2 — a workflow definition (#33).</summary>
    public Guid? WorkflowDefinitionId { get; private set; }

    /// <summary>Link 4 — an item code in a published price list (#41).</summary>
    public string? PriceListItemCode { get; private set; }

    /// <summary>Link 5 — a published QC checklist version (#34).</summary>
    public Guid? QcChecklistTemplateId { get; private set; }

    /// <summary>Whether publication may proceed with links missing.</summary>
    public bool AllowIncomplete { get; private set; }

    /// <summary>
    /// Whether the service was published with a link missing and is therefore not orderable.
    /// </summary>
    /// <remarks>
    /// Set at publication rather than computed on read. The links point at other modules' versions,
    /// and whether one was published <em>at the time this catalogue was</em> is a fact about that
    /// moment; recomputing it later would silently change what an already-published version says.
    /// </remarks>
    public bool NotOrderable { get; private set; }

    /// <summary>The first day the service is offered, or null for always.</summary>
    public DateOnly? ActiveFrom { get; private set; }

    /// <summary>The last day, or null for indefinitely.</summary>
    public DateOnly? ActiveTo { get; private set; }

    /// <summary>The branches that offer the service.</summary>
    public IReadOnlyCollection<ServiceTypeBranch> Branches => _branches;

    /// <summary>The branches that offer the service, as identifiers.</summary>
    public IEnumerable<Guid> BranchIds => _branches.Select(branch => branch.BranchId);

    /// <summary>Link 3 — the design option groups, in the order they are shown (#30).</summary>
    public IReadOnlyCollection<ServiceTypeDesignGroup> DesignGroups => _designGroups;

    /// <summary>The design option groups, as identifiers, in display order.</summary>
    public IEnumerable<Guid> DesignOptionGroupIds
        => _designGroups.OrderBy(group => group.DisplayOrder)
            .Select(group => group.DesignOptionGroupId);

    /// <summary>Whether every single-reference link is set.</summary>
    public bool HasEveryRequiredLink
        => MeasurementTemplateId is not null
           && WorkflowDefinitionId is not null
           && QcChecklistTemplateId is not null
           && !string.IsNullOrWhiteSpace(PriceListItemCode);

    /// <summary>Whether the service is within its active period on a given day.</summary>
    /// <param name="on">The day, in the branch's timezone.</param>
    /// <returns>True when the day falls inside the active period.</returns>
    public bool IsActiveOn(DateOnly on)
        => (ActiveFrom is not { } from || on >= from)
           && (ActiveTo is not { } to || on <= to);

    /// <summary>Replaces everything an administrator says about the service type.</summary>
    /// <param name="details">The validated details.</param>
    internal void Apply(ServiceTypeDetails details)
    {
        Code = details.Code;
        Name = details.Name;
        NameTamil = details.NameTamil;
        Description = details.Description;
        DisplayOrder = details.DisplayOrder;
        ExpectedDurationDays = details.ExpectedDurationDays;
        IntakeWarning = details.IntakeWarning;
        MeasurementTemplateId = details.MeasurementTemplateId;
        WorkflowDefinitionId = details.WorkflowDefinitionId;
        PriceListItemCode = details.PriceListItemCode;
        QcChecklistTemplateId = details.QcChecklistTemplateId;
        AllowIncomplete = details.AllowIncomplete;
        ActiveFrom = details.ActiveFrom;
        ActiveTo = details.ActiveTo;

        _branches.Clear();
        foreach (var branchId in details.BranchIds.Distinct())
        {
            _branches.Add(ServiceTypeBranch.For(Id, branchId));
        }

        _designGroups.Clear();
        var order = 0;
        foreach (var groupId in details.DesignOptionGroupIds.Distinct())
        {
            _designGroups.Add(ServiceTypeDesignGroup.For(Id, groupId, order++));
        }
    }

    /// <summary>Corrects the presentation fields of a published service type.</summary>
    /// <param name="presentation">The validated correction.</param>
    internal void ApplyPresentation(CatalogPresentation presentation)
    {
        Name = presentation.Name;
        NameTamil = presentation.NameTamil;
        Description = presentation.Description;
        DisplayOrder = presentation.DisplayOrder;
    }

    /// <summary>Records at publication whether the service may be ordered.</summary>
    /// <remarks>
    /// Called once, by <see cref="CatalogVersion.Publish"/>. A service missing a link is orderable
    /// only if it has every link; <see cref="AllowIncomplete"/> is what let it be published at all,
    /// not a licence to take orders against it.
    /// </remarks>
    internal void SettleOrderability() => NotOrderable = !HasEveryRequiredLink;

    /// <summary>Copies the service type into a new version.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="catalogVersionId">The version being built.</param>
    /// <param name="categoryId">The copy of this service's category in the new version.</param>
    /// <returns>The copy, carrying the same <see cref="Key"/>.</returns>
    internal ServiceType CopyInto(Guid id, Guid catalogVersionId, Guid categoryId)
        => new(
            id,
            Key,
            catalogVersionId,
            OrganisationId,
            categoryId,
            new ServiceTypeDetails(
                Code,
                Name,
                NameTamil,
                Description,
                DisplayOrder,
                ExpectedDurationDays,
                IntakeWarning,
                MeasurementTemplateId,
                WorkflowDefinitionId,
                [.. DesignOptionGroupIds],
                PriceListItemCode,
                QcChecklistTemplateId,
                AllowIncomplete,
                ActiveFrom,
                ActiveTo,
                [.. BranchIds]));
}
