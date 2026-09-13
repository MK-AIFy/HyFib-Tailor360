using Tailor360.Modules.Billing.Domain.Pricing;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>Create a price list.</summary>
public sealed record CreatePriceListRequest(string? Code, string? Name, string? Reason);

/// <summary>Rename a price list.</summary>
public sealed record RenamePriceListRequest(string? Name, string? Reason);

/// <summary>What a version says about itself.</summary>
public sealed record PriceListVersionRequest(
    string? Name,
    string? Notes,
    DateOnly? EffectiveFrom,
    bool? TaxInclusive,
    string? RoundOff,
    decimal? OverrideThresholdPercent,
    IReadOnlyList<Guid>? BranchIds,
    Guid? CloneFromVersionId,
    string? Reason)
{
    /// <summary>Whether the body said which way rates are quoted; an omitted flag is refused rather than read as exclusive.</summary>
    public bool SaysTaxInclusive => TaxInclusive.HasValue;

    /// <summary>
    /// Whether the body said which branches the version prices. An omitted list is refused rather than read
    /// as none: a whole-value update that left it out would otherwise drop every branch silently. An empty
    /// list, sent on purpose, is a draft pricing nowhere yet, which the publication checks refuse.
    /// </summary>
    public bool SaysBranchIds => BranchIds is not null;

    /// <summary>The details as the domain reads them; an omitted date or threshold becomes a value the domain refuses.</summary>
    public PriceListVersionDetails ToDetails()
        => new(
            Name ?? string.Empty,
            Notes,
            EffectiveFrom ?? default,
            TaxInclusive ?? false,
            Enum.TryParse<RoundOffRule>(RoundOff, ignoreCase: false, out var rule) ? rule : (RoundOffRule)(-1),
            OverrideThresholdPercent ?? -1m,
            BranchIds ?? []);
}

/// <summary>Add or replace an item.</summary>
public sealed record PriceListItemRequest(
    string? Code,
    string? Description,
    string? Kind,
    decimal? BaseRate,
    string? Unit,
    string? TaxCode,
    bool? Active,
    string? Reason)
{
    /// <summary>Whether the body said whether the item is active; an omitted flag is refused rather than read as retired.</summary>
    public bool SaysActive => Active.HasValue;

    /// <summary>The details as the domain reads them; an omitted rate becomes one the domain refuses.</summary>
    public PriceListItemDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Description ?? string.Empty,
            Enum.TryParse<PriceItemKind>(Kind, ignoreCase: false, out var kind) ? kind : (PriceItemKind)(-1),
            BaseRate ?? -1m,
            Unit ?? string.Empty,
            TaxCode ?? string.Empty,
            Active ?? false);
}

/// <summary>Add or replace a discount rule.</summary>
public sealed record DiscountRuleRequest(
    string? Code,
    string? Description,
    string? Kind,
    decimal? MaximumWithoutApproval,
    decimal? Maximum,
    bool? Active,
    string? Reason)
{
    /// <summary>Whether the body said whether the rule is active; an omitted flag is refused rather than read as retired.</summary>
    public bool SaysActive => Active.HasValue;

    /// <summary>The details as the domain reads them; an omitted bound becomes one the domain refuses.</summary>
    public DiscountRuleDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Description ?? string.Empty,
            Enum.TryParse<DiscountKind>(Kind, ignoreCase: false, out var kind) ? kind : (DiscountKind)(-1),
            MaximumWithoutApproval ?? -1m,
            Maximum ?? -1m,
            Active ?? false);
}
