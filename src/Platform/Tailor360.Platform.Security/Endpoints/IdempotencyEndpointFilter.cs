using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Makes a command safe to retry: the same <c>Idempotency-Key</c> produces the first outcome, never a
/// second effect.
/// </summary>
/// <remarks>
/// <para>
/// <b>The case this exists for.</b> A cashier takes a payment on a counter tablet, the shop's connection
/// drops before the response arrives, and the client retries. Without this the customer is charged
/// twice; with it the retry is answered with the receipt the first attempt produced. Everything below
/// follows from wanting that to be true even when the process dies, the request times out or two
/// attempts arrive at once.
/// </para>
/// <para>
/// <b>It runs after authorisation.</b> An endpoint filter executes inside the endpoint, so the
/// authentication and authorisation middleware have already answered (plan Section 4.4). A replay
/// presented by a revoked or unauthorised principal is refused before the record is ever read.
/// </para>
/// <para>
/// <b>What binds a key, and what does not.</b> Only a response that may have changed something — a
/// <c>2xx</c> or a <c>3xx</c> — is recorded and replayed. A <c>4xx</c> releases the claim, because it
/// changed nothing and because the client is expected to correct it and send it again <em>under the
/// same key</em>: that is exactly what the design system's conflict flow and the in-place
/// re-authentication of Section 4.4 do, and a stored refusal would replay itself forever and make both
/// impossible. A <c>5xx</c>, an unhandled exception and a request timeout leave the claim standing,
/// because their outcome is unknown and running the command again is the one thing that must not happen
/// on a guess; the claim's lease expires on its own and the key becomes usable again.
/// </para>
/// <para>
/// <b>The order of the last two steps is the whole point.</b> The response is rendered into a buffer,
/// the record is written, and only then are the bytes put on the wire. Recording after the write would
/// leave no record in precisely the case this exists for — the connection that died as the answer was
/// being sent.
/// </para>
/// <para>
/// <b>The response is held in memory while that happens</b>, which is fine for a receipt or a
/// confirmation and wrong for a stream. An endpoint that returns a file or a large export is not a
/// command, does not declare this, and uses a concurrency precondition instead.
/// </para>
/// <para>
/// <b>What it does not close.</b> A process that dies between committing the command and writing the
/// record leaves a key that will be re-executed once its lease expires. That window is microseconds
/// wide and cannot be closed from outside the command's own transaction, so a command that moves money
/// carries its own transactional guard as well — a unique index on the natural key of the effect, which
/// is what the payment-intent design of plan Section 4.4 relies on. See
/// <c>docs/platform/idempotency-and-concurrency.md</c>.
/// </para>
/// </remarks>
/// <param name="store">The record store.</param>
/// <param name="currentUser">The caller, which is part of the record key.</param>
/// <param name="options">How long a duplicate waits.</param>
/// <param name="timeProvider">Measures the wait, so a test does not have to spend it.</param>
/// <param name="logger">Records the two conditions that mean the host is misconfigured.</param>
public sealed class IdempotencyEndpointFilter(
    IIdempotencyStore store,
    ICurrentUser currentUser,
    IOptions<IdempotencyRequestOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyEndpointFilter> logger)
    : IEndpointFilter
{
    /// <summary>The longest route key the record column holds.</summary>
    private const int RouteLimit = 256;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;

        if (!TryReadKey(http, out var clientKey, out var refusal))
        {
            return refusal;
        }

        if (!currentUser.IsAuthenticated)
        {
            // An idempotent command is a command, and a command is permissioned; reaching here anonymous
            // means the endpoint declared idempotency without declaring a permission. Refusing is the
            // fail-closed answer, and the log line names the defect.
            logger.LogError(
                "Endpoint {Endpoint} requires an idempotency key but admitted an unauthenticated caller. "
                + "Declare RequirePermission on it, or do not declare RequireIdempotency.",
                http.GetEndpoint()?.DisplayName ?? "(unknown)");

            return ProblemResults.From(
                http,
                StatusCodes.Status401Unauthorized,
                AuthorisationProblemResultHandler.AuthenticationRequiredCode,
                "Sign in to continue",
                "This request needs a signed-in session. Sign in and try again.");
        }

        var principalId = currentUser.PrincipalId;
        var route = RouteKeyOf(http);

        var fingerprint = await FingerprintAsync(http);

        if (fingerprint is null)
        {
            logger.LogError(
                "Endpoint {Endpoint} requires an idempotency key but its request body could not be read "
                + "a second time. Call UseTailor360IdempotencyKeys() after UseRouting().",
                http.GetEndpoint()?.DisplayName ?? "(unknown)");

            return ProblemResults.From(
                http,
                StatusCodes.Status500InternalServerError,
                IdempotencyProblems.Unavailable,
                "That could not be completed",
                "This request could not be made safe to retry, so it was not carried out. Try again.",
                retryable: true);
        }

        var claim = await ClaimAsync(http, principalId, route, clientKey, fingerprint);

        switch (claim.Outcome)
        {
            case IdempotencyOutcome.ReplayStoredResponse:
                return Replay(http, claim);

            case IdempotencyOutcome.KeyReuseConflict:
                return ProblemResults.From(
                    http,
                    StatusCodes.Status422UnprocessableEntity,
                    IdempotencyProblems.KeyReused,
                    "That request has already been used",
                    "This request repeats a key that was used for a different request. Start the action "
                    + "again so it gets a key of its own.");

            case IdempotencyOutcome.InProgress:
                return ProblemResults.RetryAfter(
                    http,
                    StatusCodes.Status409Conflict,
                    IdempotencyProblems.InProgress,
                    "That is already being carried out",
                    "An earlier attempt at this action is still running. Wait a moment and try again; it "
                    + "will not be carried out twice.",
                    claim.RetryAfter ?? TimeSpan.FromSeconds(1));

            default:
                return await ExecuteAndRecordAsync(
                    context, next, principalId, route, clientKey, claim.LeaseUntil);
        }
    }

    /// <summary>
    /// Claims the key, waiting out a first attempt that is still running rather than refusing a duplicate
    /// that is about to become a replay.
    /// </summary>
    private async Task<IdempotencyClaim> ClaimAsync(
        HttpContext http,
        string principalId,
        string route,
        string clientKey,
        string fingerprint)
    {
        var claim = await store.ClaimAsync(principalId, route, clientKey, fingerprint, http.RequestAborted);

        if (claim.Outcome is not IdempotencyOutcome.InProgress)
        {
            return claim;
        }

        var budget = options.Value.DuplicateWaitBudget;
        var interval = options.Value.DuplicatePollInterval;
        var start = timeProvider.GetTimestamp();

        while (timeProvider.GetElapsedTime(start) < budget)
        {
            await Task.Delay(interval, timeProvider, http.RequestAborted);

            // Re-claiming rather than re-reading: if the first attempt's lease ran out while this request
            // was waiting, this one takes the claim over and runs, which is the right answer and one a
            // plain read could not give.
            claim = await store.ClaimAsync(principalId, route, clientKey, fingerprint, http.RequestAborted);

            if (claim.Outcome is not IdempotencyOutcome.InProgress)
            {
                return claim;
            }
        }

        return claim;
    }

    /// <summary>
    /// Runs the command, renders its response into a buffer, records the outcome, and only then writes
    /// the bytes to the connection.
    /// </summary>
    /// <param name="leaseUntil">
    /// The lease this request's claim was granted under. It is presented back to the store so that a
    /// request whose lease ran out — and whose claim another request has since taken over — cannot
    /// overwrite the successor's record with its own stale outcome, nor delete the successor's claim.
    /// </param>
    private async Task<object?> ExecuteAndRecordAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        string principalId,
        string route,
        string clientKey,
        DateTimeOffset? leaseUntil)
    {
        var http = context.HttpContext;
        var response = http.Response;
        var connection = response.Body;

        using var buffer = new MemoryStream();
        response.Body = buffer;

        try
        {
            await WriteResultAsync(await next(context), http);
        }
        finally
        {
            // Restored before anything else can throw: a filter that left the response pointed at a
            // discarded buffer would silently drop every later write on this request.
            response.Body = connection;
        }

        var status = response.StatusCode;
        var body = BodyToRecord(buffer, response.ContentType);

        // Deliberately not the request's cancellation token. A client that has already gone away is the
        // case the record exists for, and losing the record because the connection died would leave the
        // next attempt to run the command a second time.
        if (status is >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest)
        {
            await store.CompleteAsync(
                principalId, route, clientKey, status, body, leaseUntil, CancellationToken.None);
        }
        else if (status < StatusCodes.Status500InternalServerError)
        {
            await store.ReleaseAsync(principalId, route, clientKey, leaseUntil, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(connection, http.RequestAborted);

        return Results.Empty;
    }

    /// <summary>Executes whatever the handler returned, exactly as the framework would have.</summary>
    private static async Task WriteResultAsync(object? result, HttpContext http)
    {
        switch (result)
        {
            case null:
                return;
            case IResult typed:
                await typed.ExecuteAsync(http);
                return;
            case string text:
                await Results.Text(text).ExecuteAsync(http);
                return;
            default:
                await Results.Json(result).ExecuteAsync(http);
                return;
        }
    }

    /// <summary>
    /// Reads the rendered response back, when it is a body worth replaying.
    /// </summary>
    /// <remarks>
    /// Only JSON is kept. A command answers with a JSON document or with nothing, and storing bytes of an
    /// unknown type would mean replaying them without knowing how to label them.
    /// </remarks>
    private static string? BodyToRecord(MemoryStream buffer, string? contentType)
    {
        if (buffer.Length == 0
            || contentType is null
            || !contentType.StartsWith(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static IResult Replay(HttpContext http, IdempotencyClaim claim)
    {
        http.Response.Headers[IdempotencyHeaders.Replayed] = "true";

        var status = claim.StatusCode ?? StatusCodes.Status200OK;

        return string.IsNullOrEmpty(claim.ResponseBody)
            ? Results.StatusCode(status)
            : Results.Content(claim.ResponseBody, MediaTypeNames.Application.Json, Encoding.UTF8, status);
    }

    private static bool TryReadKey(HttpContext http, out string clientKey, out IResult? refusal)
    {
        clientKey = string.Empty;
        refusal = null;

        var header = http.Request.Headers[IdempotencyHeaders.Key].ToString();

        if (header.Length == 0)
        {
            refusal = ProblemResults.From(
                http,
                StatusCodes.Status400BadRequest,
                IdempotencyProblems.KeyRequired,
                "That request could not be accepted",
                "This action needs an Idempotency-Key header so that a retry cannot carry it out twice.");

            return false;
        }

        if (!Guid.TryParse(header, out var parsed))
        {
            refusal = ProblemResults.From(
                http,
                StatusCodes.Status400BadRequest,
                IdempotencyProblems.KeyInvalid,
                "That request could not be accepted",
                "The Idempotency-Key header must be a UUID generated by the client for this one action.");

            return false;
        }

        clientKey = parsed.ToString("D");
        return true;
    }

    /// <summary>
    /// The record's route key: the method and the route <em>template</em>, which is what an operator
    /// reads and what the store's key column is sized for.
    /// </summary>
    private static string RouteKeyOf(HttpContext http)
    {
        var template = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? http.Request.Path.Value
            ?? "/";

        var key = $"{http.Request.Method} {template}";

        if (key.Length <= RouteLimit)
        {
            return key;
        }

        // Truncating alone would let two long routes share one record, and a shared record is a replayed
        // response for a command nobody sent. The digest keeps the head readable and the tail unambiguous.
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return string.Concat(key.AsSpan(0, RouteLimit - digest.Length - 1), "#", digest);
    }

    /// <summary>
    /// Hashes the request, or returns null when the body cannot be read a second time. The resolved path
    /// is part of the hash, so one key used on two different orders of the same route is caught rather
    /// than answered with the first order's response.
    /// </summary>
    /// <remarks>
    /// The copy is asynchronous because it has to be: a host forbids synchronous reads of a request body
    /// that has not been buffered yet, which is every request whose handler declares no body parameter.
    /// </remarks>
    private static async Task<string?> FingerprintAsync(HttpContext http)
    {
        var request = http.Request;

        if (!request.Body.CanSeek)
        {
            // Nothing to re-read only if there was nothing to read. Anything else means the buffering
            // step is missing from the pipeline, and guessing at a fingerprint would silently weaken the
            // guarantee rather than fail.
            return request.ContentLength is > 0
                ? null
                : RequestFingerprint.Of(
                    request.Method, request.Path.Value ?? "/", request.QueryString.Value ?? string.Empty, []);
        }

        request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, http.RequestAborted);
        request.Body.Position = 0;

        return RequestFingerprint.Of(
            request.Method,
            request.Path.Value ?? "/",
            request.QueryString.Value ?? string.Empty,
            body.GetBuffer().AsSpan(0, (int)body.Length));
    }
}
