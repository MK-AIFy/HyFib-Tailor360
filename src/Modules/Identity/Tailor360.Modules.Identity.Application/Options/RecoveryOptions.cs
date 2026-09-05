using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Identity.Domain.Recovery;

namespace Tailor360.Modules.Identity.Application.Options;

/// <summary>
/// How account recovery behaves: how long a link lives, where it points, and how long the endpoint
/// takes to answer whether or not the address is one it knows.
/// </summary>
public sealed class RecoveryOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Recovery";

    /// <summary>
    /// How long a recovery link lives. The domain caps this at an hour whatever is configured, because
    /// a reset link is a password with a timer on it.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "01:00:00")]
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The floor the recovery request takes to answer. It must comfortably exceed the work either path
    /// does — a lookup, a hash and an enqueue — so that the floor and not the work is what the caller
    /// measures. It must also stay inside the endpoint's latency budget, which is why it is a few
    /// hundred milliseconds and not a second.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.1000000", "00:00:02")]
    public TimeSpan UniformResponseTime { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The public origin of this deployment, for example <c>https://shop.example</c>. The recovery
    /// link is absolute, so this has to be the address a person's browser can reach — not the
    /// container's own. Leaving it unset produces a relative link and an error naming this key.
    /// </summary>
    /// <remarks>
    /// A value that is set is validated at start-up rather than at the moment somebody needs a reset:
    /// it must be an absolute URI, and it must be <c>https</c> unless the deployment has said in as many
    /// words that it may not be. What travels through this address is a token that sets a password —
    /// the strongest credential in the system for as long as it lives — and the shape an operator will
    /// copy when configuring staging is whatever development was left holding, so the plain-HTTP shape
    /// has to be refused rather than accepted quietly.
    /// </remarks>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// True when this deployment may serve the client over plain HTTP. It exists for a developer's
    /// loopback and for nothing else, which is why it defaults to refusing.
    /// </summary>
    public bool AllowInsecurePublicBaseUrl { get; set; }

    /// <summary>The client route that completes a password reset.</summary>
    [Required]
    [RegularExpression("^/[A-Za-z0-9/_-]*$")]
    public string ConfirmationPath { get; set; } = "/recovery/confirm";

    /// <summary>The client route that completes an invitation (#25 sends these).</summary>
    [Required]
    [RegularExpression("^/[A-Za-z0-9/_-]*$")]
    public string InvitationPath { get; set; } = "/invitation/accept";

    /// <summary>True when the configured lifetime is one the domain will accept.</summary>
    public bool IsUsable
        => TokenLifetime >= RecoveryToken.MinimumLifetime && TokenLifetime <= RecoveryToken.MaximumLifetime;

    /// <summary>
    /// True when the configured public origin is one a recovery link may safely travel to. An unset
    /// value passes here and is reported where it is used: a host that has nothing to do with recovery,
    /// such as the migration tool, must not be prevented from starting by a setting it never reads.
    /// </summary>
    public bool IsPublicBaseUrlUsable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PublicBaseUrl))
            {
                return true;
            }

            if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var origin))
            {
                return false;
            }

            return origin.Scheme == Uri.UriSchemeHttps
                || (AllowInsecurePublicBaseUrl && origin.Scheme == Uri.UriSchemeHttp);
        }
    }
}
