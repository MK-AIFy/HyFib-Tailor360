namespace Tailor360.Modules.Billing.Domain.Tax;

/// <summary>Whether a tax code classifies goods (an HSN code) or a service (a SAC code).</summary>
public enum TaxCodeKind
{
    /// <summary>Goods, classified by HSN.</summary>
    Goods = 0,

    /// <summary>A service such as stitching, classified by SAC.</summary>
    Services = 1,
}
