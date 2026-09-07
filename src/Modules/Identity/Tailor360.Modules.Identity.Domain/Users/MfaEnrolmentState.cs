namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>
/// How far the account has got with its second factor. This records what the account <em>has</em>;
/// whether it <em>must</em> have one is a separate question answered from the holder's effective
/// permissions (#24), because a role change can make multi-factor mandatory for an account that was
/// enrolled voluntarily yesterday.
/// </summary>
public enum MfaEnrolmentState
{
    /// <summary>No second factor.</summary>
    NotEnrolled = 0,

    /// <summary>An authenticator secret has been issued but no valid code has confirmed it yet.</summary>
    PendingConfirmation = 1,

    /// <summary>At least one confirmed second factor.</summary>
    Enrolled = 2,

    /// <summary>
    /// An administrator reset the second factor after an out-of-band identity check. The holder must
    /// enrol again at the next sign-in; the reset itself never grants access.
    /// </summary>
    ResetRequired = 3,
}
