using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Audit;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Web.Configuration;

/// <summary>
/// The numbers behind the rate-limit policy catalogue. The names live in
/// <see cref="RateLimitPolicyNames"/>, where a module's endpoints can reach them; the limits live here,
/// because how much traffic a deployment tolerates is the host's decision and not a module's.
/// </summary>
/// <remarks>
/// <para>
/// One catalogue rather than per-endpoint numbers keeps the limits reviewable as a set. Each policy
/// describes a <em>shape</em> of traffic — an unauthenticated credential attempt, an ordinary read, a
/// burst of scans — so an endpoint chooses the shape it is and inherits whatever the shape currently
/// costs. Architecture rule ARCH-017 is what makes every endpoint choose one.
/// </para>
/// <para>
/// <b>Authenticated policies key on the account, not the address.</b> A whole shop reaches this system
/// through one broadband connection, so an address is a dozen people; limiting them as one caller would
/// mean the busiest person on a Saturday throttles everybody else. The credential policies key on the
/// address instead, because before a session exists there is nothing else to key on — and that is
/// exactly the traffic where one address really is one caller worth bounding.
/// </para>
/// <para>
/// This is one of three controls and the coarsest. It bounds requests; it does not know a wrong
/// password from a right one. The per-account throttle in the Identity module and the progressive
/// lockout on the account are what bound guessing, and neither is replaceable by tuning these numbers.
/// </para>
/// <para>
/// The numeric limits are <b>proposed, to be confirmed</b> by #19 against the section 2.4 rates in
/// <c>docs/nfr/capacity-and-performance.md</c>; the catalogue's shape is what ARCH-017 fixes.
/// </para>
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>The audit action a refused request is recorded under.</summary>
    public const string AuditAction = "security.rate-limited";

    /// <summary>
    /// The policies whose rejections are written to the audit trail.
    /// </summary>
    /// <remarks>
    /// These are the endpoints where being pushed at is itself the attack: guessing a password, guessing
    /// a one-time code, harvesting recovery messages. A rejection there is worth a durable record, so
    /// that "somebody spent an evening on this" is answerable months later from the trail rather than
    /// from log retention. Ordinary traffic policies are deliberately absent: a busy shop hitting the
    /// read limit is a capacity fact, and recording it would put operational noise in an append-only
    /// table. The customer-link policy joins this set with #47.
    /// </remarks>
    public static readonly IReadOnlySet<string> AuditedPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        RateLimitPolicyNames.AuthenticationAnonymous,
        RateLimitPolicyNames.MultiFactorChallenge,
        RateLimitPolicyNames.RecoveryAnonymous,
    };

    /// <summary>
    /// The catalogue: every policy this host registers, with the traffic shape it describes and the
    /// numbers that shape currently costs.
    /// </summary>
    /// <remarks>
    /// It is a list rather than a sequence of registration calls so that the set is readable as a set,
    /// and so that the contract test behind ARCH-017 can check an endpoint's declared policy against
    /// what the host actually registers. A name an endpoint declares but this list omits is not a
    /// mis-spelling a reviewer might catch: it is an endpoint with no limiter at all, which the rate
    /// limiting middleware discovers by throwing on the first request that reaches it.
    /// </remarks>
    public static IReadOnlyList<RateLimitPolicyDefinition> Catalogue { get; } =
    [
        // Sign-in and passkey assertion. Strict, and keyed on the address: nobody is signed in yet.
        new(RateLimitPolicyNames.AuthenticationAnonymous, 10, TimeSpan.FromMinutes(1), KeyedOnAccount: false),

        // Answering a second factor. Keyed on the account, because the caller has already proved a
        // password and two people on one counter connection must not throttle each other.
        new(RateLimitPolicyNames.MultiFactorChallenge, 20, TimeSpan.FromMinutes(1), KeyedOnAccount: true),

        // Asking for or spending a recovery link. Tighter than sign-in, because each accepted request
        // may send a message to somebody who did not ask for it.
        new(RateLimitPolicyNames.RecoveryAnonymous, 5, TimeSpan.FromMinutes(15), KeyedOnAccount: false),

        // Ordinary authenticated traffic. A progressive web application issues several requests per
        // interaction, so the read limit is generous by design.
        new(RateLimitPolicyNames.DefaultUser, 300, TimeSpan.FromMinutes(1), KeyedOnAccount: true),
        new(RateLimitPolicyNames.Write, 120, TimeSpan.FromMinutes(1), KeyedOnAccount: true),

        // The shop floor scans in bursts as a rack is worked through, so this is high and
        // short-windowed rather than low and long-windowed.
        new(RateLimitPolicyNames.ScanBurst, 240, TimeSpan.FromMinutes(1), KeyedOnAccount: true),

        // Report and export generation, which is expensive per call.
        new(RateLimitPolicyNames.ExportHeavy, 10, TimeSpan.FromMinutes(5), KeyedOnAccount: true),

        // Anonymous traffic that is not a credential endpoint: the application shell itself, the version
        // probe the shell reads before sign-in, the anti-forgery token, and the catch-alls that answer a
        // mistyped path. A shop is one address, and every device on it makes all four of those requests
        // when it starts, so this window has to hold a shift's worth of devices starting together —
        // hence a limit that only a loop could reach, not one a Monday morning could.
        new(RateLimitPolicyNames.DefaultIp, 120, TimeSpan.FromMinutes(1), KeyedOnAccount: false),
    ];

    /// <summary>Registers every policy in the catalogue and the shared rejection behaviour.</summary>
    public static RateLimiterOptions AddTailor360Policies(this RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = RejectAsync;

        foreach (var policy in Catalogue)
        {
            Add(options, policy);
        }

        return options;
    }

    /// <summary>
    /// Answers a refused request: the wait it must obey, an RFC 9457 document saying why, and — for the
    /// credential policies — an entry in the audit trail.
    /// </summary>
    /// <remarks>
    /// The framework's own rejection is a bare 429 with no body, which the client cannot distinguish
    /// from any other refusal without special-casing the status. Answering with the same problem
    /// envelope as everything else means the progressive web application shows "too many attempts, try
    /// again in a minute" from the <c>code</c> and <c>retryAfterSeconds</c> it already reads.
    /// </remarks>
    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? metadata
            : TimeSpan.FromMinutes(1);

        var problem = ProblemResults.RetryAfter(
            httpContext,
            StatusCodes.Status429TooManyRequests,
            "rate.limited",
            "Too many requests",
            "This request was refused because too many have arrived in a short time. Wait for the "
            + "period given and try again.",
            retryAfter);

        await RecordAsync(httpContext);
        await problem.ExecuteAsync(httpContext);
    }

    /// <summary>
    /// Records a refused credential request, coalesced per actor, endpoint and minute so that a flood
    /// cannot fill an append-only table.
    /// </summary>
    private static async Task RecordAsync(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        var policy = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        if (policy is null || !AuditedPolicies.Contains(policy))
        {
            return;
        }

        var services = httpContext.RequestServices;
        var route = endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? httpContext.Request.Path.Value ?? "/"
            : "(unrouted)";

        // The template, never the request path: a path carries identifiers the caller chose, and this
        // string is a key a query groups by. The status prefix keeps this event in its own namespace,
        // so a refusal here and an authorisation denial at the same endpoint in the same minute are two
        // records rather than one.
        var signature = $"429 {httpContext.Request.Method} {route}";
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimitPolicies));

        try
        {
            var clock = services.GetRequiredService<IClock>();
            var currentUser = services.GetRequiredService<ICurrentUser>();
            var coalescer = services.GetRequiredService<AuthorisationDenialCoalescer>();

            if (!coalescer.ShouldRecord(currentUser.PrincipalId, signature, clock.UtcNow))
            {
                return;
            }

            var writer = services.GetService<IAuditWriter>();
            if (writer is null)
            {
                return;
            }

            await writer.WriteAsync(new AuditEntry(
                AuditAction,
                AuthorisationDenialAuditingHandler.EntityType,
                AuthorisationDenialAuditingHandler.IdentifierFor(signature),
                $"{httpContext.Request.Method} {route} was refused by the '{policy}' rate-limit policy.",

                // Named here rather than left to the audit context: a refused sign-in has no
                // authenticated actor for the context to have resolved, and attributing it to "system"
                // would put an attacker's attempts in the same bucket as the scheduler's.
                ActorId: currentUser.IsAuthenticated ? currentUser.UserId : null,
                ActorDisplayName: currentUser.DisplayName));

            await writer.SaveAsync(httpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The refusal stands whatever happens here. What must not happen is that it stands silently.
            logger.LogError(
                exception,
                "A refused request at {Endpoint} could not be recorded in the audit trail.",
                signature);
        }
    }

    private static void Add(RateLimiterOptions options, RateLimitPolicyDefinition policy)
        => options.AddPolicy(policy.Name, context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context, policy),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.Permits,
                Window = policy.Window,

                // Nothing queues. A caller past the limit is told so immediately with a Retry-After it
                // can obey; holding the request open instead would consume a connection to deliver the
                // same answer later.
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    private static string PartitionKey(HttpContext context, RateLimitPolicyDefinition policy)
    {
        if (policy.KeyedOnAccount
            && context.RequestServices.GetService(typeof(ICurrentUser)) is ICurrentUser { IsAuthenticated: true } user)
        {
            return $"{policy.Name}:u:{user.PrincipalId}";
        }

        // Falls back to the remote address, which the forwarded-headers configuration has already
        // resolved through the trusted reverse proxy only.
        return $"{policy.Name}:ip:{context.Connection.RemoteIpAddress}";
    }
}

/// <summary>One policy in the rate-limit catalogue.</summary>
/// <param name="Name">The name an endpoint declares, from <see cref="RateLimitPolicyNames"/>.</param>
/// <param name="Permits">How many requests one partition may make in a window.</param>
/// <param name="Window">The fixed window the permits are counted in.</param>
/// <param name="KeyedOnAccount">
/// True when the partition is the signed-in account, falling back to the client address when there is
/// none; false when it is always the address, which is the only key a credential endpoint has.
/// </param>
public sealed record RateLimitPolicyDefinition(
    string Name,
    int Permits,
    TimeSpan Window,
    bool KeyedOnAccount);
