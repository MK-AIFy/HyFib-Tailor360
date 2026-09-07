namespace Tailor360.Modules.Identity.Domain.Sessions;

/// <summary>
/// Why a session stopped. The reason is kept because the session inventory shows it to the holder —
/// "signed out on your other device" and "an administrator ended this session" are different pieces of
/// news — and because the audit trail needs to distinguish routine sign-out from revocation.
/// </summary>
public enum SessionEndReason
{
    /// <summary>The holder signed out of this session.</summary>
    SignedOut = 0,

    /// <summary>The holder signed out of every session.</summary>
    SignedOutEverywhere = 1,

    /// <summary>
    /// The session was replaced by a new one at a privilege change — sign-in, multi-factor challenge,
    /// step-up, password change, multi-factor reset or role change — so that a ticket obtained before
    /// the change cannot be used after it.
    /// </summary>
    Rotated = 2,

    /// <summary>The holder revoked this session from the device inventory.</summary>
    RevokedByHolder = 3,

    /// <summary>An administrator revoked the session.</summary>
    RevokedByAdministrator = 4,

    /// <summary>The account's password was changed.</summary>
    PasswordChanged = 5,

    /// <summary>The account's second factor was reset.</summary>
    MfaReset = 6,

    /// <summary>The account was suspended or deactivated.</summary>
    AccountClosed = 7,

    /// <summary>Something about the request no longer matched the session.</summary>
    SuspiciousActivity = 8,
}
