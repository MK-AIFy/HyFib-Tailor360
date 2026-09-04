using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;

namespace Tailor360.Platform.Persistence.Idempotency;

/// <summary>
/// Stores the outcome of an idempotent command. The claim is an insert that either wins or loses, so
/// two simultaneous submissions of the same order cannot both proceed; the loser either waits, replays
/// the stored response, or is rejected when it carries a different body under the same key.
/// </summary>
/// <param name="context">The platform context.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Retention options.</param>
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

        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO platform.idempotency_keys
                 (principal_id, route, client_key, request_fingerprint, status, created_at, expires_at)
             VALUES ({principalId}, {route}, {clientKey}, {requestFingerprint},
                     {IdempotencyStatuses.InProgress}, {now}, {now + options.Value.Retention})
             ON CONFLICT (principal_id, route, client_key) DO NOTHING
             """,
            cancellationToken);

        if (inserted == 1)
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

        return existing.Status == IdempotencyStatuses.Completed
            ? new IdempotencyClaim(IdempotencyOutcome.ReplayStoredResponse, existing.StatusCode, existing.ResponseBody)
            : new IdempotencyClaim(IdempotencyOutcome.InProgress);
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
                    completed_at = {clock.UtcNow}
              WHERE principal_id = {principalId}
                AND route = {route}
                AND client_key = {clientKey}
             """,
            cancellationToken);
    }

    /// <summary>
    /// A stable fingerprint of a request body, used to detect a client reusing one key for two different
    /// requests. Only the hash is stored, so the record never holds request content.
    /// </summary>
    public static string Fingerprint(string requestBody)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody ?? string.Empty)));

    /// <summary>Removes expired records. Run by the worker's retention job.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
        => await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.idempotency_keys WHERE expires_at < {clock.UtcNow}",
            cancellationToken);
}
