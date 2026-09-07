using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tailor360.Web.Configuration;

/// <summary>
/// Configures forwarded headers so that the client address and scheme are taken from the reverse proxy
/// and from nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the trusted set is explicit.</b> <c>X-Forwarded-For</c> is a request header, which means it
/// is whatever the caller typed. Honouring it from an arbitrary source would let a caller choose its
/// own address and so give itself a private rate-limit partition per request, put somebody else's
/// address in the audit trail and in the abuse throttles, and defeat any address allowlist a provider
/// callback later relies on. The framework's defaults trust loopback, which is right for a sidecar
/// proxy and wrong for anything else, so they are cleared and re-stated deliberately.
/// </para>
/// <para>
/// <b>Empty means fail, not "trust nothing", outside Development.</b> A deployment that forgot to name
/// its proxy network would otherwise start happily and record the proxy's own address as every
/// caller's — one partition for the whole internet, and an audit trail that names the load balancer for
/// every action anybody takes. That is a silent, total loss of a control, so it is a start-up failure
/// instead: <c>components.md</c> stage 1 states the rule, and this is where it is enforced. In
/// Development, where requests arrive straight from the developer's own browser and there is no proxy
/// to name, the headers are simply ignored.
/// </para>
/// </remarks>
/// <param name="configuration">Application configuration.</param>
/// <param name="environment">The environment, which decides whether an empty trusted set is fatal.</param>
public sealed class ForwardedHeadersOptionsSetup(IConfiguration configuration, IHostEnvironment environment)
    : IConfigureOptions<ForwardedHeadersOptions>, IValidateOptions<ForwardedHeadersOptions>
{
    /// <summary>The configuration section listing trusted proxies and networks.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>What a deployment is told when it names no trusted proxy.</summary>
    public const string NoTrustedProxyMessage =
        "No reverse proxy is trusted for forwarded headers. Set ForwardedHeaders:KnownNetworks to the "
        + "reverse-proxy network (CIDR notation) or ForwardedHeaders:KnownProxies to its addresses. "
        + "Without one of them the client address is either the proxy's own — which puts every caller "
        + "in one rate-limit partition and names the proxy in the audit trail — or whatever the caller "
        + "chose to send, which is worse.";

    /// <inheritdoc />
    public void Configure(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // One hop. The deployment puts exactly one reverse proxy in front of this host, so a chain of
        // two forwarded addresses means somebody added one of them.
        options.ForwardLimit = 1;

        // Clear the framework defaults: they trust loopback, which is correct for a sidecar proxy but
        // must be re-stated deliberately rather than inherited.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        var section = configuration.GetSection(SectionName);

        foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
            }
        }

        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
        {
            // No trusted proxy configured: ignore forwarded headers entirely rather than trusting them.
            // Outside Development this state never survives validation, so this is what Development
            // runs with and what a failed start-up leaves behind.
            options.ForwardedHeaders = ForwardedHeaders.None;
        }
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        return options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0
            ? ValidateOptionsResult.Fail(NoTrustedProxyMessage)
            : ValidateOptionsResult.Success;
    }
}
