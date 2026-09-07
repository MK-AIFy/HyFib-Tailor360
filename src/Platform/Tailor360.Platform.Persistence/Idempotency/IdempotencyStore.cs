using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Idempotency;

/// <summary>
/// Stores the outcome of an idempotent command. The claim is one statement that either wins or loses, so
/// two simultaneous submissions of the same order cannot both proceed; the loser either waits, replays
/// the stored response, or is rejected when it carries a different request under the same key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the claim is a single statement.</b> Reading the row and then inserting when it is absent has
/// a window between the two in which a second request reads the same nothing, and both proceed — which
/// on a payment endpoint is the one failure this whole mechanism exists to prevent. <c>INSERT … ON
/// CONFLICT</c> makes the decision inside PostgreSQL, where the row lock is, so exactly one caller can
/// ever be told to proceed.
/// </para>
/// <para>
/// <b>Why the same statement can take a claim over.</b> The conflict clause updates the row only when it
/// is an <em>expired</em> in-flight claim: a process that died mid-command left a row saying it was
/// working, and nothing else will ever come back to finish it. Taking it over is still one atomic
/// statement, so two requests arriving the moment a lease runs out do not both take it.
/// </para>
/// </remarks>
/// <param name="context">The platform context.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Retention and lease options.</param>
public sealed class IdempotencyStore(
    PlatformDbContext context,
    IClock clock,
    IOptions<IdempotencyOptions> options)
    : IIdempotencyStore
{
    /// <inheritdoc />
    public async Task<IdempotencyClaim> ClaimAsync(
        string principalId,
        string route,
        string clientKey,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);

        var now = clock.UtcNow;
        var leaseUntil = now + options.Value.InFlightLease;

        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO platform.idempotency_keys
                 (principal_id, route, client_key, request_fingerprint, status,
                  created_at, in_flight_until, expires_at)
             VALUES ({principalId}, {route}, {clientKey}, {requestFingerprint},
                     {IdempotencyStatuses.InProgress}, {now}, {leaseUntil}, {now + options.Value.Retention})
             ON CONFLICT (principal_id, route, client_key) DO UPDATE
                SET request_fingerprint = EXCLUDED.request_fingerprint,
                    status              = EXCLUDED.status,
                    created_at          = EXCLUDED.created_at,
                    in_flight_until     = EXCLUDED.in_flight_until,
                    expires_at          = EXCLUDED.expires_at,
                    status_code         = NULL,
                    response_body       = NULL,
                    completed_at        = NULL
              WHERE idempotency_keys.status = {IdempotencyStatuses.InProgress}
                AND idempotency_keys.in_flight_until IS NOT NULL
                AND idempotency_keys.in_flight_until < {now}
             """,
            cancellationToken);

        if (claimed == 1)
        {
            return IdempotencyClaim.Proceed;
        }

        var existing = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.PrincipalId == principalId && r.Route == route && r.ClientKey == clientKey,
                cancellationToken);

        if (existing is null)
        {
            // The row was removed by retention between the failed insert and this read, which means the
            // request is older than the retention window. Executing it again is the safe answer.
            return IdempotencyClaim.Proceed;
        }

        if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaim(IdempotencyOutcome.KeyReuseConflict);
        }

        if (existing.Status == IdempotencyStatuses.Completed)
        {
            return new IdempotencyClaim(
                IdempotencyOutcome.ReplayStoredResponse,
                existing.StatusCode,
                existing.ResponseBody);
        }

        var remaining = existing.InFlightUntil is { } until && until > now ? until - now : TimeSpan.Zero;
        return new IdempotencyClaim(IdempotencyOutcome.InProgress, RetryAfter: remaining);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string principalId,
        string route,
        string clientKey,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE platform.idempotency_keys
                SET status = {IdempotencyStatuses.Completed},
                    status_code = {statusCode},
                    response_body = {responseBody},
                    completed_at = {clock.UtcNow},
                    in_flight_until = NULL
              WHERE principal_id = {principalId}
                AND route = {route}
                AND client_key = {clientKey}
             """,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string principalId,
        string route,
        string clientKey,
        CancellationToken cancellationToken = default)
    {
        // Only an unfinished claim is given up. A completed record is the recorded answer to that key and
        // deleting it would let the same command run a second time.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM platform.idempotency_keys
              WHERE principal_id = {principalId}
                AND route = {route}
                AND client_key = {clientKey}
                AND status = {IdempotencyStatuses.InProgress}
             """,
            cancellationToken);
    }

    /// <summary>
    /// A stable fingerprint of a request body, used to detect a client reusing one key for two different
    /// requests. Only the hash is stored, so the record never holds request content.
    /// </summary>
    public static string Fingerprint(string requestBody) => RequestFingerprint.OfBody(requestBody);

    /// <summary>Removes expired records. Run by the worker's retention job.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
        => await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.idempotency_keys WHERE expires_at < {clock.UtcNow}",
            cancellationToken);
}
