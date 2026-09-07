using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tailor360.ContractTests;

/// <summary>
/// The per-endpoint half of ARCH-019: which authentication schemes each route's authorisation metadata
/// names.
/// </summary>
/// <remarks>
/// <para>
/// The registered-set assertion in <see cref="AuthenticationSchemeTests"/> is the stronger of the two
/// while exactly one scheme exists: no endpoint can name a second scheme that is not registered. This
/// detector is what keeps the rule true the day a second scheme <em>is</em> registered — for the
/// external surface at <c>/api/ext/v1/**</c> — because from that day the set assertion has to relax and
/// the per-endpoint one is all that is left.
/// </para>
/// <para>
/// It reads <see cref="IAuthorizeData.AuthenticationSchemes"/>, which is a comma-separated list, from
/// every piece of authorisation metadata on the endpoint. An endpoint whose policies between them name
/// two schemes accepts either credential, and accepting either means accepting the weaker.
/// </para>
/// </remarks>
public static class AuthenticationSchemeInspector
{
    /// <summary>Reads the schemes each route names.</summary>
    /// <param name="sources">The composed route table.</param>
    /// <returns>One row per route.</returns>
    public static IReadOnlyList<EndpointSchemes> Read(IEnumerable<EndpointDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return
        [
            .. sources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => new EndpointSchemes(
                    Describe(endpoint),
                    endpoint.RoutePattern.RawText ?? string.Empty,
                    SchemesOf(endpoint))),
        ];
    }

    /// <summary>Applies ARCH-019 to what the routes name.</summary>
    /// <param name="routes">The routes and the schemes each names.</param>
    /// <param name="browserScheme">The one scheme the browser surface may name.</param>
    /// <param name="browserPrefix">The route prefix the browser surface lives under.</param>
    /// <returns>One complaint per breach, empty when the rule holds.</returns>
    public static IReadOnlyList<string> Inspect(
        IReadOnlyList<EndpointSchemes> routes,
        string browserScheme,
        string browserPrefix)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var complaints = new List<string>();

        foreach (var route in routes)
        {
            if (route.Schemes.Count > 1)
            {
                complaints.Add(
                    $"{route.Endpoint} names {route.Schemes.Count} authentication schemes "
                    + $"({string.Join(", ", route.Schemes)}). An endpoint that accepts more than one "
                    + "accepts the weakest of them, and the weakness is invisible at the call site.");
            }

            if (!route.Route.StartsWith(browserPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var scheme in route.Schemes
                .Where(scheme => !string.Equals(scheme, browserScheme, StringComparison.Ordinal)))
            {
                complaints.Add(
                    $"{route.Endpoint} is on the browser surface and names '{scheme}'. "
                    + $"{browserPrefix}** accepts {browserScheme} only; an external client's "
                    + "credential belongs on /api/ext/v1/** and its own host composition.");
            }
        }

        return complaints;
    }

    private static IReadOnlyList<string> SchemesOf(RouteEndpoint endpoint) =>
    [
        .. endpoint.Metadata.OfType<IAuthorizeData>()
            .Select(data => data.AuthenticationSchemes)
            .Where(schemes => !string.IsNullOrWhiteSpace(schemes))
            .SelectMany(schemes => schemes!.Split(',', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }
}

/// <summary>The authentication schemes one route's authorisation metadata names.</summary>
/// <param name="Endpoint">The route as it reads in a failure message.</param>
/// <param name="Route">The raw route template, for the prefix test.</param>
/// <param name="Schemes">The distinct scheme names, in order.</param>
public sealed record EndpointSchemes(string Endpoint, string Route, IReadOnlyList<string> Schemes);
