using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Persistence.Idempotency;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.ContractTests;

/// <summary>
/// An idempotent command finishes inside the lease its claim was granted under.
/// </summary>
/// <remarks>
/// <para>
/// The claim lease is what stops a dead process holding a key for the whole retention window: once it
/// runs out, the next request takes the claim over. That is right when the first holder is gone and
/// wrong when it is merely slow — two requests then execute the same command at once, and no amount of
/// fencing on the store prevents it, because both executions are real. The invariant is that a command
/// cannot outlive its lease, and the only place it can be checked is where the two are paired: the
/// endpoint.
/// </para>
/// <para>
/// It was previously asserted as one global comparison, against the command timeout while being named
/// for the longest — and the catalogue holds a 60-second report timeout and a 120-second export timeout
/// that the 45-second lease does not outlive. Raising the lease past the longest would be the wrong fix,
/// because the lease is also how long a crashed payment blocks its key at a counter. Bounding the
/// pairing per endpoint keeps both: a command-shaped endpoint is safe on the default, and one that
/// reaches for a longer timeout fails here rather than in production.
/// </para>
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointRequestSafetyTests(WebHostFixture fixture)
{
    [Fact]
    public void AnIdempotentEndpointFinishesInsideItsClaimLease()
    {
        var lease = Lease();
        var complaints = Inspect(Endpoints(), lease, TimeoutsByPolicy());

        complaints.ShouldBeEmpty(
            $"A command that can outrun its {lease.TotalSeconds:0}-second claim lease can be started a "
            + "second time while the first is still running:\n" + string.Join('\n', complaints));
    }

    /// <summary>Negative control: an endpoint pairing idempotency with a longer timeout is caught.</summary>
    [Fact]
    public void TheDetectorCatchesATimeoutThatOutlivesTheLease()
    {
        var endpoint = Route(
            "/api/v1/slow-command",
            new IdempotentEndpointMetadata(),
            new RequestTimeoutAttribute(RequestTimeoutPolicies.Export));

        var complaints = Inspect([endpoint], Lease(), TimeoutsByPolicy());

        complaints.ShouldHaveSingleItem().ShouldContain(RequestTimeoutPolicies.Export);
    }

    /// <summary>Negative control: an inline timeout longer than the lease is caught too.</summary>
    [Fact]
    public void TheDetectorCatchesAnInlineTimeoutThatOutlivesTheLease()
    {
        var endpoint = Route(
            "/api/v1/slow-inline",
            new IdempotentEndpointMetadata(),
            new RequestTimeoutAttribute((int)TimeSpan.FromMinutes(5).TotalMilliseconds));

        var complaints = Inspect([endpoint], Lease(), TimeoutsByPolicy());

        complaints.ShouldHaveSingleItem().ShouldContain("00:05:00");
    }

    /// <summary>
    /// Positive control: the detector is not simply complaining about everything. An idempotent endpoint
    /// on the command timeout passes, and one that declares no timeout at all is not this rule's
    /// business — the default policy covers it, and ARCH-017's neighbour asserts that separately.
    /// </summary>
    [Fact]
    public void TheDetectorAcceptsACommandShapedEndpoint()
    {
        Endpoint[] endpoints =
        [
            Route("/api/v1/pay", new IdempotentEndpointMetadata(),
                new RequestTimeoutAttribute(RequestTimeoutPolicies.Command)),
            Route("/api/v1/undeclared", new IdempotentEndpointMetadata()),
            Route("/api/v1/read", new RequestTimeoutAttribute(RequestTimeoutPolicies.Export)),
        ];

        Inspect(endpoints, Lease(), TimeoutsByPolicy()).ShouldBeEmpty();
    }

    /// <summary>One complaint per endpoint whose timeout can outrun the lease.</summary>
    private static List<string> Inspect(
        IReadOnlyList<Endpoint> endpoints,
        TimeSpan lease,
        Dictionary<string, TimeSpan> timeouts)
    {
        var complaints = new List<string>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IdempotentEndpointMetadata>() is null)
            {
                continue;
            }

            var declared = endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>();
            if (declared is null)
            {
                continue;
            }

            var (name, timeout) = declared.Timeout is { } inline
                ? (inline.ToString(), (TimeSpan?)inline)
                : (declared.PolicyName ?? "(none)",
                   timeouts.TryGetValue(declared.PolicyName ?? string.Empty, out var known)
                       ? known
                       : null);

            if (timeout is null)
            {
                complaints.Add(
                    $"{Describe(endpoint)} declares idempotency and the timeout policy '{name}', which "
                    + "the host registers no timeout for.");
                continue;
            }

            if (timeout >= lease)
            {
                complaints.Add(
                    $"{Describe(endpoint)} declares idempotency and a timeout of '{name}' "
                    + $"({timeout.Value.TotalSeconds:0}s), which is not shorter than the "
                    + $"{lease.TotalSeconds:0}-second claim lease. A slow request would have its claim "
                    + "taken over while it was still running, and the command would execute twice.");
            }
        }

        return complaints;
    }

    private static string Describe(Endpoint endpoint)
        => endpoint is RouteEndpoint route ? route.RoutePattern.RawText ?? endpoint.DisplayName ?? "?"
            : endpoint.DisplayName ?? "?";

    /// <summary>The lease the host is configured with, not the type's default.</summary>
    private TimeSpan Lease()
    {
        using var scope = fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<IdempotencyOptions>>()
            .Value.InFlightLease;
    }

    /// <summary>The catalogue's policies, by the name an endpoint declares.</summary>
    private static Dictionary<string, TimeSpan> TimeoutsByPolicy() => new(StringComparer.Ordinal)
    {
        [RequestTimeoutPolicies.Read] = RequestTimeoutPolicies.ReadTimeout,
        [RequestTimeoutPolicies.Command] = RequestTimeoutPolicies.CommandTimeout,
        [RequestTimeoutPolicies.Report] = RequestTimeoutPolicies.ReportTimeout,
        [RequestTimeoutPolicies.Export] = RequestTimeoutPolicies.ExportTimeout,
    };

    private static RouteEndpoint Route(string pattern, params object[] metadata) => new(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(pattern),
        order: 0,
        new EndpointMetadataCollection([new HttpMethodMetadata(["POST"]), .. metadata]),
        pattern);

    private List<Endpoint> Endpoints()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return [.. sources.SelectMany(source => source.Endpoints)];
    }
}
