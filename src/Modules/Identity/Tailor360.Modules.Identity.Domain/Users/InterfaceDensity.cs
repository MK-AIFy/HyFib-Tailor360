namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// How tightly the interface packs information. Comfortable suits touch on a phone or tablet; compact
/// suits a desk where a cashier wants more rows on screen at once.
/// </summary>
public enum InterfaceDensity
{
    /// <summary>Larger targets and more spacing.</summary>
    Comfortable = 0,

    /// <summary>Denser rows for pointer-driven work.</summary>
    Compact = 1,
}
