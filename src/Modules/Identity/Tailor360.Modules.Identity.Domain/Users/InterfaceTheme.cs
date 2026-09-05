namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// Which palette the interface uses. High contrast is a distinct choice rather than a variant of dark,
/// because it exists for reading a screen in direct sunlight at a shop counter as much as for a
/// vision need.
/// </summary>
public enum InterfaceTheme
{
    /// <summary>Follow whatever the device asks for.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,

    /// <summary>The high-contrast palette.</summary>
    HighContrast = 3,
}
