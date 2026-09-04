namespace Tailor360.Platform.Abstractions.Idempotency;

/// <summary>
/// Stores the outcome of an idempotent command so that a retry, a double tap or a replayed offline
/// request produces the first outcome instead of a second effect. Records are keyed by the triple
/// (principal, route, client key) and retained for at least twice the maximum offline-queue age (#53).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim the key for a new execution. Returns the stored outcome when the key was
    /// already used by the same principal on the same route.
    /// </summary>
    Task<IdempotencyClaim> ClaimAsync(
        string principalId,
        string route,
        string clientKey,
        string requestFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>Records the outcome of a successfully claimed execution.</summary>
    Task CompleteAsync(
        string principalId,
        string route,
        string clientKey,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of attempting to claim an idempotency key.</summary>
/// <param name="Outcome">Whether the caller may proceed, must replay a stored response, or conflicts.</param>
/// <param name="StatusCode">The stored status code when replaying.</param>
/// <param name="ResponseBody">The stored response body when replaying.</param>
public sealed record IdempotencyClaim(IdempotencyOutcome Outcome, int? StatusCode = null, string? ResponseBody = null)
{
    /// <summary>The caller holds the claim and must execute the command.</summary>
    public static IdempotencyClaim Proceed { get; } = new(IdempotencyOutcome.Proceed);
}

/// <summary>What the caller should do with an idempotency claim.</summary>
public enum IdempotencyOutcome
{
    /// <summary>The key is new; execute the command.</summary>
    Proceed,

    /// <summary>The key was used before with the same request; return the stored response.</summary>
    ReplayStoredResponse,

    /// <summary>The key was used before with a different request body; reject the request.</summary>
    KeyReuseConflict,

    /// <summary>A concurrent request holds the claim and has not finished.</summary>
    InProgress,
}
