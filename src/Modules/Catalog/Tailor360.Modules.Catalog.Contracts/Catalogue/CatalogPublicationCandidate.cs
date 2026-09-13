namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// A draft catalogue version as a validator sees it.
/// </summary>
/// <remarks>
/// <para>
/// A projection of the module's own entities rather than the entities themselves. A validator lives in
/// another module, so handing it a <c>CatalogVersion</c> would put the Catalog module's
/// <c>Domain</c> project on the far side of a boundary that only <c>Contracts</c> may cross
/// (ARCH-004). The cost is one mapping; the benefit is that a validator cannot reach past what it was
/// given.
/// </para>
/// <para>
/// It carries the history as well as the draft, because two of the rules are about the past: a code
/// may not change once the record that carries it has been published, and a code retired under one
/// meaning may never be re-used for another. Neither can be answered from a draft alone.
/// </para>
/// </remarks>
/// <param name="VersionId">The draft being published.</param>
/// <param name="OrganisationId">The organisation whose catalogue it is.</param>
/// <param name="VersionNumber">The version's number, for a message a person reads.</param>
/// <param name="Categories">Every category in the draft.</param>
/// <param name="ServiceTypes">Every service type in the draft.</param>
/// <param name="CategoryCodeHistory">What categories have been called in versions already published.</param>
/// <param name="ServiceTypeCodeHistory">
/// What service types have been called, keyed by the fully qualified
/// <c>CATEGORY_CODE.SERVICE_CODE</c> reference that price lists and exports use.
/// </param>
/// <param name="DesignGroups">Every design option group in the draft, with its options (#30).</param>
/// <param name="DesignRules">Every design rule in the draft.</param>
/// <param name="DesignGroupCodeHistory">
/// What design groups have been called, keyed by <c>CATEGORY_CODE.group_code</c>.
/// </param>
/// <param name="DesignOptionCodeHistory">
/// What design options have been called, keyed by <c>CATEGORY_CODE.group_code.OPTION_CODE</c> — the
/// fully qualified reference of section 2 of <c>docs/prd/design-options.md</c>.
/// </param>
public sealed record CatalogPublicationCandidate(
    Guid VersionId,
    Guid OrganisationId,
    int VersionNumber,
    IReadOnlyList<CatalogCategoryView> Categories,
    IReadOnlyList<CatalogServiceTypeView> ServiceTypes,
    CatalogCodeHistory CategoryCodeHistory,
    CatalogCodeHistory ServiceTypeCodeHistory,
    IReadOnlyList<CatalogDesignGroupView> DesignGroups,
    IReadOnlyList<CatalogDesignRuleView> DesignRules,
    CatalogCodeHistory DesignGroupCodeHistory,
    CatalogCodeHistory DesignOptionCodeHistory);

/// <summary>What a published version already fixed about a set of codes.</summary>
/// <remarks>
/// Two maps of the same facts, because the two rules read them in opposite directions: "has this
/// record's code changed?" looks up by key, and "has this code meant something else?" looks up by
/// code. Building both once is cheaper than every validator building the inverse of one.
/// </remarks>
/// <param name="CodeByKey">The code each concept was last published under.</param>
/// <param name="KeyByCode">The concept each code has been published against.</param>
public sealed record CatalogCodeHistory(
    IReadOnlyDictionary<Guid, string> CodeByKey,
    IReadOnlyDictionary<string, Guid> KeyByCode)
{
    /// <summary>A history holding nothing, for an organisation that has published no version yet.</summary>
    public static CatalogCodeHistory Empty { get; } = new(
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal));
}

/// <summary>One category of the draft.</summary>
/// <param name="Id">The row's identity in this version.</param>
/// <param name="Key">The category's identity as a concept, carried across versions.</param>
/// <param name="Code">The machine key.</param>
/// <param name="Name">The label.</param>
/// <param name="ParentId">The parent category in this version, or null for top level.</param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="FeatureFlagKey">The flag that can switch it off, or null.</param>
/// <param name="BranchIds">The branches that offer it.</param>
public sealed record CatalogCategoryView(
    Guid Id,
    Guid Key,
    string Code,
    string Name,
    Guid? ParentId,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    string? FeatureFlagKey,
    IReadOnlyCollection<Guid> BranchIds);

/// <summary>One service type of the draft, with its five links.</summary>
/// <param name="Id">The row's identity in this version.</param>
/// <param name="Key">The service type's identity as a concept.</param>
/// <param name="CategoryId">The category that offers it.</param>
/// <param name="Code">The machine key, unique within its category.</param>
/// <param name="Name">The label.</param>
/// <param name="MeasurementTemplateId">Link 1 — a published measurement template version (#27).</param>
/// <param name="WorkflowDefinitionId">Link 2 — a workflow definition (#33).</param>
/// <param name="DesignOptionGroupIds">Link 3 — the design option groups, in order (#30).</param>
/// <param name="PriceListItemCode">Link 4 — an item code in a published price list (#41).</param>
/// <param name="QcChecklistTemplateId">Link 5 — a published QC checklist version (#34).</param>
/// <param name="AllowIncomplete">Whether the administrator accepted publication with links missing.</param>
/// <param name="ActiveFrom">The first day it is offered, or null for always.</param>
/// <param name="ActiveTo">The last day, or null for indefinitely.</param>
/// <param name="BranchIds">The branches that offer it.</param>
public sealed record CatalogServiceTypeView(
    Guid Id,
    Guid Key,
    Guid CategoryId,
    string Code,
    string Name,
    Guid? MeasurementTemplateId,
    Guid? WorkflowDefinitionId,
    IReadOnlyList<Guid> DesignOptionGroupIds,
    string? PriceListItemCode,
    Guid? QcChecklistTemplateId,
    bool AllowIncomplete,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyCollection<Guid> BranchIds);

/// <summary>A published version being retired, and what would replace it.</summary>
/// <remarks>
/// The successor is what makes retirement safe or unsafe. A module holding work in progress reports
/// which service types that work uses; retirement is refused when the successor does not carry a
/// replacement for one of them, and permitted when it does — which is why the successor's service
/// keys travel with the question rather than being looked up by each participant.
/// </remarks>
/// <param name="VersionId">The version being retired.</param>
/// <param name="OrganisationId">The organisation whose catalogue it is.</param>
/// <param name="SuccessorVersionId">
/// The version that is published in its place, or null when none is. Retiring the only published
/// version leaves the shop with nothing orderable, so a participant holding any work at all refuses.
/// </param>
/// <param name="SuccessorServiceTypeKeys">
/// The concepts the successor still offers, as <see cref="CatalogServiceTypeView.Key"/> values. A
/// service whose key is here has a replacement, whatever its row identity is in the new version.
/// </param>
public sealed record CatalogRetirementCandidate(
    Guid VersionId,
    Guid OrganisationId,
    Guid? SuccessorVersionId,
    IReadOnlySet<Guid> SuccessorServiceTypeKeys);

/// <summary>One design option group as a validator sees it.</summary>
/// <param name="Id">The row.</param>
/// <param name="Key">The group as a concept across versions.</param>
/// <param name="CategoryId">The category whose garments it is chosen for.</param>
/// <param name="Code">The <c>lower_snake_case</c> code, unique within the category.</param>
/// <param name="Name">The label.</param>
/// <param name="SelectionMode"><c>SingleChoice</c> or <c>MultipleChoice</c>.</param>
/// <param name="Required">Whether a garment may be confirmed with nothing chosen here.</param>
/// <param name="DisplayOrder">Where it sits in the picker.</param>
/// <param name="ActiveFrom">The first day offered, or null.</param>
/// <param name="ActiveTo">The last day offered, or null.</param>
/// <param name="BranchIds">The branches that offer it.</param>
/// <param name="Options">Its options, retired ones included.</param>
public sealed record CatalogDesignGroupView(
    Guid Id,
    Guid Key,
    Guid CategoryId,
    string Code,
    string Name,
    string SelectionMode,
    bool Required,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyCollection<Guid> BranchIds,
    IReadOnlyList<CatalogDesignOptionView> Options);

/// <summary>One design option as a validator sees it.</summary>
/// <param name="Id">The row.</param>
/// <param name="Key">The option as a concept across versions.</param>
/// <param name="Code">The <c>UPPER_SNAKE_CASE</c> code, unique within its group.</param>
/// <param name="Name">The label.</param>
/// <param name="Active">Whether it is offered; false is retirement.</param>
/// <param name="HasIllustration">Whether a drawing is referenced.</param>
/// <param name="HasIllustrationAlt">Whether the shape is described in words.</param>
/// <param name="HasHelpText">Whether the choice is explained.</param>
/// <param name="PriceListItemCode">The price-list item it resolves to, or null.</param>
/// <param name="TimeImpactDays">Signed working days.</param>
/// <param name="DisplayOrder">Where it sits in its group.</param>
public sealed record CatalogDesignOptionView(
    Guid Id,
    Guid Key,
    string Code,
    string Name,
    bool Active,
    bool HasIllustration,
    bool HasIllustrationAlt,
    bool HasHelpText,
    string? PriceListItemCode,
    int TimeImpactDays,
    int DisplayOrder);

/// <summary>One design rule as a validator sees it.</summary>
/// <param name="Id">The row.</param>
/// <param name="Key">The rule as a concept across versions.</param>
/// <param name="CategoryId">The category whose groups it reads.</param>
/// <param name="Number">The number in <c>DR-nn</c>.</param>
/// <param name="Identifier"><c>DR-nn</c>, as findings name it.</param>
/// <param name="Type"><c>Requires</c>, <c>Excludes</c>, <c>RequiresAttachment</c> or <c>Note</c>.</param>
/// <param name="Antecedent">What has to hold for it to fire.</param>
/// <param name="Consequent">The option set a requires or excludes rule names, or null.</param>
/// <param name="Note">The instruction a note attaches.</param>
public sealed record CatalogDesignRuleView(
    Guid Id,
    Guid Key,
    Guid CategoryId,
    int Number,
    string Identifier,
    string Type,
    CatalogDesignOperandView Antecedent,
    CatalogDesignOperandView? Consequent,
    string? Note);

/// <summary>One side of a rule.</summary>
/// <param name="GroupCode">The group read, or null for <c>Always</c>.</param>
/// <param name="Form">The operand form, by the name section 4 of the design options document gives it.</param>
/// <param name="OptionCodes">The option codes named.</param>
public sealed record CatalogDesignOperandView(
    string? GroupCode,
    string Form,
    IReadOnlyList<string> OptionCodes);
