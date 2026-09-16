using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Audit;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;

namespace Tailor360.Web.Telemetry;

/// <summary>
/// <c>POST /api/v1/telemetry/client</c> (#52): the server half of client telemetry — a same-origin,
/// anonymous, rate-limited endpoint a browser flushes batched, sampled events to. The browser module that
/// batches, samples and posts them is <c>[E12-F03-5]</c>; this maps the events it sends to structured
/// logs and a counter, through the allowlist that is this route's only defence against carrying anything
/// it should not.
/// </summary>
public static class ClientTelemetryEndpoints
{
    /// <summary>The route.</summary>
    public const string Path = "/api/v1/telemetry/client";

    /// <summary>The audit action a refused batch is recorded under. An accepted batch writes no row.</summary>
    public const string RefusedAction = "observability.client_telemetry_refused";

    /// <summary>The entity type a refusal is recorded against.</summary>
    private const string EntityType = "ClientTelemetryBatch";

    private const string ContentTypeErrorCode = "observability.telemetry-content-type-not-supported";
    private const string MalformedErrorCode = "observability.telemetry-malformed-batch";
    private const string OversizeErrorCode = "observability.telemetry-batch-oversize";
    private const string UnallowlistedErrorCode = "observability.telemetry-no-allowlisted-events";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the client telemetry endpoint.</summary>
    public static IEndpointRouteBuilder MapClientTelemetryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Path, HandleAsync)
            .AllowAnonymousWithJustification(
                "The browser flushes telemetry — unhandled errors, web vitals, capability detection — " +
                "before a session may exist: the unsupported-configuration page of [E12-F03-3] renders " +
                "before SessionProvider, and a batch flushed on page hide travels through " +
                "navigator.sendBeacon, which cannot attach the anti-forgery header. The same-origin " +
                "guarantee comes from UseTailor360OriginChecks() and this route's own declared-origin " +
                "filter, not from the session cookie. When a session does exist the cookie travels " +
                "anyway and the handler may correlate with it; it never requires it.",
                "#52")
            .WithoutAntiforgeryValidation(
                "navigator.sendBeacon cannot set a custom header, so a batch flushed on page hide could " +
                "never carry the anti-forgery token. This route's own declared-origin filter is the " +
                "compensating control anti-forgery would otherwise have provided.",
                "#52")
            .AddEndpointFilter<DeclaredOriginRequiredFilter>()
            .RequireRateLimiting(RateLimitPolicyNames.ClientTelemetryIngest)
            .Audited(RefusedAction)
            .WithName("IngestClientTelemetry")
            .WithSummary("Accept a batch of client telemetry events.")
            .WithDescription(
                "Same-origin, anonymous and rate-limited. Every event's type and every attribute name " +
                "and shape must be on the server-side allowlist or it is dropped before it reaches a " +
                "log or a metric; a batch left with nothing allowlisted is refused rather than silently " +
                "accepted. No table, no schema: an accepted event is a structured log record and a " +
                "counter increment, never a row.")
            .Produces<ClientTelemetryAcceptedResponse>(StatusCodes.Status202Accepted)
            .WithTags("Platform");

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IOptions<ClientTelemetryOptions> options,
        ClientTelemetryHandler handler,
        IAuditWriter audit,
        AuthorisationDenialCoalescer coalescer,
        ICurrentUser currentUser,
        IClock clock,
        IIdGenerator ids,
        ILogger<ClientTelemetryHandler> logger,
        CancellationToken cancellationToken)
    {
        var config = options.Value;

        if (!config.Enabled)
        {
            // A client never changes behaviour because an operator turned ingestion off: the same
            // acknowledgement as an accepted batch, and nothing is emitted.
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        if (!IsJson(context.Request.ContentType))
        {
            await RecordRefusalAsync(context, audit, coalescer, currentUser, clock, ids, logger, "content-type", null, cancellationToken);
            return ProblemResults.From(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                ContentTypeErrorCode,
                "Unsupported content type",
                "Send the batch as application/json.");
        }

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > config.MaxBodyBytes)
        {
            await RecordRefusalAsync(context, audit, coalescer, currentUser, clock, ids, logger, "oversize", null, cancellationToken);
            return ProblemResults.From(
                context,
                StatusCodes.Status413PayloadTooLarge,
                OversizeErrorCode,
                "Batch too large",
                $"The batch body must be at most {config.MaxBodyBytes} bytes.");
        }

        buffer.Position = 0;
        ClientTelemetryBatchRequest? batch;
        try
        {
            batch = await JsonSerializer.DeserializeAsync<ClientTelemetryBatchRequest>(buffer, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            batch = null;
        }

        if (batch is not { BatchId: { } batchId, Events.Count: > 0 })
        {
            await RecordRefusalAsync(context, audit, coalescer, currentUser, clock, ids, logger, "malformed", null, cancellationToken);
            return ProblemResults.From(
                context,
                StatusCodes.Status400BadRequest,
                MalformedErrorCode,
                "Malformed batch",
                "The batch must carry a batchId and at least one event.");
        }

        if (batch.Events!.Count > config.MaxEventsPerBatch)
        {
            await RecordRefusalAsync(context, audit, coalescer, currentUser, clock, ids, logger, "oversize", batchId, cancellationToken);
            return ProblemResults.From(
                context,
                StatusCodes.Status400BadRequest,
                OversizeErrorCode,
                "Batch too large",
                $"A batch may carry at most {config.MaxEventsPerBatch} events.");
        }

        var emitted = handler.Ingest(batch);

        if (emitted == 0)
        {
            await RecordRefusalAsync(context, audit, coalescer, currentUser, clock, ids, logger, "unallowlisted", batchId, cancellationToken);
            return ProblemResults.From(
                context,
                StatusCodes.Status400BadRequest,
                UnallowlistedErrorCode,
                "No allowlisted events",
                "None of this batch's events are of a type this server accepts.");
        }

        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    private static bool IsJson(string? contentType)
        => contentType is not null
            && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records a refusal, coalesced per actor, endpoint and minute exactly as
    /// <c>RateLimitPolicies.RecordAsync</c> does for a rate-limit rejection — reusing the same
    /// <see cref="AuthorisationDenialCoalescer"/>, so the two mechanisms agree on what "too many of the
    /// same refusal in one minute" means.
    /// </summary>
    private static async Task RecordRefusalAsync(
        HttpContext context,
        IAuditWriter audit,
        AuthorisationDenialCoalescer coalescer,
        ICurrentUser currentUser,
        IClock clock,
        IIdGenerator ids,
        ILogger logger,
        string reason,
        Guid? batchId,
        CancellationToken cancellationToken)
    {
        var signature = $"POST {Path}#{reason}";

        try
        {
            if (!coalescer.ShouldRecord(currentUser.PrincipalId, signature, clock.UtcNow))
            {
                return;
            }

            await audit.WriteAsync(
                new AuditEntry(
                    RefusedAction,
                    EntityType,

                    // The batch's own id when it parsed far enough to have one; otherwise a fresh
                    // identifier (ARCH-015), because the entry still needs one to be about.
                    batchId ?? ids.NewId(),
                    $"POST {Path} was refused: {reason}.",
                    ActorId: null),
                cancellationToken);

            await audit.SaveAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The refusal stands whatever happens here, the same discipline RateLimitPolicies.RecordAsync
            // follows: what must not happen is that it stands silently.
            logger.LogError(exception, "A refused client telemetry batch could not be recorded in the audit trail.");
        }
    }

}

/// <summary>
/// The body of an accepted batch: none. Declared only so the published document has a schema to point
/// to for <c>202</c> — the handler answers with no bytes, and the client must not wait on this response
/// or retry a refusal.
/// </summary>
public sealed record ClientTelemetryAcceptedResponse;
