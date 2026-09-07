using Microsoft.AspNetCore.Http;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>The stable codes the optimistic-concurrency contract answers with.</summary>
public static class ConcurrencyProblems
{
    /// <summary>428 — the endpoint requires <c>If-Match</c> and the request carried none.</summary>
    /// <remarks>
    /// 428 rather than 400 is recorded decision <b>COD-05</b> in
    /// <c>docs/architecture/conventions.md</c>: a client can tell "you forgot the precondition", which it
    /// fixes by re-reading and resending, from "your payload is wrong", which it cannot.
    /// </remarks>
    public const string PreconditionRequired = "concurrency.if-match-required";

    /// <summary>400 — the <c>If-Match</c> header was present but is not a strong entity tag.</summary>
    public const string PreconditionMalformed = "concurrency.if-match-invalid";
}

/// <summary>
/// The <c>ETag</c> and <c>If-Match</c> half of the concurrency contract in
/// <c>docs/architecture/conventions.md</c> section 4.2.
/// </summary>
/// <remarks>
/// <para>
/// The rule the contract exists for is "last write wins" being unacceptable: two people editing one
/// customer's measurements from two counters must not have one of them silently overwrite the other. The
/// aggregate's <c>xmin</c> travels to the client as an <c>ETag</c>, comes back as <c>If-Match</c>, and a
/// mismatch is a 409 that carries the current version so the client can show both values and let the
/// person choose — never a 409 that just says no.
/// </para>
/// </remarks>
public static class ConcurrencyResults
{
    /// <summary>Publishes an aggregate's version on a response, for the client to send back.</summary>
    /// <param name="response">The response.</param>
    /// <param name="tag">The aggregate's current entity tag.</param>
    public static void SetEntityTag(this HttpResponse response, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ETag = tag.Value;
    }

    /// <summary>Reads the request's <c>If-Match</c> header.</summary>
    /// <param name="request">The request.</param>
    /// <param name="tag">The parsed tag when the header carried a strong entity tag.</param>
    /// <returns>False when the header is absent, or present but not a strong entity tag.</returns>
    public static bool TryGetIfMatch(this HttpRequest request, out EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EntityTag.TryParse(request.Headers.IfMatch.ToString(), out tag);
    }

    /// <summary>
    /// Checks a request's precondition against the version the aggregate actually carries, and returns
    /// the refusal to answer with, or null when the caller may proceed.
    /// </summary>
    /// <remarks>
    /// This is the whole contract in one call, so that no endpoint has to reimplement the three-way
    /// answer — missing, malformed, stale — and get one of them subtly wrong.
    /// </remarks>
    /// <param name="context">The request.</param>
    /// <param name="current">The version the stored aggregate carries now.</param>
    /// <param name="conflictCode">The module's conflict code, for example <c>orders.version-conflict</c>.</param>
    /// <param name="conflictDetail">What the person should do, written for them.</param>
    public static IResult? CheckIfMatch(
        HttpContext context,
        EntityTag current,
        string conflictCode,
        string conflictDetail)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictCode);

        var header = context.Request.Headers.IfMatch.ToString();

        if (header.Length == 0)
        {
            return PreconditionMissing(context);
        }

        if (!EntityTag.TryParse(header, out var presented))
        {
            return ProblemResults.From(
                context,
                StatusCodes.Status400BadRequest,
                ConcurrencyProblems.PreconditionMalformed,
                "That request could not be accepted",
                "The If-Match header must be the ETag this record was last read with, in double quotes.");
        }

        return presented.Matches(current)
            ? null
            : VersionConflict(context, conflictCode, conflictDetail, current);
    }

    /// <summary>Answers a request that omitted a required <c>If-Match</c>.</summary>
    /// <param name="context">The request.</param>
    public static IResult PreconditionMissing(HttpContext context)
        => ProblemResults.From(
            context,
            StatusCodes.Status428PreconditionRequired,
            ConcurrencyProblems.PreconditionRequired,
            "Reload before saving",
            "This record can be changed by more than one person, so a change has to say which version it "
            + "was made against. Reload the record and try again.");

    /// <summary>
    /// Answers a stale update with the current version, so the client can show both values rather than
    /// discarding what the person typed.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="code">The module's conflict code, for example <c>orders.version-conflict</c>.</param>
    /// <param name="detail">What happened and what to do, written for the person on the screen.</param>
    /// <param name="current">The version the stored aggregate carries now.</param>
    public static IResult VersionConflict(HttpContext context, string code, string detail, EntityTag current)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The tag goes on the response as well as in the body: a client that re-reads uses the header,
        // and a client that merges in place uses the member. Sending only one of them makes one of those
        // two clients do a second round trip for something we already knew.
        context.Response.SetEntityTag(current);

        return ProblemResults.From(
            context,
            StatusCodes.Status409Conflict,
            code,
            "This record changed since you opened it",
            detail,
            retryable: false,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["currentVersion"] = current.Version,
                ["currentEtag"] = current.Value,
            });
    }
}
