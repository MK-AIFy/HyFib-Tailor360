namespace Tailor360.Modules.Identity.Application.Abuse;

/// <summary>Which credential endpoint a throttle bucket belongs to.</summary>
/// <remarks>
/// They are counted separately because they are abused differently. Sign-in is sprayed across many
/// accounts from one address; a multi-factor challenge is hammered against one account whose password
/// is already known; a recovery request is used to send mail to someone else. One shared counter would
/// let the loudest of the three set the limit for all of them.
/// </remarks>
public enum CredentialAction
{
    /// <summary>Answering the first factor.</summary>
    SignIn = 0,

    /// <summary>Answering a multi-factor challenge, with an authenticator or a recovery code.</summary>
    MultiFactorChallenge = 1,

    /// <summary>Asking for a recovery link, or spending one.</summary>
    Recovery = 2,
}

/// <summary>What the throttle says about one attempt before it is made.</summary>
/// <param name="IsAllowed">False when the attempt must be refused without being evaluated.</param>
/// <param name="RetryAfter">
/// How long the caller must wait. Reported in <c>Retry-After</c>, which is deliberately honest: hiding
/// it would not stop an attacker, who simply retries, and it would leave a person who mistyped their
/// password twice with no idea whether to wait a second or a shift.
/// </param>
/// <param name="IsCaptchaRequired">
/// True once the attempts from this address look automated. It is a signal rather than a decision: the
/// endpoint acts on it only when a verifier is configured, and the hard limit applies either way.
/// </param>
public readonly record struct ThrottleDecision(
    bool IsAllowed,
    TimeSpan RetryAfter,
    bool IsCaptchaRequired)
{
    /// <summary>The decision when nothing about the attempt is suspicious.</summary>
    public static ThrottleDecision Allowed { get; } = new(true, TimeSpan.Zero, false);
}
