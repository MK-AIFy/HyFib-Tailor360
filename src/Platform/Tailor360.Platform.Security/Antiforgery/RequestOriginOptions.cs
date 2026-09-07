using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>Which origins a state-changing request may claim to come from.</summary>
public sealed class RequestOriginOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Security:RequestOrigin";

    /// <summary>
    /// Origins accepted in addition to the one the request was addressed to. Normally empty: the client
    /// is served from the same origin as the API, which is the whole point of a backend-for-frontend.
    /// An entry here is a deliberate exception — a separate host for the print station, say — and each
    /// one is an absolute origin such as <c>https://station.example.com</c>, never a bare host and
    /// never a wildcard.
    /// </summary>
    public IList<string> AdditionalAllowedOrigins { get; } = [];

    /// <summary>
    /// Whether a state-changing request that declares neither <c>Origin</c> nor <c>Sec-Fetch-Site</c>
    /// is refused. Off by default: every browser that can run the client sends at least one of them, but
    /// the command-line tool and the health probes do not, and anti-forgery validation already stands
    /// behind this check. Turn it on in a deployment where nothing but a browser calls the API.
    /// </summary>
    [Required]
    public bool RequireDeclaredOrigin { get; set; }
}
