using Tailor360.Modules.Billing.Domain.Pricing;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>A price list.</summary>
public sealed record PriceListPayload(Guid PriceListId, string Code, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a list.</summary>
    public static PriceListPayload From(PriceList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new PriceListPayload(list.Id, list.Code, list.Name, list.CreatedAt, list.UpdatedAt);
    }
}

/// <summary>A price-list version without its contents: what a list shows.</summary>
public sealed record PriceListVersionSummaryPayload(
    Guid PriceListVersionId,
    Guid PriceListId,
    int VersionNumber,
    string Name,
    string? Notes,
    string Status,
    DateOnly EffectiveFrom,
    bool TaxInclusive,
    string RoundOff,
    decimal OverrideThresholdPercent,
    IReadOnlyList<Guid> BranchIds,
    Guid? ClonedFromVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt)
{
    /// <summary>Projects a version.</summary>
    public static PriceListVersionSummaryPayload From(PriceListVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new PriceListVersionSummaryPayload(
            version.Id,
            version.PriceListId,
            version.VersionNumber,
            version.Name,
            version.Notes,
            version.Status.ToString(),
            version.EffectiveFrom,
            version.TaxInclusive,
            version.RoundOff.ToString(),
            version.OverrideThresholdPercent,
            [.. version.BranchIds.Order()],
            version.ClonedFromVersionId,
            version.CreatedAt,
            version.PublishedAt,
            version.RetiredAt);
    }
}

/// <summary>A price-list version and everything in it.</summary>
public sealed record PriceListVersionPayload(
    PriceListVersionSummaryPayload Version,
    IReadOnlyList<PriceListItemPayload> Items,
    IReadOnlyList<DiscountRulePayload> DiscountRules)
{
    /// <summary>Projects a version and its contents.</summary>
    public static PriceListVersionPayload From(PriceListVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new PriceListVersionPayload(
            PriceListVersionSummaryPayload.From(version),
            [.. version.Items.OrderBy(item => item.Code, StringComparer.Ordinal).Select(PriceListItemPayload.From)],
            [.. version.DiscountRules.OrderBy(rule => rule.Code, StringComparer.Ordinal).Select(DiscountRulePayload.From)]);
    }
}

/// <summary>One price-list item.</summary>
public sealed record PriceListItemPayload(
    Guid PriceListItemId,
    Guid PriceListItemKey,
    string Code,
    string Description,
    string Kind,
    decimal BaseRate,
    string Unit,
    string TaxCode,
    bool Active)
{
    /// <summary>Projects an item.</summary>
    public static PriceListItemPayload From(PriceListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new PriceListItemPayload(
            item.Id, item.Key, item.Code, item.Description, item.Kind.ToString(), item.BaseRate, item.Unit, item.TaxCode, item.Active);
    }
}

/// <summary>One discount rule.</summary>
public sealed record DiscountRulePayload(
    Guid DiscountRuleId,
    Guid DiscountRuleKey,
    string Code,
    string Description,
    string Kind,
    decimal MaximumWithoutApproval,
    decimal Maximum,
    bool Active)
{
    /// <summary>Projects a rule.</summary>
    public static DiscountRulePayload From(DiscountRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new DiscountRulePayload(
            rule.Id, rule.Key, rule.Code, rule.Description, rule.Kind.ToString(), rule.MaximumWithoutApproval, rule.Maximum, rule.Active);
    }
}

/// <summary>What a publication produced.</summary>
public sealed record PriceListPublicationPayload(
    PriceListVersionPayload Published,
    Guid? SupersededVersionId,
    IReadOnlyList<BillingFindingPayload> Findings);
