using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The design catalogue's administration: groups, options and rules on a draft, and their words on a
/// published version (#30, issue #137).
/// </summary>
/// <remarks>
/// Every command here takes the same shape as the category and service-type commands: the version is
/// loaded against the tag the administrator read, the aggregate decides, the store saves, and the audit
/// trail records what changed and why. Nothing is evaluated — a rule is stored, not run; running it
/// is issue #138's.
/// </remarks>
public sealed partial class CatalogHandler
{
    /// <summary>The platform sequence the <c>DR-nn</c> numbers come from, scoped per organisation.</summary>
    public const string DesignRuleNumberSequence = "catalog.design-rule";

    /// <summary>The audit action for a group added to a draft.</summary>
    public const string DesignGroupAddedAction = "catalog.design_group.added";

    /// <summary>The audit action for a group changed in a draft.</summary>
    public const string DesignGroupChangedAction = "catalog.design_group.changed";

    /// <summary>The audit action for a group removed from a draft.</summary>
    public const string DesignGroupRemovedAction = "catalog.design_group.removed";

    /// <summary>The audit action for an option added to a draft.</summary>
    public const string DesignOptionAddedAction = "catalog.design_option.added";

    /// <summary>The audit action for an option changed in a draft.</summary>
    public const string DesignOptionChangedAction = "catalog.design_option.changed";

    /// <summary>The audit action for an option removed from a draft.</summary>
    public const string DesignOptionRemovedAction = "catalog.design_option.removed";

    /// <summary>The audit action for a rule added to a draft.</summary>
    public const string DesignRuleAddedAction = "catalog.design_rule.added";

    /// <summary>The audit action for a rule changed in a draft.</summary>
    public const string DesignRuleChangedAction = "catalog.design_rule.changed";

    /// <summary>The audit action for a rule removed from a draft.</summary>
    public const string DesignRuleRemovedAction = "catalog.design_rule.removed";

    /// <summary>Adds a design option group to a category of a draft.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The group, or the reason it was refused.</returns>
    public async Task<Result<DesignOptionGroup>> AddDesignGroupAsync(
        AddDesignGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(found.Error);
        }

        var version = found.Value;
        var added = version.AddDesignGroup(
            ids.NewId(), ids.NewId(), command.CategoryId, command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return added;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, added.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignGroupAddedAction,
            version.Id,
            $"Design group '{reference}' added to catalogue version {version.VersionNumber}.",
            null,
            null,
            CatalogDesignSnapshot.Of(added.Value, reference),
            cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about a group.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The group, or the reason it was refused.</returns>
    public async Task<Result<DesignOptionGroup>> EditDesignGroupAsync(
        EditDesignGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(found.Error);
        }

        var version = found.Value;
        var before = version.FindDesignGroup(command.DesignOptionGroupId) is { } existing
            ? CatalogDesignSnapshot.Of(existing, ReferenceOf(version, existing))
            : null;

        var edited = version.EditDesignGroup(
            command.DesignOptionGroupId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return edited;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, edited.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignGroupChangedAction,
            version.Id,
            $"Design group '{reference}' changed in catalogue version {version.VersionNumber}.",
            command.Reason,
            before,
            CatalogDesignSnapshot.Of(edited.Value, reference),
            cancellationToken);

        return edited;
    }

    /// <summary>Removes a group, its options and the rules that read it from a draft.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public async Task<Result> RemoveDesignGroupAsync(
        RemoveDesignGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value;
        var existing = version.FindDesignGroup(command.DesignOptionGroupId);
        var reference = existing is null ? string.Empty : ReferenceOf(version, existing);
        var before = existing is null ? null : CatalogDesignSnapshot.Of(existing, reference);
        var rulesBefore = version.DesignRules.Count;

        var removed = version.RemoveDesignGroup(command.DesignOptionGroupId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return removed;
        }

        await store.SaveAsync(cancellationToken);

        var rulesGone = rulesBefore - version.DesignRules.Count;
        await CatalogAudit.RecordAsync(
            audit,
            DesignGroupRemovedAction,
            version.Id,
            $"Design group '{reference}' removed from catalogue version {version.VersionNumber}, with "
            + $"{before?.OptionCount ?? 0} option(s) and {rulesGone} rule(s) that read it.",
            command.Reason,
            before,
            null,
            cancellationToken);

        return removed;
    }

    /// <summary>Adds an option to a group of a draft.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    public async Task<Result<DesignOption>> AddDesignOptionAsync(
        AddDesignOptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignOption>(found.Error);
        }

        var version = found.Value;
        var added = version.AddDesignOption(
            ids.NewId(), ids.NewId(), command.DesignOptionGroupId, command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return added;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, added.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignOptionAddedAction,
            version.Id,
            $"Design option '{reference}' added to catalogue version {version.VersionNumber}.",
            null,
            null,
            CatalogDesignSnapshot.Of(added.Value, reference),
            cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about an option.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    public async Task<Result<DesignOption>> EditDesignOptionAsync(
        EditDesignOptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignOption>(found.Error);
        }

        var version = found.Value;
        var before = version.FindDesignOption(command.DesignOptionId) is { } existing
            ? CatalogDesignSnapshot.Of(existing.Option, ReferenceOf(version, existing.Option))
            : null;

        var edited = version.EditDesignOption(
            command.DesignOptionId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return edited;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, edited.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignOptionChangedAction,
            version.Id,
            $"Design option '{reference}' changed in catalogue version {version.VersionNumber}.",
            command.Reason,
            before,
            CatalogDesignSnapshot.Of(edited.Value, reference),
            cancellationToken);

        return edited;
    }

    /// <summary>Removes an option from a draft.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public async Task<Result> RemoveDesignOptionAsync(
        RemoveDesignOptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value;
        var existing = version.FindDesignOption(command.DesignOptionId);
        var reference = existing is null ? string.Empty : ReferenceOf(version, existing.Value.Option);
        var before = existing is null ? null : CatalogDesignSnapshot.Of(existing.Value.Option, reference);

        var removed = version.RemoveDesignOption(command.DesignOptionId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return removed;
        }

        await store.SaveAsync(cancellationToken);
        await CatalogAudit.RecordAsync(
            audit,
            DesignOptionRemovedAction,
            version.Id,
            $"Design option '{reference}' removed from catalogue version {version.VersionNumber}.",
            command.Reason,
            before,
            null,
            cancellationToken);

        return removed;
    }

    /// <summary>Adds a rule to a category of a draft, allocating its number.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or the reason it was refused.</returns>
    public async Task<Result<DesignRule>> AddDesignRuleAsync(
        AddDesignRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignRule>(found.Error);
        }

        var version = found.Value;

        // Section 4 rule 7: allocated by the catalogue, unique across every version the organisation
        // has ever held, and never re-used — a removed rule's number is retired with it. The platform
        // sequence is what gives all three: it hands out each number once, under a row lock, whatever
        // two administrators are doing in two drafts at the same moment, and a number it has given
        // out is never given out again, whether or not the rule that took it still exists.
        var number = checked((int)await sequences.NextAsync(
            DesignRuleNumberSequence, command.OrganisationId.ToString("N"), cancellationToken));

        var added = version.AddDesignRule(
            ids.NewId(), ids.NewId(), command.CategoryId, number, command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return added;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, added.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignRuleAddedAction,
            version.Id,
            $"Design rule {reference} added to catalogue version {version.VersionNumber}.",
            null,
            null,
            CatalogDesignSnapshot.Of(added.Value, reference),
            cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about a rule.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or the reason it was refused.</returns>
    public async Task<Result<DesignRule>> EditDesignRuleAsync(
        EditDesignRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<DesignRule>(found.Error);
        }

        var version = found.Value;
        var before = version.FindDesignRule(command.DesignRuleId) is { } existing
            ? CatalogDesignSnapshot.Of(existing, ReferenceOf(version, existing))
            : null;

        var edited = version.EditDesignRule(command.DesignRuleId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return edited;
        }

        await store.SaveAsync(cancellationToken);

        var reference = ReferenceOf(version, edited.Value);
        await CatalogAudit.RecordAsync(
            audit,
            DesignRuleChangedAction,
            version.Id,
            $"Design rule {reference} changed in catalogue version {version.VersionNumber}.",
            command.Reason,
            before,
            CatalogDesignSnapshot.Of(edited.Value, reference),
            cancellationToken);

        return edited;
    }

    /// <summary>Removes a rule from a draft. Its number is retired with it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public async Task<Result> RemoveDesignRuleAsync(
        RemoveDesignRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value;
        var existing = version.FindDesignRule(command.DesignRuleId);
        var reference = existing is null ? string.Empty : ReferenceOf(version, existing);
        var before = existing is null ? null : CatalogDesignSnapshot.Of(existing, reference);

        var removed = version.RemoveDesignRule(command.DesignRuleId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return removed;
        }

        await store.SaveAsync(cancellationToken);
        await CatalogAudit.RecordAsync(
            audit,
            DesignRuleRemovedAction,
            version.Id,
            $"Design rule {reference} removed from catalogue version {version.VersionNumber}. Its number "
            + "is retired with it and is never given to another rule.",
            command.Reason,
            before,
            null,
            cancellationToken);

        return removed;
    }

    /// <summary>Corrects the words of a group or an option on a published version.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it was refused.</returns>
    public async Task<Result<AdministeredCatalogVersion>> CorrectDesignPresentationAsync(
        CorrectDesignPresentationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(found.Error);
        }

        var version = found.Value;
        ICatalogAuditState? before;
        ICatalogAuditState after;
        string what;

        if (command.DesignOptionGroupId is { } groupId && command.GroupPresentation is { } groupWords)
        {
            var existing = version.FindDesignGroup(groupId);
            before = existing is null ? null : CatalogDesignSnapshot.Of(existing, ReferenceOf(version, existing));

            var corrected = version.CorrectDesignGroupPresentation(
                groupId, groupWords, command.Reason, clock.UtcNow, command.By);
            if (corrected.IsFailure)
            {
                return Result.Failure<AdministeredCatalogVersion>(corrected.Error);
            }

            var reference = ReferenceOf(version, corrected.Value);
            after = CatalogDesignSnapshot.Of(corrected.Value, reference);
            what = $"design group '{reference}'";
        }
        else if (command.DesignOptionId is { } optionId && command.OptionPresentation is { } optionWords)
        {
            var existing = version.FindDesignOption(optionId);
            before = existing is null
                ? null
                : CatalogDesignSnapshot.Of(existing.Value.Option, ReferenceOf(version, existing.Value.Option));

            var corrected = version.CorrectDesignOptionPresentation(
                optionId, optionWords, command.Reason, clock.UtcNow, command.By);
            if (corrected.IsFailure)
            {
                return Result.Failure<AdministeredCatalogVersion>(corrected.Error);
            }

            var reference = ReferenceOf(version, corrected.Value);
            after = CatalogDesignSnapshot.Of(corrected.Value, reference);
            what = $"design option '{reference}'";
        }
        else
        {
            return Result.Failure<AdministeredCatalogVersion>(
                CatalogErrors.Required("designOptionGroupId or designOptionId"));
        }

        await store.SaveAsync(cancellationToken);
        cache.Invalidate();

        await CatalogAudit.RecordAsync(
            audit,
            LabelCorrectedAction,
            version.Id,
            $"Presentation corrected on {what} in catalogue version {version.VersionNumber}. Words are "
            + "read by people and by nothing else, so nothing priced, worked to or reported changes.",
            command.Reason,
            before,
            after,
            cancellationToken);

        return Result.Success(Administered(version));
    }

    private static string ReferenceOf(CatalogVersion version, DesignOptionGroup group)
        => $"{version.Find(group.CategoryId)?.Code ?? "?"}.{group.Code}";

    private static string ReferenceOf(CatalogVersion version, DesignOption option)
        => version.FindDesignGroup(option.DesignOptionGroupId) is { } group
            ? $"{ReferenceOf(version, group)}.{option.Code}"
            : option.Code;

    private static string ReferenceOf(CatalogVersion version, DesignRule rule)
        => $"{rule.Identifier} ({version.Find(rule.CategoryId)?.Code ?? "?"})";
}

/// <summary>What the audit trail keeps of a design group, option or rule: its reference, its words, its place.</summary>
internal sealed record CatalogDesignSnapshot(
    string Kind,
    string Reference,
    string Name,
    int DisplayOrder,
    int OptionCount,
    string? Statement) : ICatalogAuditState
{
    public static CatalogDesignSnapshot Of(DesignOptionGroup group, string reference)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new CatalogDesignSnapshot(
            "group", reference, group.Name, group.DisplayOrder, group.Options.Count, null);
    }

    public static CatalogDesignSnapshot Of(DesignOption option, string reference)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new CatalogDesignSnapshot(
            "option", reference, option.Name, option.DisplayOrder, 0, option.Active ? null : "retired");
    }

    public static CatalogDesignSnapshot Of(DesignRule rule, string reference)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new CatalogDesignSnapshot("rule", reference, rule.Type.ToString(), rule.Number, 0, rule.Statement);
    }
}

/// <summary>Adds a design option group to a category of a draft.</summary>
public sealed record AddDesignGroupCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid CategoryId,
    DesignGroupDetails Details,
    Guid? By);

/// <summary>Replaces what a draft says about a group.</summary>
public sealed record EditDesignGroupCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignOptionGroupId,
    DesignGroupDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Removes a group from a draft.</summary>
public sealed record RemoveDesignGroupCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignOptionGroupId,
    string? Reason,
    Guid? By);

/// <summary>Adds an option to a group of a draft.</summary>
public sealed record AddDesignOptionCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignOptionGroupId,
    DesignOptionDetails Details,
    Guid? By);

/// <summary>Replaces what a draft says about an option.</summary>
public sealed record EditDesignOptionCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignOptionId,
    DesignOptionDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Removes an option from a draft.</summary>
public sealed record RemoveDesignOptionCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignOptionId,
    string? Reason,
    Guid? By);

/// <summary>Adds a rule to a category of a draft.</summary>
public sealed record AddDesignRuleCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid CategoryId,
    DesignRuleDetails Details,
    Guid? By);

/// <summary>Replaces what a draft says about a rule.</summary>
public sealed record EditDesignRuleCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignRuleId,
    DesignRuleDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Removes a rule from a draft.</summary>
public sealed record RemoveDesignRuleCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid DesignRuleId,
    string? Reason,
    Guid? By);

/// <summary>Corrects the words of a group or an option on a published version.</summary>
public sealed record CorrectDesignPresentationCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? DesignOptionGroupId,
    Guid? DesignOptionId,
    DesignGroupPresentation? GroupPresentation,
    DesignOptionPresentation? OptionPresentation,
    string Reason,
    Guid? By);
