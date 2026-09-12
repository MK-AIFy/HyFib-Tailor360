using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>One item of one price-list version. The <see cref="Key"/> is the concept across versions.</summary>
public sealed class PriceListItem
{
    private PriceListItem()
    {
        // The persistence layer materialises instances through this constructor.
    }

    internal PriceListItem(Guid id, Guid key, Guid versionId, Guid organisationId, PriceListItemDetails details)
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

    /// <summary>What is charged for.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>What the item prices.</summary>
    public PriceItemKind Kind { get; private set; }

    /// <summary>The rate per unit.</summary>
    public decimal BaseRate { get; private set; }

    /// <summary>What one of it is.</summary>
    public string Unit { get; private set; } = string.Empty;

    /// <summary>The tax code that classifies it.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>Whether the item may be priced.</summary>
    public bool Active { get; private set; }

    /// <summary>The details as an administrator would re-enter them.</summary>
    public PriceListItemDetails Details => new(Code, Description, Kind, BaseRate, Unit, TaxCode, Active);

    internal void Apply(PriceListItemDetails details)
    {
        Code = details.Code;
        Description = details.Description.Trim();
        Kind = details.Kind;
        BaseRate = details.BaseRate;
        Unit = details.Unit;
        TaxCode = details.TaxCode;
        Active = details.Active;
    }

    internal PriceListItem CopyInto(IIdGenerator ids, Guid versionId)
        => new(ids.NewId(), Key, versionId, OrganisationId, Details);
}
