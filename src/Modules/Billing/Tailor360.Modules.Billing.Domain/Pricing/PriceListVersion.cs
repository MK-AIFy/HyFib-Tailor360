using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>
/// One version of a price list: the items and discount rules in force from a date at a set of
/// branches, with the two conventions — inclusive or exclusive rates, the document round-off — that
/// every calculation on it follows (<c>docs/prd/glossary.md</c>, <c>docs/architecture/conventions.md</c>
/// section 1).
/// </summary>
/// <remarks>
/// Shaped as the tax configuration version is, for the same reason: a calculation is pinned to one
/// version (INV-INV-03), so a change is a clone, an edit and a second publication. Exactly one version
/// of a list is published at a time, and a branch is priced by at most one published version across
/// every list, which the publication checks enforce. A published version's rows are frozen by a
/// database trigger as well as by this class.
/// </remarks>
public sealed class PriceListVersion
{
    /// <summary>The longest reason accepted.</summary>
    public const int MaximumReasonLength = 500;

    private readonly List<PriceListItem> _items = [];
    private readonly List<DiscountRule> _discountRules = [];
    private readonly List<PriceListVersionBranch> _branches = [];

    private PriceListVersion()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PriceListVersion(
        Guid id,
        Guid priceListId,
        Guid organisationId,
        int versionNumber,
        PriceListVersionDetails details,
        Guid? clonedFromVersionId,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        PriceListId = priceListId;
        OrganisationId = organisationId;
        VersionNumber = versionNumber;
        ClonedFromVersionId = clonedFromVersionId;
        Status = PriceListVersionStatus.Draft;
        CreatedAt = now;
        CreatedBy = by;
        Apply(details, now, by);
    }

    /// <summary>This version.</summary>
    public Guid Id { get; private set; }

    /// <summary>The list this is a version of.</summary>
    public Guid PriceListId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The number an administrator reads; unique per list.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>Where the version stands.</summary>
    public PriceListVersionStatus Status { get; private set; }

    /// <summary>What the version is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Why it exists.</summary>
    public string? Notes { get; private set; }

    /// <summary>The first business day the version applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Whether the rates include tax.</summary>
    public bool TaxInclusive { get; private set; }

    /// <summary>How a document total is rounded.</summary>
    public RoundOffRule RoundOff { get; private set; }

    /// <summary>The variance above which an override needs approval.</summary>
    public decimal OverrideThresholdPercent { get; private set; }

    /// <summary>The version this one was cloned from, when it was.</summary>
    public Guid? ClonedFromVersionId { get; private set; }

    /// <summary>When the version was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it was published.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who published it.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Why it was published.</summary>
    public string? PublishReason { get; private set; }

    /// <summary>When it was retired.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Who retired it.</summary>
    public Guid? RetiredBy { get; private set; }

    /// <summary>Why it was retired.</summary>
    public string? RetiredReason { get; private set; }

    /// <summary>The branches this version prices for.</summary>
    public IReadOnlyCollection<PriceListVersionBranch> Branches => _branches;

    /// <summary>The branch identifiers.</summary>
    public IEnumerable<Guid> BranchIds => _branches.Select(branch => branch.BranchId);

    /// <summary>The items.</summary>
    public IReadOnlyCollection<PriceListItem> Items => _items;

    /// <summary>The discount rules.</summary>
    public IReadOnlyCollection<DiscountRule> DiscountRules => _discountRules;

    /// <summary>Whether the version may still change.</summary>
    public bool IsEditable => Status == PriceListVersionStatus.Draft;

    /// <summary>The details as an administrator would re-enter them.</summary>
    public PriceListVersionDetails Details
        => new(Name, Notes, EffectiveFrom, TaxInclusive, RoundOff, OverrideThresholdPercent, [.. BranchIds]);

    /// <summary>Whether the version prices for a branch.</summary>
    public bool Covers(Guid branchId) => _branches.Any(branch => branch.BranchId == branchId);

    /// <summary>Starts an empty draft.</summary>
    public static Result<PriceListVersion> CreateDraft(
        Guid id,
        Guid priceListId,
        Guid organisationId,
        int versionNumber,
        PriceListVersionDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        var validated = details.Validate();

        return validated.IsFailure
            ? Result.Failure<PriceListVersion>(validated.Error)
            : Result.Success(new PriceListVersion(id, priceListId, organisationId, versionNumber, details, null, now, by));
    }

    /// <summary>Starts a draft holding copies of this version's items and rules, with the same keys and fresh rows.</summary>
    public Result<PriceListVersion> CloneAsDraft(
        IIdGenerator ids,
        int versionNumber,
        PriceListVersionDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(details);

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<PriceListVersion>(validated.Error);
        }

        var clone = new PriceListVersion(ids.NewId(), PriceListId, OrganisationId, versionNumber, details, Id, now, by);
        foreach (var item in _items)
        {
            clone._items.Add(item.CopyInto(ids, clone.Id));
        }

        foreach (var rule in _discountRules)
        {
            clone._discountRules.Add(rule.CopyInto(ids, clone.Id));
        }

        return Result.Success(clone);
    }

    /// <summary>Changes what a draft says about itself.</summary>
    public Result Describe(PriceListVersionDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure(BillingErrors.VersionNotEditable);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return validated;
        }

        Apply(details, now, by);

        return Result.Success();
    }

    /// <summary>One item by identifier.</summary>
    public PriceListItem? FindItem(Guid itemId) => _items.Find(item => item.Id == itemId);

    /// <summary>One item by code.</summary>
    public PriceListItem? FindItemByCode(string code)
        => _items.Find(item => string.Equals(item.Code, code, StringComparison.Ordinal));

    /// <summary>One rule by identifier.</summary>
    public DiscountRule? FindDiscountRule(Guid ruleId) => _discountRules.Find(rule => rule.Id == ruleId);

    /// <summary>One rule by code.</summary>
    public DiscountRule? FindDiscountRuleByCode(string code)
        => _discountRules.Find(rule => string.Equals(rule.Code, code, StringComparison.Ordinal));

    /// <summary>Adds an item to a draft.</summary>
    public Result<PriceListItem> AddItem(Guid id, Guid key, PriceListItemDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<PriceListItem>(BillingErrors.VersionNotEditable);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<PriceListItem>(validated.Error);
        }

        if (FindItemByCode(details.Code) is not null)
        {
            return Result.Failure<PriceListItem>(BillingErrors.CodeNotUnique("code"));
        }

        var added = new PriceListItem(id, key, Id, OrganisationId, details);
        _items.Add(added);
        Touch(now, by);

        return Result.Success(added);
    }

    /// <summary>Replaces what a draft says about an item.</summary>
    public Result<PriceListItem> EditItem(Guid itemId, PriceListItemDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<PriceListItem>(BillingErrors.VersionNotEditable);
        }

        if (FindItem(itemId) is not { } item)
        {
            return Result.Failure<PriceListItem>(BillingErrors.ItemNotFound);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<PriceListItem>(validated.Error);
        }

        if (FindItemByCode(details.Code) is { } other && other.Id != itemId)
        {
            return Result.Failure<PriceListItem>(BillingErrors.CodeNotUnique("code"));
        }

        item.Apply(details);
        Touch(now, by);

        return Result.Success(item);
    }

    /// <summary>Removes an item from a draft.</summary>
    public Result RemoveItem(Guid itemId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(BillingErrors.VersionNotEditable);
        }

        if (FindItem(itemId) is not { } item)
        {
            return Result.Failure(BillingErrors.ItemNotFound);
        }

        _items.Remove(item);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Adds a discount rule to a draft.</summary>
    public Result<DiscountRule> AddDiscountRule(Guid id, Guid key, DiscountRuleDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DiscountRule>(BillingErrors.VersionNotEditable);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DiscountRule>(validated.Error);
        }

        if (FindDiscountRuleByCode(details.Code) is not null)
        {
            return Result.Failure<DiscountRule>(BillingErrors.CodeNotUnique("code"));
        }

        var added = new DiscountRule(id, key, Id, OrganisationId, details);
        _discountRules.Add(added);
        Touch(now, by);

        return Result.Success(added);
    }

    /// <summary>Replaces what a draft says about a discount rule.</summary>
    public Result<DiscountRule> EditDiscountRule(Guid ruleId, DiscountRuleDetails details, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!IsEditable)
        {
            return Result.Failure<DiscountRule>(BillingErrors.VersionNotEditable);
        }

        if (FindDiscountRule(ruleId) is not { } rule)
        {
            return Result.Failure<DiscountRule>(BillingErrors.DiscountRuleNotFound);
        }

        var validated = details.Validate();
        if (validated.IsFailure)
        {
            return Result.Failure<DiscountRule>(validated.Error);
        }

        if (FindDiscountRuleByCode(details.Code) is { } other && other.Id != ruleId)
        {
            return Result.Failure<DiscountRule>(BillingErrors.CodeNotUnique("code"));
        }

        rule.Apply(details);
        Touch(now, by);

        return Result.Success(rule);
    }

    /// <summary>Removes a discount rule from a draft.</summary>
    public Result RemoveDiscountRule(Guid ruleId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(BillingErrors.VersionNotEditable);
        }

        if (FindDiscountRule(ruleId) is not { } rule)
        {
            return Result.Failure(BillingErrors.DiscountRuleNotFound);
        }

        _discountRules.Remove(rule);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Publishes a draft. The caller retires the version it supersedes.</summary>
    public Result Publish(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != PriceListVersionStatus.Draft)
        {
            return Result.Failure(BillingErrors.VersionNotPublishable);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = PriceListVersionStatus.Published;
        PublishedAt = now;
        PublishedBy = by;
        PublishReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Retires the published version, because a successor was published.</summary>
    public Result Retire(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != PriceListVersionStatus.Published)
        {
            return Result.Failure(BillingErrors.VersionNotRetirable);
        }

        var reasoned = CheckReason(reason);
        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = PriceListVersionStatus.Retired;
        RetiredAt = now;
        RetiredBy = by;
        RetiredReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Checks a reason where one is required.</summary>
    public static Result CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(BillingErrors.ReasonRequired);
        }

        return reason.Length > MaximumReasonLength
            ? Result.Failure(BillingErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private void Apply(PriceListVersionDetails details, DateTimeOffset now, Guid? by)
    {
        Name = details.Name.Trim();
        Notes = details.Notes;
        EffectiveFrom = details.EffectiveFrom;
        TaxInclusive = details.TaxInclusive;
        RoundOff = details.RoundOff;
        OverrideThresholdPercent = details.OverrideThresholdPercent;
        _branches.Clear();
        foreach (var branchId in details.BranchIds.Distinct())
        {
            _branches.Add(PriceListVersionBranch.For(Id, branchId));
        }

        Touch(now, by);
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
