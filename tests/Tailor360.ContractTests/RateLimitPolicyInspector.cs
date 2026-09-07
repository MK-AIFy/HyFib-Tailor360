using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Tailor360.Platform.Observability.Health;

namespace Tailor360.ContractTests;

/// <summary>
/// The detector behind ARCH-017: what rate-limit policy, if any, each published route declares.
/// </summary>
/// <remarks>
/// <para>
/// It is a separate type from the test so that the same code can be pointed at the composed route table
/// and at a handful of synthetic endpoints. A rule whose detector is only ever run against a route table
/// that satisfies it proves nothing: it would pass just as happily if it had stopped looking.
/// </para>
/// <para>
/// The rule is "exactly one", not "at least one". Two policies on one endpoint is not twice the
/// protection: the middleware applies the one it finds, so the second is a declaration somebody wrote
/// and nothing enforces — and which of the two is dead depends on metadata order, which is not a fact an
/// author controls.
/// </para>
/// </remarks>
public static class RateLimitPolicyInspector
{
    /// <summary>Reads what each route declares.</summary>
    /// <param name="endpoints">The endpoints to inspect.</param>
    /// <returns>One row per route.</returns>
    public static IReadOnlyList<EndpointRateLimits> Read(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return
        [
            .. endpoints.Select(endpoint => new EndpointRateLimits(
                Describe(endpoint),
                [
                    .. endpoint.Metadata.OfType<EnableRateLimitingAttribute>()
                        .Select(attribute => attribute.PolicyName ?? "(an unnamed policy instance)"),
                ],
                endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null,
                endpoint.Metadata.GetMetadata<HealthProbeMetadata>() is not null)),
        ];
    }

    /// <summary>Applies ARCH-017 to what the routes declare.</summary>
    /// <param name="routes">The routes and the policies each declares.</param>
    /// <param name="catalogue">The policy names the host registers limiters for.</param>
    /// <returns>One complaint per breach, empty when the rule holds.</returns>
    public static IReadOnlyList<string> Inspect(
        IReadOnlyList<EndpointRateLimits> routes,
        IReadOnlySet<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(catalogue);

        var complaints = new List<string>();

        foreach (var route in routes)
        {
            if (route.DisablesRateLimiting)
            {
                complaints.Add(
                    $"{route.Endpoint} declares DisableRateLimiting. The exemption ARCH-017 allows is "
                    + "the health probes, which are recognised by their own metadata; opting an "
                    + "ordinary route out of the limiter is an unlimited endpoint whichever verb "
                    + "spells it.");

                continue;
            }

            // The documented exception: the reverse proxy and the external uptime check call these at a
            // fixed cadence, and a probe that is throttled reports the instance down.
            if (route.IsHealthProbe && route.Policies.Count == 0)
            {
                continue;
            }

            if (route.Policies.Count == 0)
            {
                complaints.Add(
                    $"{route.Endpoint} declares no rate-limit policy. Add "
                    + ".RequireRateLimiting(RateLimitPolicyNames.…) naming the shape of traffic it is; "
                    + "an endpoint with none is an unmetered one.");

                continue;
            }

            if (route.Policies.Count > 1)
            {
                complaints.Add(
                    $"{route.Endpoint} declares {route.Policies.Count} rate-limit policies "
                    + $"({string.Join(", ", route.Policies)}). Only one of them is applied, and which "
                    + "one depends on metadata order rather than on anything the author chose.");
            }

            foreach (var policy in route.Policies.Where(policy => !catalogue.Contains(policy)))
            {
                complaints.Add(
                    $"{route.Endpoint} declares '{policy}', which the host registers no limiter for. "
                    + "The rate limiting middleware throws on the first request that reaches such an "
                    + "endpoint, so this is a 500 waiting for traffic, not a naming slip. Add the "
                    + "policy to RateLimitPolicies.Catalogue or declare one that is already there.");
            }
        }

        return complaints;
    }

    private static string Describe(Endpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        var route = endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : endpoint.DisplayName;

        return $"{string.Join('|', methods)} {route}".TrimStart();
    }
}

/// <summary>What one route declares about rate limiting.</summary>
/// <param name="Endpoint">The route as it reads in a failure message.</param>
/// <param name="Policies">The policy names its metadata declares, in the order they appear.</param>
/// <param name="DisablesRateLimiting">True when the route opts out of the limiter altogether.</param>
/// <param name="IsHealthProbe">True for the probes ARCH-017 exempts.</param>
public sealed record EndpointRateLimits(
    string Endpoint,
    IReadOnlyList<string> Policies,
    bool DisablesRateLimiting,
    bool IsHealthProbe);
