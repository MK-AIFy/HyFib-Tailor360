namespace Tailor360.Modules.Identity.Application.Sessions;

/// <summary>
/// Why a session is being replaced by a new one with a new cookie value. Every entry is a moment at
/// which what the holder may do changes, which is exactly when a ticket obtained before the change
/// must stop working.
/// </summary>
/// <remarks>
/// Rotation is the answer to session fixation. An attacker who plants a known session value in
/// someone's browser — through a cookie-tossing subdomain, a shared machine, a link — is betting that
/// the value they know will still be the value in use after that person signs in. Replacing it at
/// sign-in, and again at every step that raises what the session can do, means the value they planted
/// is revoked before it is worth anything.
/// </remarks>
public enum SessionRotationReason
{
    /// <summary>The holder completed the first factor. Any ticket presented beforehand is discarded.</summary>
    SignedIn = 0,

    /// <summary>A second factor was satisfied, so the session may now reach what multi-factor gates.</summary>
    MultiFactorSatisfied = 1,

    /// <summary>The holder re-authenticated for a step-up action.</summary>
    SteppedUp = 2,

    /// <summary>The password changed.</summary>
    PasswordChanged = 3,

    /// <summary>The second factor was reset, so the session's multi-factor state no longer holds.</summary>
    MultiFactorReset = 4,

    /// <summary>The holder's roles or permissions changed.</summary>
    RolesChanged = 5,
}

/// <summary>Which rotations count as proving a strong factor.</summary>
public static class SessionRotationReasonExtensions
{
    /// <summary>
    /// True when the rotation is itself a strong authentication, which restarts the step-up freshness
    /// window. A password change is deliberately not one: section 4.4 defines step-up as multi-factor
    /// re-authentication, and treating a password alone as strong would let anyone who found an
    /// unattended signed-in session approve a financially final action by changing the password.
    /// </summary>
    public static bool ProvesStrongAuthentication(this SessionRotationReason reason)
        => reason is SessionRotationReason.MultiFactorSatisfied or SessionRotationReason.SteppedUp;

    /// <summary>The audit action name recorded for a rotation.</summary>
    public static string AuditAction(this SessionRotationReason reason) => reason switch
    {
        SessionRotationReason.SignedIn => "identity.session.signed-in",
        SessionRotationReason.MultiFactorSatisfied => "identity.session.multi-factor-satisfied",
        SessionRotationReason.SteppedUp => "identity.session.stepped-up",
        SessionRotationReason.PasswordChanged => "identity.session.password-changed",
        SessionRotationReason.MultiFactorReset => "identity.session.multi-factor-reset",
        SessionRotationReason.RolesChanged => "identity.session.roles-changed",
        _ => "identity.session.rotated",
    };
}
