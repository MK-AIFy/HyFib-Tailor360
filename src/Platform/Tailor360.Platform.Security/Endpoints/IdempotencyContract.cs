using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>The headers the idempotency contract is carried in.</summary>
public static class IdempotencyHeaders
{
    /// <summary>The client-generated UUID that names one attempt at one command.</summary>
    public const string Key = "Idempotency-Key";

    /// <summary>
    /// Set to <c>true</c> on a response served from the record rather than by running the command, so a
    /// client — and anyone reading a capture — can tell a replay from a first execution.
    /// </summary>
    public const string Replayed = "Idempotency-Replayed";
}

/// <summary>The stable codes the idempotency contract answers with.</summary>
/// <remarks>
/// They are published in the problem-type registry that ships with the API documentation, and the
/// progressive web application branches on them, so they are as much a part of the contract as the
/// status codes beside them.
/// </remarks>
public static class IdempotencyProblems
{
    /// <summary>400 — the endpoint requires <c>Idempotency-Key</c> and the request carried none.</summary>
    public const string KeyRequired = "idempotency.key-required";

    /// <summary>400 — the key was present but is not a UUID.</summary>
    public const string KeyInvalid = "idempotency.key-invalid";

    /// <summary>422 — the key has already been used for a different request.</summary>
    public const string KeyReused = "idempotency.key-reused";

    /// <summary>409 — an earlier attempt with this key is still running.</summary>
    public const string InProgress = "idempotency.in-progress";

    /// <summary>
    /// 500 — the request could not be made safe to retry, so it was not carried out. It means the host is
    /// missing the buffering step, never anything the caller did.
    /// </summary>
    public const string Unavailable = "idempotency.unavailable";
}

/// <summary>
/// Marks an endpoint as requiring an idempotency key, for the endpoint inventory, the generated
/// specification and the architecture tests.
/// </summary>
public sealed record IdempotentEndpointMetadata;

/// <summary>How the endpoint filter behaves when it finds a duplicate.</summary>
public sealed class IdempotencyRequestOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Idempotency:Requests";

    /// <summary>
    /// How long a duplicate waits for the first attempt to finish before being told to come back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five seconds is the plan's number (Section 4.4), and the reason it is not zero is the shape of the
    /// duplicate this mostly catches: a counter tablet on a slow connection where somebody pressed
    /// <em>Take payment</em> twice. The first attempt is usually a few hundred milliseconds from
    /// finishing, and waiting turns the second press into the same receipt rather than into an error the
    /// cashier has to interpret in front of a customer.
    /// </para>
    /// <para>
    /// The reason it is not longer is that a request holding a connection open is holding a thread, a
    /// database connection from a budget of thirty, and a person's attention. Beyond a few seconds,
    /// "try again in a moment" is both more honest and cheaper than waiting.
    /// </para>
    /// <para>
    /// The wait is spent <em>inside</em> the duplicate's own request, so it comes out of that request's
    /// timeout budget. It must stay well under the shortest timeout any idempotent endpoint declares, or
    /// a duplicate would be answered with a timeout instead of the conflict it was waiting to be told
    /// about. Five seconds against a thirty-second command budget leaves ample room.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "00:00:30")]
    public TimeSpan DuplicateWaitBudget { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the waiting duplicate re-reads the record.</summary>
    [Range(typeof(TimeSpan), "00:00:00.020", "00:00:02")]
    public TimeSpan DuplicatePollInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}
