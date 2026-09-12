using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// One design option group — a neckline, a sleeve length, a lining — as one catalogue version holds it.
/// </summary>
/// <remarks>
/// <para>
/// A group belongs to one category of one version, and is offered by the service types of that
/// category that name it (link 3 of <c>docs/prd/category-hierarchy.md</c>). The same code may
/// appear in another category and mean the same thing, but each category holds its own record with
/// its own option list, because a gown offers sleeve lengths a blouse does not (section 2 of the
/// design options document).
/// </para>
/// <para>
/// The version owns the group the way it owns a category: cloned into a new draft with a fresh
/// <see cref="Id"/> and the same <see cref="Key"/>, never edited once published, never deleted —
/// retired by a date or by an option's flag, because confirmed snapshots and printed job cards
/// refer to it.
/// </para>
/// </remarks>
public sealed class DesignOptionGroup
{
    private readonly List<DesignGroupBranch> _branches = [];
    private readonly List<DesignOption> _options = [];

    private DesignOptionGroup()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal DesignOptionGroup(
        Guid id,
        Guid key,
        Guid catalogVersionId,
        Guid organisationId,
        Guid categoryId,
        DesignGroupDetails details)
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

    /// <summary>Identity of the group as a concept, carried unchanged from version to version.</summary>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The category whose garments this group is chosen for.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>The machine key. Immutable once its version is published.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The label staff and customers read.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The Tamil label, where one is confirmed.</summary>
    public string? NameTamil { get; private set; }

    /// <summary>Single or multiple.</summary>
    public DesignSelectionMode SelectionMode { get; private set; }

    /// <summary>Whether a garment may be confirmed with nothing chosen here.</summary>
    public bool Required { get; private set; }

    /// <summary>Where the group sits in the picker.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>The first day it is offered, or null for immediately.</summary>
    public DateOnly? ActiveFrom { get; private set; }

    /// <summary>The last day, or null for indefinitely.</summary>
    public DateOnly? ActiveTo { get; private set; }

    /// <summary>The branches that offer the group.</summary>
    public IReadOnlyCollection<DesignGroupBranch> Branches => _branches;

    /// <summary>The branches that offer the group, as identifiers.</summary>
    public IEnumerable<Guid> BranchIds => _branches.Select(branch => branch.BranchId);

    /// <summary>The options, in the order they were added; sort by <see cref="DesignOption.DisplayOrder"/> to show them.</summary>
    public IReadOnlyCollection<DesignOption> Options => _options;

    /// <summary>Whether the group is within its active period on a given day.</summary>
    /// <param name="on">The day, in the branch's timezone.</param>
    /// <returns>True when the day falls inside the active period.</returns>
    public bool IsActiveOn(DateOnly on)
        => (ActiveFrom is not { } from || on >= from)
           && (ActiveTo is not { } to || on <= to);

    /// <summary>Finds an option of this group by its row identity.</summary>
    /// <param name="optionId">The option.</param>
    /// <returns>The option, or null.</returns>
    public DesignOption? FindOption(Guid optionId)
        => _options.SingleOrDefault(option => option.Id == optionId);

    /// <summary>Finds an option of this group by its code.</summary>
    /// <param name="code">The option code.</param>
    /// <returns>The option, or null.</returns>
    public DesignOption? FindOptionByCode(string code)
        => _options.SingleOrDefault(option => string.Equals(option.Code, code, StringComparison.Ordinal));

    /// <summary>The details as they stand, for a copy or an audit snapshot.</summary>
    public DesignGroupDetails Details => new(
        Code,
        Name,
        NameTamil,
        SelectionMode,
        Required,
        DisplayOrder,
        ActiveFrom,
        ActiveTo,
        [.. BranchIds]);

    /// <summary>Replaces everything an administrator says about the group.</summary>
    /// <param name="details">The validated details.</param>
    internal void Apply(DesignGroupDetails details)
    {
        // A renamed group takes its options' illustration references with it: every anchor is
        // `sheet#group.OPTION`, checked against this code when the option is added, and a rename
        // would otherwise leave each one pointing at a code the sheet no longer carries.
        var renamed = !string.Equals(Code, details.Code, StringComparison.Ordinal);
        Code = details.Code;
        if (renamed)
        {
            foreach (var option in _options)
            {
                option.FollowGroupCode(Code);
            }
        }

        Name = details.Name;
        NameTamil = details.NameTamil;
        SelectionMode = details.SelectionMode;
        Required = details.Required;
        DisplayOrder = details.DisplayOrder;
        ActiveFrom = details.ActiveFrom;
        ActiveTo = details.ActiveTo;

        _branches.Clear();
        foreach (var branchId in details.BranchIds.Distinct())
        {
            _branches.Add(DesignGroupBranch.For(Id, branchId));
        }
    }

    /// <summary>Corrects the words of a published group.</summary>
    /// <param name="presentation">The validated correction.</param>
    internal void ApplyPresentation(DesignGroupPresentation presentation)
    {
        Name = presentation.Name;
        NameTamil = presentation.NameTamil;
        DisplayOrder = presentation.DisplayOrder;
    }

    /// <summary>Adds an option. The version has already checked that it may be edited.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="key">The option's identity as a concept.</param>
    /// <param name="details">The details.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    internal Result<DesignOption> AddOption(Guid id, Guid key, DesignOptionDetails details)
    {
        var validated = CheckOption(details);
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOption>(validated.Error);
        }

        if (FindOptionByCode(details.Code) is not null)
        {
            return Result.Failure<DesignOption>(CatalogErrors.CodeNotUnique("code"));
        }

        var option = new DesignOption(id, key, Id, CatalogVersionId, OrganisationId, details);
        _options.Add(option);

        return Result.Success(option);
    }

    /// <summary>Replaces what is said about an option.</summary>
    /// <param name="optionId">The option.</param>
    /// <param name="details">The details.</param>
    /// <returns>The option, or the reason it was refused.</returns>
    internal Result<DesignOption> EditOption(Guid optionId, DesignOptionDetails details)
    {
        if (FindOption(optionId) is not { } option)
        {
            return Result.Failure<DesignOption>(CatalogErrors.DesignOptionNotFound);
        }

        var validated = CheckOption(details);
        if (validated.IsFailure)
        {
            return Result.Failure<DesignOption>(validated.Error);
        }

        if (_options.Any(other =>
                other.Id != optionId
                && string.Equals(other.Code, details.Code, StringComparison.Ordinal)))
        {
            return Result.Failure<DesignOption>(CatalogErrors.CodeNotUnique("code"));
        }

        option.Apply(details);

        return Result.Success(option);
    }

    /// <summary>What the option says about itself, and that its drawing is anchored on it and this group.</summary>
    private Result CheckOption(DesignOptionDetails details)
    {
        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return validated;
        }

        return details.IllustrationKey is { } key && !DesignCode.IllustrationKeyNames(key, Code, details.Code)
            ? Result.Failure(CatalogErrors.IllustrationKeyNotForThisOption("illustrationKey"))
            : Result.Success();
    }

    /// <summary>Removes an option from a draft.</summary>
    /// <param name="optionId">The option.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    internal Result RemoveOption(Guid optionId)
    {
        if (FindOption(optionId) is not { } option)
        {
            return Result.Failure(CatalogErrors.DesignOptionNotFound);
        }

        _options.Remove(option);

        return Result.Success();
    }

    /// <summary>Copies the group and its options into a new version.</summary>
    /// <param name="ids">The identifier generator, for the copies' row identities.</param>
    /// <param name="catalogVersionId">The version being built.</param>
    /// <param name="categoryId">The copy of this group's category in the new version.</param>
    /// <returns>The copy, carrying the same <see cref="Key"/> on the group and on every option.</returns>
    internal DesignOptionGroup CopyInto(IIdGenerator ids, Guid catalogVersionId, Guid categoryId)
    {
        var copy = new DesignOptionGroup(ids.NewId(), Key, catalogVersionId, OrganisationId, categoryId, Details);
        foreach (var option in _options)
        {
            copy._options.Add(option.CopyInto(ids.NewId(), copy.Id, catalogVersionId));
        }

        return copy;
    }
}
