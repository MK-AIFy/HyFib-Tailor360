namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>Where a tax configuration version stands. The ordinals are what the database stores.</summary>
public enum TaxConfigurationStatus
{
    /// <summary>Being written; freely editable; calculates nothing.</summary>
    Draft = 0,

    /// <summary>In force; immutable; what every calculation from its effective date reads.</summary>
    Published = 1,

    /// <summary>Superseded; immutable; still readable, because documents are pinned to it.</summary>
    Retired = 2,
}
