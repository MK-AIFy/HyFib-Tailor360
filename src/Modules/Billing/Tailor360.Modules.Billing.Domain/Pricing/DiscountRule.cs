using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>One discount rule of one price-list version. The <see cref="Key"/> is the concept across versions.</summary>
public sealed class DiscountRule
{
    private DiscountRule()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal DiscountRule(Guid id, Guid key, Guid versionId, Guid organisationId, DiscountRuleDetails details)
    {
        Id = id;
        Key = key;
        PriceListVersionId = versionId;
        OrganisationId = organisationId;
        Apply(details);
    }

    /// <summary>This row.</summary>
    public Guid Id { get; private set; }

    /// <summary>The concept, carried across versions by cloning.</summary>
    public Guid Key { get; private set; }

    /// <summary>The version this row belongs to.</summary>
    public Guid PriceListVersionId { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The stable business key.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the discount is for.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>A percentage or an amount.</summary>
    public DiscountKind Kind { get; private set; }

    /// <summary>The largest value the counter may give on its own.</summary>
    public decimal MaximumWithoutApproval { get; private set; }

    /// <summary>The largest value anyone may give, with approval.</summary>
    public decimal Maximum { get; private set; }

    /// <summary>Whether the rule may be applied to a new line.</summary>
    public bool Active { get; private set; }

    /// <summary>The details as an administrator would re-enter them.</summary>
    public DiscountRuleDetails Details => new(Code, Description, Kind, MaximumWithoutApproval, Maximum, Active);

    internal void Apply(DiscountRuleDetails details)
    {
        Code = details.Code;
        Description = details.Description.Trim();
        Kind = details.Kind;
        MaximumWithoutApproval = details.MaximumWithoutApproval;
        Maximum = details.Maximum;
        Active = details.Active;
    }

    internal DiscountRule CopyInto(IIdGenerator ids, Guid versionId)
        => new(ids.NewId(), Key, versionId, OrganisationId, Details);
}
