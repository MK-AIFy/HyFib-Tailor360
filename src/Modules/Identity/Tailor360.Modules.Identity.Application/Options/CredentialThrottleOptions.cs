using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Application.Abuse;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// How many credential attempts one account and one client address may make inside a window, per
/// endpoint.
/// </summary>
/// <remarks>
/// The per-address numbers are the larger of the two on purpose. A whole shop reaches this system
/// through one broadband connection, so an address is often a dozen people rather than one; the
/// per-account number is what actually bounds an attack on a named account, and the address number is
/// there to bound a spray across many of them.
/// </remarks>
public sealed class CredentialThrottleOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Throttle";

    /// <summary>Sign-in attempts.</summary>
    public CredentialThrottleRule SignIn { get; } = new()
    {
        PerAccount = 10,
        PerClient = 40,
        Window = TimeSpan.FromMinutes(5),
        CaptchaAfter = 15,
    };

    /// <summary>Multi-factor challenge answers.</summary>
    public CredentialThrottleRule MultiFactorChallenge { get; } = new()
    {
        PerAccount = 10,
        PerClient = 30,
        Window = TimeSpan.FromMinutes(5),
        CaptchaAfter = 0,
    };

    /// <summary>Recovery requests and confirmations.</summary>
    public CredentialThrottleRule Recovery { get; } = new()
    {
        PerAccount = 5,
        PerClient = 15,
        Window = TimeSpan.FromMinutes(15),
        CaptchaAfter = 8,
    };

    /// <summary>The rule for one endpoint.</summary>
    public CredentialThrottleRule For(CredentialAction action) => action switch
    {
        CredentialAction.MultiFactorChallenge => MultiFactorChallenge,
        CredentialAction.Recovery => Recovery,
        _ => SignIn,
    };

    /// <summary>True when every rule is usable.</summary>
    public bool IsUsable => SignIn.IsUsable && MultiFactorChallenge.IsUsable && Recovery.IsUsable;
}

/// <summary>One endpoint's counting rule.</summary>
public sealed class CredentialThrottleRule
{
    /// <summary>How many attempts one account tolerates inside the window.</summary>
    [Range(1, 1000)]
    public int PerAccount { get; set; } = 10;

    /// <summary>How many attempts one client address tolerates inside the window.</summary>
    [Range(1, 10000)]
    public int PerClient { get; set; } = 40;

    /// <summary>The window both counters are measured over.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many attempts from one address before a human check is asked for, when one is configured.
    /// Zero turns the signal off for this endpoint, which is right where a challenge is answered by
    /// someone who has already proved a password.
    /// </summary>
    [Range(0, 10000)]
    public int CaptchaAfter { get; set; }

    /// <summary>True when the numbers describe a control rather than switch it off.</summary>
    public bool IsUsable
        => PerAccount >= 1 && PerClient >= PerAccount && Window > TimeSpan.Zero;
}
