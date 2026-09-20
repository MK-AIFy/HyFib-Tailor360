namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// How large the interface renders text, independently of the device's own zoom. The one preference
/// checklist item A11Y-72 names for a person with presbyopia who has not zoomed the browser at all.
/// </summary>
public enum InterfaceTextSize
{
    /// <summary>100%.</summary>
    Standard = 0,

    /// <summary>125%.</summary>
    Large = 1,

    /// <summary>150%.</summary>
    Larger = 2,
}
