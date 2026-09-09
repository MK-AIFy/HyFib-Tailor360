using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// Everything an administrator says about a service type, validated as one value.
/// </summary>
/// <remarks>
/// <para>
/// Five of these fields are the links that join the taxonomy to the rest of the system
/// (<c>docs/prd/category-hierarchy.md</c> section 5): a measurement template, a workflow definition,
/// a set of design option groups, a price-list item and a QC checklist. All five are nullable here and
/// none is a foreign key, because each names a published version of configuration owned by another
/// module. A key across the boundary would be an ARCH-005 violation; what checks them instead is the
/// registered validator each owning module contributes at publish.
/// </para>
/// <para>
/// A service type published with a link missing is possible only under
/// <see cref="AllowIncomplete"/>, and it is then flagged not orderable. That exists so a category can
/// be created and reviewed before its price list is ready — never as a way to take orders against a
/// half-configured service.
/// </para>
/// </remarks>
/// <param name="Code">The machine key, unique within its category.</param>
/// <param name="Name">The label staff and customers read (en-IN).</param>
/// <param name="NameTamil">The Tamil label, where one is confirmed.</param>
/// <param name="Description">What the service covers.</param>
/// <param name="DisplayOrder">Where the service sits among its category's services.</param>
/// <param name="ExpectedDurationDays">
/// The default due-date offset in working days against the branch calendar. Configuration, and never
/// a promise to the customer.
/// </param>
/// <param name="IntakeWarning">
/// What the counter is warned about before taking this service, or null. The Aari alteration warning
/// is the seeded example, and it is configuration on the service type rather than code.
/// </param>
/// <param name="MeasurementTemplateId">Link 1 — a published measurement template version (#27).</param>
/// <param name="WorkflowDefinitionId">Link 2 — a workflow definition (#33).</param>
/// <param name="DesignOptionGroupIds">Link 3 — the design option groups, in order (#30).</param>
/// <param name="PriceListItemCode">Link 4 — an item code in a published price list (#41).</param>
/// <param name="QcChecklistTemplateId">Link 5 — a published QC checklist version (#34).</param>
/// <param name="AllowIncomplete">Whether publication may proceed with links missing.</param>
/// <param name="ActiveFrom">The first day the service is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="BranchIds">The branches that offer it. Empty means nowhere, not everywhere.</param>
public sealed record ServiceTypeDetails(
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
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyCollection<Guid> BranchIds)
{
    /// <summary>The longest label the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest description the column holds.</summary>
    public const int MaximumDescriptionLength = 1000;

    /// <summary>The longest intake warning the column holds.</summary>
    public const int MaximumIntakeWarningLength = 500;

    /// <summary>The longest price-list item code the column holds.</summary>
    public const int MaximumPriceListItemCodeLength = 60;

    /// <summary>
    /// The longest expected duration, in working days.
    /// </summary>
    /// <remarks>
    /// A year of working days. The bound is not a business rule anybody asked for — it is a guard
    /// against a typed extra digit becoming a due date in 2049, which nothing downstream would query.
    /// </remarks>
    public const int MaximumExpectedDurationDays = 250;

    /// <summary>The three links that are a single reference, and whether each is set.</summary>
    /// <remarks>
    /// Design option groups are deliberately absent: section 5 gives them a cardinality of "zero or
    /// more", so an empty set is a complete answer rather than a missing link. Reading emptiness as
    /// incompleteness would flag every service that simply has no shape choices as not orderable.
    /// </remarks>
    public bool HasEveryRequiredLink
        => MeasurementTemplateId is not null
           && WorkflowDefinitionId is not null
           && QcChecklistTemplateId is not null
           && !string.IsNullOrWhiteSpace(PriceListItemCode);

    /// <summary>Checks everything that can be checked about a service type on its own.</summary>
    /// <returns>Success, or the first failure.</returns>
    public Result Validate()
    {
        if (!CatalogCode.IsWellFormed(Code))
        {
            return Result.Failure(CatalogErrors.CodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return Result.Failure(CatalogErrors.Required("name"));
        }

        if (Name.Length > MaximumNameLength)
        {
            return Result.Failure(CatalogErrors.TooLong("name", MaximumNameLength));
        }

        if (NameTamil is { Length: > MaximumNameLength })
        {
            return Result.Failure(CatalogErrors.TooLong("nameTamil", MaximumNameLength));
        }

        if (Description is { Length: > MaximumDescriptionLength })
        {
            return Result.Failure(CatalogErrors.TooLong("description", MaximumDescriptionLength));
        }

        if (IntakeWarning is { Length: > MaximumIntakeWarningLength })
        {
            return Result.Failure(
                CatalogErrors.TooLong("intakeWarning", MaximumIntakeWarningLength));
        }

        if (PriceListItemCode is { Length: > MaximumPriceListItemCodeLength })
        {
            return Result.Failure(
                CatalogErrors.TooLong("priceListItemCode", MaximumPriceListItemCodeLength));
        }

        if (DisplayOrder < 0)
        {
            return Result.Failure(CatalogErrors.DisplayOrderNegative("displayOrder"));
        }

        if (ExpectedDurationDays is < 1 or > MaximumExpectedDurationDays)
        {
            return Result.Failure(CatalogErrors.DurationOutOfRange(
                "expectedDurationDays", MaximumExpectedDurationDays));
        }

        return ActiveFrom is { } from && ActiveTo is { } to && to < from
            ? Result.Failure(CatalogErrors.ActiveDatesReversed("activeTo"))
            : Result.Success();
    }
}
