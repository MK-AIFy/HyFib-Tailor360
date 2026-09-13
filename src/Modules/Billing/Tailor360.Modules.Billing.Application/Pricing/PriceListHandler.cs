using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// The commands an administrator runs against price lists and their versions (#41, #146).
/// </summary>
/// <remarks>
/// Every command that changes a version compares the caller's <c>If-Match</c> against the version's
/// row version before touching it, saves, and then records an audit entry. Publication runs the
/// checks, retires the version it supersedes in the same transaction, and lets the database settle
/// two administrators publishing at once.
/// </remarks>
/// <remarks>
/// The publication checks ask Catalog what the published catalogue names and then write here, and a
/// catalogue publication committing in that window was validated against the version this one retires.
/// The window is the same one Customers documents for a template retirement; both sides are step-up
/// protected administrative acts, and the catalogue's reconciliation is where a stranded reference is
/// recorded (decision OD-18).
/// </remarks>
public sealed class PriceListHandler(
    IPriceListStore store,
    ITaxConfigurationStore taxConfigurations,
    ICatalogAvailabilityQuery catalogue,
    IBranchDirectory branches,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A price list was created.</summary>
    public const string ListCreatedAction = "billing.price_list.created";

    /// <summary>A price list was renamed.</summary>
    public const string ListRenamedAction = "billing.price_list.renamed";

    /// <summary>A draft was started empty.</summary>
    public const string DraftedAction = "billing.price_list_version.drafted";

    /// <summary>A draft was started as a copy.</summary>
    public const string ClonedAction = "billing.price_list_version.cloned";

    /// <summary>A draft's own details changed.</summary>
    public const string ChangedAction = "billing.price_list_version.changed";

    /// <summary>A draft was published.</summary>
    public const string PublishedAction = "billing.price_list_version.published";

    /// <summary>An item was added to a draft.</summary>
    public const string ItemAddedAction = "billing.price_list_item.added";

    /// <summary>An item of a draft changed.</summary>
    public const string ItemChangedAction = "billing.price_list_item.changed";

    /// <summary>An item was removed from a draft.</summary>
    public const string ItemRemovedAction = "billing.price_list_item.removed";

    /// <summary>A discount rule was added to a draft.</summary>
    public const string RuleAddedAction = "billing.discount_rule.added";

    /// <summary>A discount rule of a draft changed.</summary>
    public const string RuleChangedAction = "billing.discount_rule.changed";

    /// <summary>A discount rule was removed from a draft.</summary>
    public const string RuleRemovedAction = "billing.discount_rule.removed";

    /// <summary>Creates a price list.</summary>
    public async Task<Result<AdministeredPriceList>> CreateListAsync(CreatePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = PriceList.Create(ids.NewId(), command.OrganisationId, command.Code, command.Name, clock.UtcNow, command.By);
        if (created.IsFailure)
        {
            return Result.Failure<AdministeredPriceList>(created.Error);
        }

        if (await store.FindListByCodeAsync(command.Code, command.OrganisationId, cancellationToken) is not null)
        {
            return Result.Failure<AdministeredPriceList>(BillingErrors.CodeNotUnique("code"));
        }

        store.AddList(created.Value);
        var saved = await store.SaveDraftAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceList>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ListCreatedAction, BillingAudit.PriceListEntity, created.Value.Id,
            $"Price list {created.Value.Code} created.", command.Reason, null, PriceListSnapshot.Of(created.Value), cancellationToken);

        return Result.Success(new AdministeredPriceList(created.Value, store.EntityTagOf(created.Value)));
    }

    /// <summary>Renames a price list.</summary>
    public async Task<Result<AdministeredPriceList>> RenameListAsync(RenamePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await store.FindListAsync(command.PriceListId, command.OrganisationId, cancellationToken);
        if (list is null)
        {
            return Result.Failure<AdministeredPriceList>(BillingErrors.PriceListNotFound);
        }

        if (!command.ExpectedVersion.Matches(store.EntityTagOf(list)))
        {
            return Result.Failure<AdministeredPriceList>(BillingErrors.PriceListChanged);
        }

        var before = PriceListSnapshot.Of(list);
        var renamed = list.Rename(command.Name, clock.UtcNow, command.By);
        if (renamed.IsFailure)
        {
            return Result.Failure<AdministeredPriceList>(renamed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceList>(saved.Error.Code == BillingErrors.VersionChanged.Code ? BillingErrors.PriceListChanged : saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ListRenamedAction, BillingAudit.PriceListEntity, list.Id,
            $"Price list {list.Code} renamed.", command.Reason, before, PriceListSnapshot.Of(list), cancellationToken);

        return Result.Success(new AdministeredPriceList(list, store.EntityTagOf(list)));
    }

    /// <summary>Starts a draft version, empty or as a copy of an existing version of the same list.</summary>
    public async Task<Result<AdministeredPriceListVersion>> CreateDraftAsync(CreatePriceListDraftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var list = await store.FindListAsync(command.PriceListId, command.OrganisationId, cancellationToken);
        if (list is null)
        {
            return Result.Failure<AdministeredPriceListVersion>(BillingErrors.PriceListNotFound);
        }

        var known = await BranchesKnownAsync(command.Details, command.OrganisationId, cancellationToken);
        if (known.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(known.Error);
        }

        var number = await store.NextVersionNumberAsync(list.Id, cancellationToken);
        var now = clock.UtcNow;
        Result<PriceListVersion> created;
        string action;
        string summary;

        if (command.CloneFromVersionId is { } sourceId)
        {
            var source = await store.FindVersionAsync(sourceId, command.OrganisationId, cancellationToken);
            if (source is null || source.PriceListId != list.Id)
            {
                return Result.Failure<AdministeredPriceListVersion>(BillingErrors.VersionNotFound);
            }

            created = source.CloneAsDraft(ids, number, command.Details, now, command.By);
            action = ClonedAction;
            summary = $"Price list {list.Code} version {number} started as a copy of version {source.VersionNumber}.";
        }
        else
        {
            created = PriceListVersion.CreateDraft(ids.NewId(), list.Id, command.OrganisationId, number, command.Details, now, command.By);
            action = DraftedAction;
            summary = $"Price list {list.Code} version {number} started empty.";
        }

        if (created.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(created.Error);
        }

        store.AddVersion(created.Value);
        var saved = await store.SaveDraftAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, action, BillingAudit.PriceListVersionEntity, created.Value.Id, summary, command.Reason, null,
            PriceListVersionSnapshot.Of(created.Value), cancellationToken);

        return Result.Success(Administered(created.Value));
    }

    /// <summary>Changes a draft's own details.</summary>
    public async Task<Result<AdministeredPriceListVersion>> DescribeAsync(DescribePriceListVersionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(found.Error);
        }

        var known = await BranchesKnownAsync(command.Details, command.OrganisationId, cancellationToken);
        if (known.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(known.Error);
        }

        var version = found.Value;
        var before = PriceListVersionSnapshot.Of(version);
        var described = version.Describe(command.Details, clock.UtcNow, command.By);
        if (described.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(described.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceListVersion>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ChangedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Price list version {version.VersionNumber} described; effective from {version.EffectiveFrom:yyyy-MM-dd}.",
            command.Reason, before, PriceListVersionSnapshot.Of(version), cancellationToken);

        return Result.Success(Administered(version));
    }

    /// <summary>Adds an item to a draft.</summary>
    public async Task<Result<AdministeredPriceListItem>> AddItemAsync(AddPriceListItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(found.Error);
        }

        var version = found.Value;
        var added = version.AddItem(ids.NewId(), ids.NewId(), command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(added.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ItemAddedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Item {added.Value.Code} added to price list version {version.VersionNumber}.",
            command.Reason, null, PriceListItemSnapshot.Of(added.Value), cancellationToken);

        return Result.Success(new AdministeredPriceListItem(added.Value, store.EntityTagOf(version)));
    }

    /// <summary>Replaces what a draft says about an item.</summary>
    public async Task<Result<AdministeredPriceListItem>> EditItemAsync(EditPriceListItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(found.Error);
        }

        var version = found.Value;
        var before = version.FindItem(command.ItemId) is { } existing ? PriceListItemSnapshot.Of(existing) : null;
        var edited = version.EditItem(command.ItemId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(edited.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPriceListItem>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ItemChangedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Item {edited.Value.Code} of price list version {version.VersionNumber} changed.",
            command.Reason, before, PriceListItemSnapshot.Of(edited.Value), cancellationToken);

        return Result.Success(new AdministeredPriceListItem(edited.Value, store.EntityTagOf(version)));
    }

    /// <summary>Removes an item from a draft.</summary>
    public async Task<Result<EntityTag>> RemoveItemAsync(RemovePriceListItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<EntityTag>(found.Error);
        }

        var version = found.Value;
        var before = version.FindItem(command.ItemId) is { } existing ? PriceListItemSnapshot.Of(existing) : null;
        var removed = version.RemoveItem(command.ItemId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return Result.Failure<EntityTag>(removed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<EntityTag>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ItemRemovedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Item {before?.Code} removed from price list version {version.VersionNumber}.",
            command.Reason, before, null, cancellationToken);

        return Result.Success(store.EntityTagOf(version));
    }

    /// <summary>Adds a discount rule to a draft.</summary>
    public async Task<Result<AdministeredDiscountRule>> AddDiscountRuleAsync(AddDiscountRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(found.Error);
        }

        var version = found.Value;
        var added = version.AddDiscountRule(ids.NewId(), ids.NewId(), command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(added.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, RuleAddedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Discount rule {added.Value.Code} added to price list version {version.VersionNumber}.",
            command.Reason, null, DiscountRuleSnapshot.Of(added.Value), cancellationToken);

        return Result.Success(new AdministeredDiscountRule(added.Value, store.EntityTagOf(version)));
    }

    /// <summary>Replaces what a draft says about a discount rule.</summary>
    public async Task<Result<AdministeredDiscountRule>> EditDiscountRuleAsync(EditDiscountRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(found.Error);
        }

        var version = found.Value;
        var before = version.FindDiscountRule(command.RuleId) is { } existing ? DiscountRuleSnapshot.Of(existing) : null;
        var edited = version.EditDiscountRule(command.RuleId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(edited.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredDiscountRule>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, RuleChangedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Discount rule {edited.Value.Code} of price list version {version.VersionNumber} changed.",
            command.Reason, before, DiscountRuleSnapshot.Of(edited.Value), cancellationToken);

        return Result.Success(new AdministeredDiscountRule(edited.Value, store.EntityTagOf(version)));
    }

    /// <summary>Removes a discount rule from a draft.</summary>
    public async Task<Result<EntityTag>> RemoveDiscountRuleAsync(RemoveDiscountRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<EntityTag>(found.Error);
        }

        var version = found.Value;
        var before = version.FindDiscountRule(command.RuleId) is { } existing ? DiscountRuleSnapshot.Of(existing) : null;
        var removed = version.RemoveDiscountRule(command.RuleId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return Result.Failure<EntityTag>(removed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<EntityTag>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, RuleRemovedAction, BillingAudit.PriceListVersionEntity, version.Id,
            $"Discount rule {before?.Code} removed from price list version {version.VersionNumber}.",
            command.Reason, before, null, cancellationToken);

        return Result.Success(store.EntityTagOf(version));
    }

    /// <summary>Runs the publication checks against a version without publishing it.</summary>
    public async Task<Result<BillingValidationReport>> ValidateAsync(Guid versionId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        var version = await store.FindVersionAsync(versionId, organisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<BillingValidationReport>(BillingErrors.VersionNotFound);
        }

        return Result.Success(await CheckAsync(version, cancellationToken));
    }

    /// <summary>Publishes a draft, retiring the list's published version in the same transaction.</summary>
    public async Task<Result<PriceListPublication>> PublishAsync(PublishPriceListVersionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<PriceListPublication>(found.Error);
        }

        var draft = found.Value;
        if (!draft.IsEditable)
        {
            return Result.Failure<PriceListPublication>(BillingErrors.VersionNotPublishable);
        }

        var report = await CheckAsync(draft, cancellationToken);
        if (report.HasErrors)
        {
            return Result.Failure<PriceListPublication>(BillingErrors.PublishValidationFailed);
        }

        var outgoing = await store.FindPublishedAsync(draft.PriceListId, command.OrganisationId, cancellationToken);
        var now = clock.UtcNow;
        if (outgoing is not null)
        {
            // The retirement's own reason is the fact; the administrator's reason rides on the audit entry
            // below, where its length was already checked, rather than being appended here where it could
            // push the sentence past what the column holds.
            var superseded = outgoing.Retire(now, command.By, $"Superseded by price list version {draft.VersionNumber}.");
            if (superseded.IsFailure)
            {
                return Result.Failure<PriceListPublication>(superseded.Error);
            }
        }

        var published = draft.Publish(now, command.By, command.Reason);
        if (published.IsFailure)
        {
            return Result.Failure<PriceListPublication>(published.Error);
        }

        var saved = await store.SavePublicationAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<PriceListPublication>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, PublishedAction, BillingAudit.PriceListVersionEntity, draft.Id,
            $"Price list version {draft.VersionNumber} published with {draft.Items.Count} item(s) and "
            + $"{draft.DiscountRules.Count} discount rule(s) for {draft.Branches.Count} branch(es), effective from "
            + $"{draft.EffectiveFrom:yyyy-MM-dd}"
            + (outgoing is null ? ", as the list's first published version." : $", superseding version {outgoing.VersionNumber}."),
            command.Reason,
            outgoing is null ? null : PriceListVersionSnapshot.Of(outgoing),
            PriceListVersionSnapshot.Of(draft),
            cancellationToken);

        return Result.Success(new PriceListPublication(Administered(draft), outgoing?.Id, report.Findings));
    }

    private async Task<BillingValidationReport> CheckAsync(PriceListVersion version, CancellationToken cancellationToken)
    {
        var ledger = await store.ReadCodeHistoryAsync(version.PriceListId, cancellationToken);
        var published = await store.FindPublishedAsync(version.PriceListId, version.OrganisationId, cancellationToken);
        var taxConfiguration = await taxConfigurations.FindPublishedAsync(version.OrganisationId, cancellationToken);
        var others = await store.PublishedVersionsAsync(version.OrganisationId, cancellationToken);
        var references = await catalogue.PublishedPriceListItemReferencesAsync(version.OrganisationId, cancellationToken);

        return PriceListPublicationCheck.Run(version, ledger, published, taxConfiguration, others, references);
    }

    /// <summary>
    /// Every branch a version prices must be one Identity knows <em>and</em> one of the caller's
    /// organisation. The branch is Identity's record; a version pricing an identifier that names none would
    /// price nowhere until the catalogue's publication found it, and one pricing another organisation's
    /// branch would claim it — the one-version-per-branch rule is judged over every organisation — and could
    /// keep its owner from publishing. The domain can check only that the identifier is not empty.
    /// </summary>
    private async Task<Result> BranchesKnownAsync(PriceListVersionDetails details, Guid organisationId, CancellationToken cancellationToken)
    {
        var wanted = details.BranchIds.Where(branch => branch != Guid.Empty).Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return Result.Success();
        }

        var found = await branches.FindManyAsync(wanted, cancellationToken);

        return found.Where(branch => branch.OrganisationId == organisationId).Select(branch => branch.BranchId).ToHashSet().IsSupersetOf(wanted)
            ? Result.Success()
            : Result.Failure(BillingErrors.BranchNotFound("branchIds"));
    }

    private async Task<Result<PriceListVersion>> LoadForChangeAsync(Guid versionId, Guid organisationId, EntityTag expectedVersion, CancellationToken cancellationToken)
    {
        var version = await store.FindVersionAsync(versionId, organisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<PriceListVersion>(BillingErrors.VersionNotFound);
        }

        return expectedVersion.Matches(store.EntityTagOf(version))
            ? Result.Success(version)
            : Result.Failure<PriceListVersion>(BillingErrors.VersionChanged);
    }

    private AdministeredPriceListVersion Administered(PriceListVersion version) => new(version, store.EntityTagOf(version));
}

/// <summary>Create a price list.</summary>
public sealed record CreatePriceListCommand(Guid OrganisationId, string Code, string Name, string? Reason, Guid? By);

/// <summary>Rename a price list.</summary>
public sealed record RenamePriceListCommand(Guid PriceListId, Guid OrganisationId, EntityTag ExpectedVersion, string Name, string? Reason, Guid? By);

/// <summary>Start a draft version.</summary>
public sealed record CreatePriceListDraftCommand(Guid PriceListId, Guid OrganisationId, PriceListVersionDetails Details, Guid? CloneFromVersionId, string? Reason, Guid? By);

/// <summary>Change a draft's own details.</summary>
public sealed record DescribePriceListVersionCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, PriceListVersionDetails Details, string? Reason, Guid? By);

/// <summary>Add an item to a draft.</summary>
public sealed record AddPriceListItemCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, PriceListItemDetails Details, string? Reason, Guid? By);

/// <summary>Replace an item of a draft.</summary>
public sealed record EditPriceListItemCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, Guid ItemId, PriceListItemDetails Details, string? Reason, Guid? By);

/// <summary>Remove an item from a draft.</summary>
public sealed record RemovePriceListItemCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, Guid ItemId, string? Reason, Guid? By);

/// <summary>Add a discount rule to a draft.</summary>
public sealed record AddDiscountRuleCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, DiscountRuleDetails Details, string? Reason, Guid? By);

/// <summary>Replace a discount rule of a draft.</summary>
public sealed record EditDiscountRuleCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, Guid RuleId, DiscountRuleDetails Details, string? Reason, Guid? By);

/// <summary>Remove a discount rule from a draft.</summary>
public sealed record RemoveDiscountRuleCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, Guid RuleId, string? Reason, Guid? By);

/// <summary>Publish a draft.</summary>
public sealed record PublishPriceListVersionCommand(Guid VersionId, Guid OrganisationId, EntityTag ExpectedVersion, string Reason, Guid? By);

/// <summary>A price list beside the tag a client sends back.</summary>
public sealed record AdministeredPriceList(PriceList List, EntityTag Tag);

/// <summary>A version beside the tag a client sends back.</summary>
public sealed record AdministeredPriceListVersion(PriceListVersion Version, EntityTag Tag);

/// <summary>An item as written, with the tag its version carries after the write: the parent row moved with the child.</summary>
public sealed record AdministeredPriceListItem(PriceListItem Item, EntityTag VersionTag);

/// <summary>A discount rule as written, with the tag its version carries after the write.</summary>
public sealed record AdministeredDiscountRule(DiscountRule Rule, EntityTag VersionTag);

/// <summary>What a publication produced.</summary>
public sealed record PriceListPublication(AdministeredPriceListVersion Published, Guid? SupersededVersionId, IReadOnlyList<BillingFinding> Findings);

internal sealed record PriceListSnapshot(string Code, string Name)
{
    public static PriceListSnapshot Of(PriceList list) => new(list.Code, list.Name);
}

internal sealed record PriceListVersionSnapshot(int VersionNumber, string Status, string EffectiveFrom, bool TaxInclusive, string RoundOff, int BranchCount, int ItemCount, int RuleCount)
{
    public static PriceListVersionSnapshot Of(PriceListVersion version)
        => new(
            version.VersionNumber,
            version.Status.ToString(),
            version.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            version.TaxInclusive,
            version.RoundOff.ToString(),
            version.Branches.Count,
            version.Items.Count,
            version.DiscountRules.Count);
}

/// <summary>What the audit trail records about an item.</summary>
/// <remarks>
/// The rate is recorded. <c>docs/nfr/data-classification.md</c> classes rates Confidential, and its
/// treatment table governs logs, traces, telemetry, events and backups — not the audit trail, whose
/// before-and-after state is read under <c>admin.audit.read</c>, the named permission Confidential
/// requires. The consequence is deliberate: a holder of that permission sees what a price was changed
/// from and to, because the trail is what proves who changed it. The audit writer never logs the payload.
/// </remarks>
internal sealed record PriceListItemSnapshot(string Code, string Kind, decimal BaseRate, string Unit, string TaxCode, bool Active)
{
    public static PriceListItemSnapshot Of(PriceListItem item)
        => new(item.Code, item.Kind.ToString(), item.BaseRate, item.Unit, item.TaxCode, item.Active);
}

internal sealed record DiscountRuleSnapshot(string Code, string Kind, decimal MaximumWithoutApproval, decimal Maximum, bool Active)
{
    public static DiscountRuleSnapshot Of(DiscountRule rule)
        => new(rule.Code, rule.Kind.ToString(), rule.MaximumWithoutApproval, rule.Maximum, rule.Active);
}
