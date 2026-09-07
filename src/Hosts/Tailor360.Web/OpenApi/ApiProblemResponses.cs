namespace Tailor360.Web.OpenApi;

/// <summary>
/// The error responses every operation publishes, and the rule that decides which of them apply.
/// </summary>
/// <remarks>
/// <para>
/// They are declared once as components and referenced from each operation, rather than repeated per
/// endpoint. Repetition is how a document starts telling ninety slightly different stories about the
/// same failure, and it is also how the committed document becomes too large to review as a diff.
/// </para>
/// <para>
/// The set is derived from what the endpoint declares, not applied uniformly, because a documented
/// response that cannot occur is as misleading as a missing one: an anonymous endpoint has no 403 to
/// give, and an operation with no route parameter has nothing to fail to find. An endpoint that answers
/// a status outside its derived set — a command that answers <c>409</c> on a version conflict, say —
/// declares that status on itself, and this transformer leaves the declaration alone.
/// </para>
/// </remarks>
public static class ApiProblemResponses
{
    /// <summary>The component name of the shared correlation-identifier response header.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>The component name of the shared retry hint on a throttled response.</summary>
    public const string RetryAfterHeader = "Retry-After";

    /// <summary>Every error response the document declares as a reusable component.</summary>
    public static IReadOnlyList<ApiProblemResponse> Catalogue { get; } =
    [
        new(
            400,
            "BadRequest",
            "The request was not valid. The body is problem details with per-field `errors` when the "
            + "failure is a validation failure."),
        new(
            401,
            "Unauthorized",
            "There is no live session, or the one presented has expired or been revoked. The client "
            + "re-authenticates in place and retries the request with the same `Idempotency-Key`."),
        new(
            403,
            "Forbidden",
            "The caller holds a session but not the permission, the branch reach or the session "
            + "assurance this operation requires. The refusal is audited."),
        new(
            404,
            "NotFound",
            "There is no such resource, or none this caller may see. The two are deliberately "
            + "indistinguishable, so that the identifier space cannot be probed."),
        new(
            415,
            "UnsupportedMediaType",
            "The request body was not `application/json`."),
        new(
            426,
            "UpgradeRequired",
            "The client build declared in `X-Client-Version` is older than the minimum this server "
            + "supports. Reloading collects the current build; repeating the request does not help. "
            + "The body carries `minimumClient` and `current`."),
        new(
            429,
            "TooManyRequests",
            "The rate-limit policy this operation declares refused the request. `Retry-After` says when "
            + "it is worth trying again.",
            RetryAfter: true),
        new(
            500,
            "InternalServerError",
            "The request failed unexpectedly. The body carries the problem type and the correlation "
            + "identifier and nothing else — never a stack trace, never an exception message."),
    ];

    /// <summary>
    /// The statuses an operation must publish, given what its endpoint declares.
    /// </summary>
    /// <param name="requiresAuthorisation">True when the endpoint demands a session.</param>
    /// <param name="hasPathParameter">True when the route names a resource.</param>
    /// <param name="hasRequestBody">True when the operation accepts a body.</param>
    /// <param name="honoursClientVersion">
    /// True when the client-version handshake can refuse this operation. False for the handshake's own
    /// endpoint, which is exempt from it: a client that has been refused reads the versions it needs
    /// from there, so refusing it too would leave the refusal unexplainable.
    /// </param>
    /// <returns>The applicable statuses, ascending.</returns>
    public static IReadOnlyList<int> Applicable(
        bool requiresAuthorisation,
        bool hasPathParameter,
        bool hasRequestBody,
        bool honoursClientVersion = true)
    {
        var statuses = new List<int> { 400 };

        if (requiresAuthorisation)
        {
            // 401 and 403 answer two different questions — "who are you?" and "you, specifically, may
            // not" — and a client that cannot tell them apart either loops on a re-authentication that
            // will never help, or shows "access denied" to somebody whose session merely expired.
            statuses.Add(401);
            statuses.Add(403);
        }

        if (hasPathParameter)
        {
            statuses.Add(404);
        }

        if (hasRequestBody)
        {
            statuses.Add(415);
        }

        if (honoursClientVersion)
        {
            statuses.Add(426);
        }

        statuses.Add(429);
        statuses.Add(500);

        return statuses;
    }
}

/// <summary>One reusable error response.</summary>
/// <param name="Status">The HTTP status it describes.</param>
/// <param name="ComponentName">The name it is declared under in <c>components.responses</c>.</param>
/// <param name="Description">What the caller should understand from receiving it.</param>
/// <param name="RetryAfter">True when the response also carries a <c>Retry-After</c> header.</param>
public sealed record ApiProblemResponse(
    int Status,
    string ComponentName,
    string Description,
    bool RetryAfter = false);
