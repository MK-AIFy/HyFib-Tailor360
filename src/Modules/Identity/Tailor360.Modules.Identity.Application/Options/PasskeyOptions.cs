using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// The relying party a WebAuthn ceremony runs against, and how long a challenge stays answerable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The relying-party identifier is the whole security property.</b> A browser signs only for the
/// origin it is registered against, which is what makes a passkey phishing-resistant: a look-alike
/// domain gets no signature at all, however convincing it is to the person looking at it. Getting
/// <see cref="RelyingPartyId"/> wrong therefore does not weaken the control, it disables it — the
/// browser simply refuses — so it is left unset by default and the endpoints report themselves
/// unavailable rather than guessing a domain from the request, which is exactly what an attacker
/// controlling the <c>Host</c> header would want them to do.
/// </para>
/// <para>
/// <see cref="Origins"/> must list the full origins staff reach the application on, scheme included.
/// </para>
/// </remarks>
public sealed class PasskeyOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Passkeys";

    /// <summary>
    /// The registrable domain credentials are bound to, for example <c>shop.example</c>. Empty turns
    /// passkeys off.
    /// </summary>
    [StringLength(253)]
    public string RelyingPartyId { get; set; } = string.Empty;

    /// <summary>The name an authenticator shows when it asks the holder to confirm.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string RelyingPartyName { get; set; } = "HyFib Tailor 360";

    /// <summary>The full origins the application is served from, scheme included.</summary>
    public IList<string> Origins { get; } = [];

    /// <summary>
    /// How long a challenge stays answerable. Long enough to find a security key in a drawer, short
    /// enough that a challenge captured from a screen is worthless by the time it is used.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "00:10:00")]
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How many bytes of entropy a challenge carries. The library defaults to sixteen; thirty-two is
    /// set here because a challenge is the only thing standing between a replayed assertion and a
    /// sign-in, and the cost of the extra bytes is nothing.
    /// </summary>
    [Range(16, 64)]
    public int ChallengeBytes { get; set; } = 32;

    /// <summary>True when enough is configured for a ceremony to run at all.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(RelyingPartyId) && Origins.Count > 0;
}
