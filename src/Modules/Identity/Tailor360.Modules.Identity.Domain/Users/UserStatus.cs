namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// Where a staff account stands. There is no deletion: an account that must stop working is
/// deactivated, so the audit trail and every "who did this" reference stay resolvable
/// (conventions section 7).
/// </summary>
public enum UserStatus
{
    /// <summary>Created by an administrator and waiting for the holder to set a password.</summary>
    Invited = 0,

    /// <summary>Usable.</summary>
    Active = 1,

    /// <summary>Temporarily stopped by an administrator. Reversible.</summary>
    Suspended = 2,

    /// <summary>Stopped for good. Reinstating one starts the credentials again from scratch.</summary>
    Deactivated = 3,
}
