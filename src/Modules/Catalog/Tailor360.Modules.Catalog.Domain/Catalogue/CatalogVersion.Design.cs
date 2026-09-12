using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// The design catalogue — groups, options and rules — as part of the version (#30).
/// </summary>
/// <remarks>
/// <para>
/// The version owns them for the reason it owns the categories: an order is confirmed against one
/// published version, and the design a customer chose has to be the one that version held. So a
/// group belongs to a category of the version, is cloned with it, is frozen with it, and is never
/// deleted from a published one.
/// </para>
/// <para>
/// A rule is checked here only for what one version can check on its own: its shape, and that the
/// groups it reads are groups of its category. Whether the options it names exist, whether two
/// rules contradict, whether a required group can still be satisfied — those need the whole
/// category at once and are the publish validator's (issue #138), where an administrator gets every
/// finding in one report rather than one refusal per save.
/// </para>
/// </remarks>
public sealed partial class CatalogVersion
{
    private readonly List<DesignOptionGroup> _designGroups = [];
    private readonly List<DesignRule> _designRules = [];

    /// <summary>The design option groups of every category in this version.</summary>
    public IReadOnlyCollection<DesignOptionGroup> DesignGroups => _designGroups;

    /// <summary>The design rules of every category in this version.</summary>
    public IReadOnlyCollection<DesignRule> DesignRules => _designRules;

    /// <summary>Finds a group by its row identity.</summary>
    /// <param name="designOptionGroupId">The group.</param>
    /// <returns>The group, or null.</returns>
    public DesignOptionGroup? FindDesignGroup(Guid designOptionGroupId)
        => _designGroups.SingleOrDefault(group => group.Id == designOptionGroupId);

    /// <summary>Finds an option by its row identity, in whichever group holds it.</summary>
    /// <param name="designOptionId">The option.</param>
    /// <returns>The option and its group, or null.</returns>
    public (DesignOptionGroup Group, DesignOption Option)? FindDesignOption(Guid designOptionId)
    {
        foreach (var group in _designGroups)
        {
            if (group.FindOption(designOptionId) is { } option)
            {
                return (group, option);
            }
        }

        return null;
    }

    /// <summary>Finds a rule by its row identity.</summary>
    /// <param name="designRuleId">The rule.</param>
    /// <returns>The rule, or null.</returns>
    public DesignRule? FindDesignRule(Guid designRuleId)
        => _designRules.SingleOrDefault(rule => rule.Id == designRuleId);

    /// <summary>The groups of one category, in display order.</summary>
    /// <param name="categoryId">The category.</param>
    /// <returns>The groups.</returns>
    public IEnumerable<DesignOptionGroup> DesignGroupsOf(Guid categoryId)
        => _designGroups
            .Where(group => group.CategoryId == categoryId)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Code, StringComparer.Ordinal);

    /// <summary>The rules of one category, by number.</summary>
    /// <param name="categoryId">The category.</param>
    /// <returns>The rules.</returns>
    public IEnumerable<DesignRule> DesignRulesOf(Guid categoryId)
        => _designRules.Where(rule => rule.CategoryId == categoryId).OrderBy(rule => rule.Number);

    /// <summary>The highest rule number this version holds, or zero.</summary>
    public int HighestDesignRuleNumber
        => _designRules.Count == 0 ? 0 : _designRules.Max(rule => rule.Number);

    /// <summary>Adds a design option group to a category of a draft.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The group's identity as a concept.</param>
    /// <param name="categoryId">The category, which must be in this version.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The group, or the reason it was refused.</returns>
    public Result<DesignOptionGroup> AddDesignGroup(
        Guid id,
        Guid key,
        Guid categoryId,
        DesignGroupDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.VersionNotEditable);
        }

        if (Find(categoryId) is null)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.CategoryNotFound);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(validated.Error);
        }

        if (DesignGroupsOf(categoryId).Any(existing =>
                string.Equals(existing.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.CodeNotUnique("code"));
        }

        var group = new DesignOptionGroup(id, key, Id, OrganisationId, categoryId, details);
        _designGroups.Add(group);
        Touch(now, by);

        return Result.Success(group);
    }

    /// <summary>Replaces what a draft says about a group.</summary>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The group, or the reason it was refused.</returns>
    public Result<DesignOptionGroup> EditDesignGroup(
        Guid designOptionGroupId,
        DesignGroupDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignGroup(designOptionGroupId) is not { } group)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.DesignGroupNotFound);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(validated.Error);
        }

        if (DesignGroupsOf(group.CategoryId).Any(other =>
                other.Id != designOptionGroupId
                && string.Equals(other.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.CodeNotUnique("code"));
        }

        group.Apply(details);
        Touch(now, by);

        return Result.Success(group);
    }

    /// <summary>Removes a group, its options and every rule that reads it from a draft.</summary>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveDesignGroup(Guid designOptionGroupId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignGroup(designOptionGroupId) is not { } group)
        {
            return Result.Failure(CatalogErrors.DesignGroupNotFound);
        }

        // A rule over a group that is gone can never fire, and a service link to it can never be
        // followed; both go with the group, so the draft never carries a reference to nothing.
        _designRules.RemoveAll(rule =>
            rule.CategoryId == group.CategoryId && rule.Details.GroupCodes.Contains(group.Code, StringComparer.Ordinal));
        foreach (var service in ServicesOf(group.CategoryId))
        {
            service.ForgetDesignGroup(group.Id);
        }

        _designGroups.Remove(group);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Adds an option to a group of a draft.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The option's identity as a concept.</param>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    public Result<DesignOption> AddDesignOption(
        Guid id,
        Guid key,
        Guid designOptionGroupId,
        DesignOptionDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignOption>(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignGroup(designOptionGroupId) is not { } group)
        {
            return Result.Failure<DesignOption>(CatalogErrors.DesignGroupNotFound);
        }

        var added = group.AddOption(id, key, details);
        if (added.IsSuccess)
        {
            Touch(now, by);
        }

        return added;
    }

    /// <summary>Replaces what a draft says about an option.</summary>
    /// <param name="designOptionId">The option.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    public Result<DesignOption> EditDesignOption(
        Guid designOptionId,
        DesignOptionDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignOption>(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignOption(designOptionId) is not { } found)
        {
            return Result.Failure<DesignOption>(CatalogErrors.DesignOptionNotFound);
        }

        var edited = found.Group.EditOption(designOptionId, details);
        if (edited.IsSuccess)
        {
            Touch(now, by);
        }

        return edited;
    }

    /// <summary>Removes an option from a draft.</summary>
    /// <param name="designOptionId">The option.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveDesignOption(Guid designOptionId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignOption(designOptionId) is not { } found)
        {
            return Result.Failure(CatalogErrors.DesignOptionNotFound);
        }

        var removed = found.Group.RemoveOption(designOptionId);
        if (removed.IsSuccess)
        {
            Touch(now, by);
        }

        return removed;
    }

    /// <summary>Adds a rule to a category of a draft.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The rule's identity as a concept.</param>
    /// <param name="categoryId">The category whose groups it reads.</param>
    /// <param name="number">The <c>DR-nn</c> number the catalogue allocated, never re-used.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The rule, or the reason it was refused.</returns>
    public Result<DesignRule> AddDesignRule(
        Guid id,
        Guid key,
        Guid categoryId,
        int number,
        DesignRuleDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignRule>(CatalogErrors.VersionNotEditable);
        }

        if (Find(categoryId) is null)
        {
            return Result.Failure<DesignRule>(CatalogErrors.CategoryNotFound);
        }

        var checkedDetails = CheckRule(categoryId, details);
        if (checkedDetails.IsFailure)
        {
            return Result.Failure<DesignRule>(checkedDetails.Error);
        }

        if (number < 1 || _designRules.Any(rule => rule.Number == number))
        {
            return Result.Failure<DesignRule>(CatalogErrors.CodeNotUnique("number"));
        }

        var added = new DesignRule(id, key, Id, OrganisationId, categoryId, number, details);
        _designRules.Add(added);
        Touch(now, by);

        return Result.Success(added);
    }

    /// <summary>Replaces what a draft says about a rule. Its number and category never change.</summary>
    /// <param name="designRuleId">The rule.</param>
    /// <param name="details">The details.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The rule, or the reason it was refused.</returns>
    public Result<DesignRule> EditDesignRule(
        Guid designRuleId,
        DesignRuleDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DesignRule>(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignRule(designRuleId) is not { } rule)
        {
            return Result.Failure<DesignRule>(CatalogErrors.DesignRuleNotFound);
        }

        var checkedDetails = CheckRule(rule.CategoryId, details);
        if (checkedDetails.IsFailure)
        {
            return Result.Failure<DesignRule>(checkedDetails.Error);
        }

        rule.Apply(details);
        Touch(now, by);

        return Result.Success(rule);
    }

    /// <summary>Removes a rule from a draft. Its number is retired with it and never re-used.</summary>
    /// <param name="designRuleId">The rule.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveDesignRule(Guid designRuleId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(CatalogErrors.VersionNotEditable);
        }

        if (FindDesignRule(designRuleId) is not { } rule)
        {
            return Result.Failure(CatalogErrors.DesignRuleNotFound);
        }

        _designRules.Remove(rule);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Corrects the words of a group on a published version.</summary>
    /// <param name="designOptionGroupId">The group.</param>
    /// <param name="presentation">The correction.</param>
    /// <param name="reason">Why.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The group, or the reason it was refused.</returns>
    public Result<DesignOptionGroup> CorrectDesignGroupPresentation(
        Guid designOptionGroupId,
        DesignGroupPresentation presentation,
        string reason,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (Status == CatalogStatus.Draft)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.CorrectionNeedsPublishedVersion);
        }

        if (FindDesignGroup(designOptionGroupId) is not { } group)
        {
            return Result.Failure<DesignOptionGroup>(CatalogErrors.DesignGroupNotFound);
        }

        var validated = presentation.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(validated.Error);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return Result.Failure<DesignOptionGroup>(reasoned.Error);
        }

        group.ApplyPresentation(presentation);
        Touch(now, by);

        return Result.Success(group);
    }

    /// <summary>Corrects the words of an option on a published version.</summary>
    /// <param name="designOptionId">The option.</param>
    /// <param name="presentation">The correction.</param>
    /// <param name="reason">Why.</param>
    /// <param name="now">The instant.</param>
    /// <param name="by">Who.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    public Result<DesignOption> CorrectDesignOptionPresentation(
        Guid designOptionId,
        DesignOptionPresentation presentation,
        string reason,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (Status == CatalogStatus.Draft)
        {
            return Result.Failure<DesignOption>(CatalogErrors.CorrectionNeedsPublishedVersion);
        }

        if (FindDesignOption(designOptionId) is not { } found)
        {
            return Result.Failure<DesignOption>(CatalogErrors.DesignOptionNotFound);
        }

        var validated = presentation.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOption>(validated.Error);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return Result.Failure<DesignOption>(reasoned.Error);
        }

        found.Option.ApplyPresentation(presentation);
        Touch(now, by);

        return Result.Success(found.Option);
    }

    /// <summary>
    /// Copies every group, option and rule of a source version into this draft, category by category.
    /// </summary>
    /// <param name="source">The version being cloned.</param>
    /// <param name="ids">The identifier generator.</param>
    /// <param name="copiedCategories">Source category identity to its copy in this draft.</param>
    /// <returns>Source group identity to its copy in this draft, for the service links.</returns>
    private Dictionary<Guid, Guid> CopyDesignFrom(
        CatalogVersion source,
        IIdGenerator ids,
        Dictionary<Guid, Guid> copiedCategories)
    {
        var copiedGroups = new Dictionary<Guid, Guid>();

        foreach (var group in source._designGroups)
        {
            var copy = group.CopyInto(ids, Id, copiedCategories[group.CategoryId]);
            copiedGroups[group.Id] = copy.Id;
            _designGroups.Add(copy);
        }

        foreach (var rule in source._designRules)
        {
            _designRules.Add(rule.CopyInto(ids.NewId(), Id, copiedCategories[rule.CategoryId]));
        }

        return copiedGroups;
    }

    /// <summary>Drops the groups and rules of categories being removed from a draft.</summary>
    /// <param name="categoryIds">The categories.</param>
    private void RemoveDesignOf(HashSet<Guid> categoryIds)
    {
        _designGroups.RemoveAll(group => categoryIds.Contains(group.CategoryId));
        _designRules.RemoveAll(rule => categoryIds.Contains(rule.CategoryId));
    }

    /// <summary>What one version can check about a rule on its own: its shape, and its groups.</summary>
    private Result CheckRule(Guid categoryId, DesignRuleDetails details)
    {
        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return validated;
        }

        var codes = DesignGroupsOf(categoryId).Select(group => group.Code).ToHashSet(StringComparer.Ordinal);

        return details.GroupCodes.All(codes.Contains)
            ? Result.Success()
            : Result.Failure(CatalogErrors.RuleGroupNotInCategory(
                details.Antecedent.GroupCode is { } antecedent && !codes.Contains(antecedent)
                    ? "antecedent.groupCode"
                    : "consequent.groupCode"));
    }
}
