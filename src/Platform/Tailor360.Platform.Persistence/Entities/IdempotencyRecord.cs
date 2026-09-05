namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// The recorded outcome of an idempotent command. Keyed by principal, route and client key together:
/// keying by the client key alone would let one user's replayed key collide with another's, and keying
/// without the route would make one key silently cover two different commands.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>The caller who first used the key.</summary>
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>The route the key was used on.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>The key the client supplied.</summary>
    public string ClientKey { get; set; } = string.Empty;

    /// <summary>
    /// A hash of the request. A repeat with the same key but a different body is a client defect and is
    /// rejected rather than answered with the earlier result.
    /// </summary>
    public string RequestFingerprint { get; set; } = string.Empty;

    /// <summary>Whether the first execution is still running or has completed.</summary>
    public string Status { get; set; } = IdempotencyStatuses.InProgress;

    /// <summary>The status code the first execution returned.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The body the first execution returned, replayed to later callers.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>When the key was first claimed.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the first execution finished.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When the record may be deleted. Retention is at least twice the maximum age of a queued offline
    /// request, so a scan captured on a device that stayed offline over a weekend still de-duplicates
    /// when it finally arrives.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>The states an idempotency record can be in.</summary>
public static class IdempotencyStatuses
{
    /// <summary>A first execution is running.</summary>
    public const string InProgress = "in_progress";

    /// <summary>The first execution completed and its outcome is stored.</summary>
    public const string Completed = "completed";
}
