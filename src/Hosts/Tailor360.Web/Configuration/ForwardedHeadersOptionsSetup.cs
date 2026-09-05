using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tailor360.Web.Configuration;

/// <summary>
/// Configures forwarded headers so that the client address and scheme are taken from the reverse proxy
/// and from nowhere else. Trusting forwarded headers from an arbitrary source would let a caller choose
/// its own address, which would in turn defeat the address-partitioned rate limits and pollute the
/// audit trail, so the trusted set is explicit and empty by default.
/// </summary>
/// <param name="configuration">Application configuration.</param>
public sealed class ForwardedHeadersOptionsSetup(IConfiguration configuration)
    : IConfigureOptions<ForwardedHeadersOptions>
{
    /// <summary>The configuration section listing trusted proxies and networks.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <inheritdoc />
    public void Configure(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
            options.ForwardedHeaders = ForwardedHeaders.None;
        }
    }
}
