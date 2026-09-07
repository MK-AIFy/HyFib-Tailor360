using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>How the sign-in endpoint behaves in the ways an operator may need to tune.</summary>
public sealed class SignInTimingOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:SignIn";

    /// <summary>
    /// The floor a sign-in takes to answer, whichever way it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identical wording is necessary and not sufficient. The honest implementation does visibly
    /// different amounts of work on its branches — a known account loads its whole aggregate and, when
    /// the password is wrong, writes a failure count; an unknown one does neither — and that difference
    /// is a better account oracle than any message would have been, because it also says which accounts
    /// are currently locked out. The decoy verification equalises the largest term, one Argon2id
    /// derivation; this floor covers what is left.
    /// </para>
    /// <para>
    /// It is a mitigation and not a proof, and it works only while every branch stays comfortably under
    /// it. Set too low it silently does nothing, which is why the default leaves several times the
    /// measured cost of a derivation in hand, and why the rate limits and the progressive lockout —
    /// which bound how many samples an attacker can take — are what the guarantee actually rests on.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.1000000", "00:00:02")]
    public TimeSpan UniformResponseTime { get; set; } = TimeSpan.FromMilliseconds(400);
}
