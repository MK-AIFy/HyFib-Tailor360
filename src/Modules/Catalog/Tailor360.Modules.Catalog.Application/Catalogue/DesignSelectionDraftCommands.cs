using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>Starts choosing a design for a service type of the currently published version.</summary>
/// <param name="ServiceTypeId">The service type, as the current published version names it.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch choosing.</param>
/// <param name="By">Who started it.</param>
public sealed record StartDesignSelectionDraftCommand(
    Guid ServiceTypeId,
    Guid OrganisationId,
    Guid BranchId,
    Guid? By);

/// <summary>Replaces the whole selection set of a draft.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="Selections">What was chosen, one entry per group.</param>
/// <param name="Instructions">Free-text craft instructions, or null.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the draft was read against.</param>
/// <param name="By">Who saved it.</param>
public sealed record SaveDesignSelectionsCommand(
    Guid DraftId,
    IReadOnlyList<DesignSelectionInput> Selections,
    string? Instructions,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Re-pins a draft to the currently published version and re-validates it.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the draft was read against.</param>
/// <param name="HasReferenceImage">Whether the garment holds a reference image, for the re-validation.</param>
/// <param name="By">Who asked for the migration.</param>
public sealed record MigrateDesignSelectionDraftCommand(
    Guid DraftId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    bool HasReferenceImage,
    Guid? By);

/// <summary>A draft and the tag an edit to it must be made against.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Tag">Its <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record CapturedDesignDraft(DesignSelectionDraft Draft, EntityTag Tag);

/// <summary>A draft as a read answers it, with the migration prompt a republish may have left standing.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Tag">Its <c>xmin</c>.</param>
/// <param name="MigrationPlan">
/// What migrating to the currently published version would do, or null when the draft is already pinned
/// to it, when nothing is published at all, or when the migration was already applied by this read's own
/// caller (never true here — <c>ReadAsync</c> only plans, it never applies).
/// </param>
public sealed record DesignDraftRead(
    DesignSelectionDraft Draft,
    EntityTag Tag,
    DesignSelectionMigrationPlan? MigrationPlan);

/// <summary>What a migration command did, and the draft's fresh evaluation afterwards.</summary>
/// <param name="Draft">The draft, re-pinned.</param>
/// <param name="Tag">Its <c>xmin</c> after the migration.</param>
/// <param name="AppliedChanges">Every change the migration prompt named, now applied.</param>
/// <param name="Evaluation">The rules, asked again against the version just migrated to.</param>
public sealed record DesignMigrationOutcome(
    DesignSelectionDraft Draft,
    EntityTag Tag,
    IReadOnlyList<DesignMigrationChange> AppliedChanges,
    DesignEvaluation Evaluation);

/// <summary>What the picker reads for one service type: its groups, their options, and the rules between them.</summary>
/// <param name="CatalogVersionId">The published version this answer came from.</param>
/// <param name="CategoryId">The category the service type belongs to.</param>
/// <param name="ServiceTypeId">The service type.</param>
/// <param name="Groups">The service type's groups, in display order, options filtered to what is offerable.</param>
/// <param name="Rules">The rules a garment of this service type could meet.</param>
public sealed record DesignPickerRead(
    Guid CatalogVersionId,
    Guid CategoryId,
    Guid ServiceTypeId,
    IReadOnlyList<DesignOptionGroup> Groups,
    IReadOnlyList<DesignRule> Rules);
