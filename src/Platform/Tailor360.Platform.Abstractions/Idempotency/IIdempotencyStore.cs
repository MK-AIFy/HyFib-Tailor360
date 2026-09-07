namespace Tailor360.Platform.Abstractions.Idempotency;

/// <summary>
/// Stores the outcome of an idempotent command so that a retry, a double tap or a replayed offline
/// request produces the first outcome instead of a second effect. Records are keyed by the triple
/// (principal, route, client key) and retained for at least twice the maximum offline-queue age (#53).
/// </summary>
/// <remarks>
/// <para>
/// <b>A claim is a lease, not a flag.</b> Claiming writes a row that says "I am running this now, and I
/// expect to be finished by then". A process that dies mid-command therefore blocks the key until its
/// lease runs out and no longer, which is the difference between a payment that can be retried in half a
/// minute and one that is stuck for the whole seven-day retention window.
/// </para>
/// <para>
/// <b>Authentication and authorisation run before the lookup</b> (plan Section 4.4). A replay presented
/// by a revoked or unauthorised principal is refused, never served from the store.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim the key for a new execution. Returns the stored outcome when the key was
    /// already used by the same principal on the same route, and takes the claim over when the previous
    /// holder's lease has expired.
    /// </summary>
    /// <param name="principalId">The caller, so that one person's key cannot collide with another's.</param>
    /// <param name="route">The route template the key was used on, including the method.</param>
    /// <param name="clientKey">The value the client sent in <c>Idempotency-Key</c>.</param>
    /// <param name="requestFingerprint">The request hash from <see cref="RequestFingerprint"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IdempotencyClaim> ClaimAsync(
        string principalId,
        string route,
        string clientKey,
        string requestFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the outcome of a successfully claimed execution, which is what a later retry replays.
    /// </summary>
    /// <remarks>
    /// Call this before the response reaches the network. A client whose connection dropped after the
    /// command committed must find the outcome waiting for it, and a record written after the write to
    /// the socket would be missing in exactly the case it exists for.
    /// </remarks>
    Task CompleteAsync(
        string principalId,
        string route,
        string clientKey,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives up a claim without recording an outcome, so the key is free for the client to use again.
    /// </summary>
    /// <remarks>
    /// This is for a request that is known to have changed nothing — a refusal the caller can correct
    /// and resend under the same key, which is what the design system's conflict and re-authentication
    /// flows do. It never removes a completed record: an outcome that has been reported is the answer to
    /// that key for as long as the record is retained.
    /// </remarks>
    Task ReleaseAsync(
        string principalId,
        string route,
        string clientKey,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of attempting to claim an idempotency key.</summary>
/// <param name="Outcome">Whether the caller may proceed, must replay a stored response, or conflicts.</param>
/// <param name="StatusCode">The stored status code when replaying.</param>
/// <param name="ResponseBody">The stored response body when replaying.</param>
/// <param name="RetryAfter">How long the first attempt's lease still has to run, when one is in flight.</param>
public sealed record IdempotencyClaim(
    IdempotencyOutcome Outcome,
    int? StatusCode = null,
    string? ResponseBody = null,
    TimeSpan? RetryAfter = null)
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
