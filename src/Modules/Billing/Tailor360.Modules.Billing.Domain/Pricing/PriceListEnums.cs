namespace Tailor360.Modules.Billing.Domain.Pricing;

/// <summary>Where a price-list version stands. The ordinals are what the database stores.</summary>
public enum PriceListVersionStatus
{
    /// <summary>Being written; freely editable; prices nothing.</summary>
    Draft = 0,

    /// <summary>In force; immutable; what every calculation from its effective date reads.</summary>
    Published = 1,

    /// <summary>Superseded; immutable; still readable, because snapshots are pinned to it.</summary>
    Retired = 2,
}

/// <summary>What a price-list item prices.</summary>
public enum PriceItemKind
{
    /// <summary>A service type's base charge — the item the catalogue's link 4 names.</summary>
    Service = 0,

    /// <summary>An addition to a service: a design option's impact, extra labour.</summary>
    Surcharge = 1,

    /// <summary>A material sold with the garment, priced per unit.</summary>
    Material = 2,
}

/// <summary>How a discount rule states its value.</summary>
public enum DiscountKind
{
    /// <summary>A percentage of the line's base amount.</summary>
    Percentage = 0,

    /// <summary>A fixed amount off the line.</summary>
    Amount = 1,
}

/// <summary>How a document total is rounded (<c>docs/architecture/conventions.md</c> section 1.2, OD-05).</summary>
public enum RoundOffRule
{
    /// <summary>No document round-off; the total is the sum of the rounded lines.</summary>
    None = 0,

    /// <summary>The total is rounded to the nearest rupee and the difference recorded as round-off.</summary>
    NearestRupee = 1,
}
