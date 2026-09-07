using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>The stable code a request that ran out of time is answered with.</summary>
public static class RequestProblems
{
    /// <summary>504 — the server gave up on the request before it finished.</summary>
    public const string Timeout = "request.timeout";
}

/// <summary>
/// The request-timeout catalogue: how long the server is prepared to spend on one request before it
/// stops, so that a query nobody will ever see the answer to stops holding a thread and a database
/// connection from a budget of thirty.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the numbers come from.</b> A timeout is not a latency target; it is the point past which the
/// request is certainly broken rather than merely slow. Each is set an order of magnitude above the
/// objective it protects in <c>docs/nfr/slo.md</c> section 5 — reads p95 &lt; 400 ms, commands p95 &lt;
/// 800 ms, report and read-model queries p95 &lt; 1.5 s, export generation p95 &lt; 60 s — so a timeout
/// firing is a defect and never a busy afternoon. The service-level indicator is defined over them: a
/// <b>good</b> request is one answered below 500 <em>within the request timeout</em>, which is the
/// number this catalogue supplies. The values are <b>proposed, to be confirmed</b> alongside the
/// rate-limit numbers of issue #19.
/// </para>
/// <para>
/// <b>Why cancellation is enough to leave no half-applied state.</b> The timeout cancels
/// <c>HttpContext.RequestAborted</c>, every database call is made with that token, and a cancelled
/// <c>SaveChanges</c> rolls its transaction back. Nothing is written by a request that ran out of time —
/// which is also why a timed-out command's idempotency claim is left standing rather than released: the
/// outcome is unknown to the <em>caller</em>, and the safe answer to "try again" is "wait for the lease",
/// not "run it again".
/// </para>
/// </remarks>
public static class RequestTimeoutPolicies
{
    /// <summary>Safe reads. Protects the p95 &lt; 400 ms read objective (SLO S2).</summary>
    public const string Read = "read";

    /// <summary>State-changing requests. Protects the p95 &lt; 800 ms command objective (SLO S3).</summary>
    public const string Command = "command";

    /// <summary>Report and read-model queries, whose own objective is p95 &lt; 1.5 s.</summary>
    public const string Report = "report";

    /// <summary>Export generation, whose own objective is p95 &lt; 60 s.</summary>
    public const string Export = "export";

    /// <summary>How long a safe read may take.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a command may take. The idempotency lease must exceed this.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a report query may take.</summary>
    public static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long an export may take.</summary>
    public static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Registers the catalogue, with the command budget as the default so that an endpoint which
    /// declares nothing is still bounded.
    /// </summary>
    /// <remarks>
    /// A default is deliberate. The alternative — every endpoint must declare one — is how the rate-limit
    /// catalogue works, and it is right there because the wrong limit is a security decision. Here the
    /// wrong answer is an unbounded request, and an endpoint nobody remembered to annotate is exactly the
    /// one that will hang.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddTailor360RequestTimeouts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = Create(CommandTimeout);
            options.AddPolicy(Read, Create(ReadTimeout));
            options.AddPolicy(Command, Create(CommandTimeout));
            options.AddPolicy(Report, Create(ReportTimeout));
            options.AddPolicy(Export, Create(ExportTimeout));
        });

        return services;
    }

    /// <summary>
    /// Builds a policy of a given length that answers with the same problem document as the catalogue.
    /// </summary>
    /// <remarks>
    /// A host adding a timeout of its own — a media stream, a long import — uses this rather than
    /// constructing a policy directly, so that every timed-out request in the system answers the same
    /// way. The framework's default is a bare 504 with no body, which a client cannot distinguish from a
    /// reverse proxy giving up.
    /// </remarks>
    /// <param name="timeout">How long a request under this policy may take.</param>
    public static RequestTimeoutPolicy Create(TimeSpan timeout) => new()
    {
        Timeout = timeout,
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        WriteTimeoutResponse = WriteTimedOutAsync,
    };

    private static Task WriteTimedOutAsync(HttpContext context)
        => ProblemResults.RetryAfter(
            context,
            StatusCodes.Status504GatewayTimeout,
            RequestProblems.Timeout,
            "That took too long",
            "The server stopped waiting for this request, so nothing was changed. Try again.",
            TimeSpan.FromSeconds(1))
            .ExecuteAsync(context);
}
