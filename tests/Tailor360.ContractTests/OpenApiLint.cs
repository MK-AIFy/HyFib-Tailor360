using System.Globalization;
using Microsoft.OpenApi;
using Tailor360.Web.OpenApi;

namespace Tailor360.ContractTests;

/// <summary>
/// The lint the published API document has to pass, expressed as a detector over the document object
/// model so that it can be run against a deliberately broken document as well as against the real one.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the ones plan section 5.1 names for Spectral: every operation declares its security
/// requirements, publishes the problem-details responses it can answer with, and carries at least one
/// example. They are implemented here as well as declared in <c>.spectral.yaml</c> so that the gate runs
/// in the contract tier with no network and no toolchain outside the .NET SDK — a gate that can only run
/// when a package registry is reachable is a gate that gets skipped on the day it matters.
/// <c>docs/api/openapi-gates.md</c> records the relationship between the two.
/// </para>
/// <para>
/// Nothing here re-implements the generator. Every rule is stated in terms of what a reader of the
/// document is entitled to, so a rule that only ever passes because one transformer wrote the document
/// is a rule to delete rather than to keep.
/// </para>
/// </remarks>
public static class OpenApiLint
{
    /// <summary>The statuses every operation publishes, whatever else it can answer with.</summary>
    /// <remarks>
    /// Any request can be malformed, any request can be throttled, and any request can meet an
    /// unexpected failure. An operation that documents none of the three leaves a client with no
    /// contract for the three failures it is most likely to see.
    /// </remarks>
    public static readonly int[] UniversalFailures = [400, 429, 500];

    /// <summary>
    /// The one path exempt from documenting <c>426</c>, because it is exempt from the handshake that
    /// produces it: a refused client reads the versions it needs from here.
    /// </summary>
    public static readonly string[] ExemptFromClientVersion = [UnversionedPath];

    /// <summary>The one path that is deliberately outside the versioned surface.</summary>
    /// <remarks>
    /// <c>GET /api/version</c> is the handshake that tells a client which versions this server speaks,
    /// so versioning it would put the answer behind the question.
    /// </remarks>
    public const string UnversionedPath = "/api/version";

    /// <summary>Runs every rule and returns what failed.</summary>
    /// <param name="document">The document to lint.</param>
    /// <returns>One complaint per breach, empty when the document passes.</returns>
    public static IReadOnlyList<LintComplaint> Inspect(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var complaints = new List<LintComplaint>();

        InspectDocument(document, complaints);

        var operationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var describedTags = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Description))
            .Select(tag => tag.Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var (path, item) in document.Paths ?? [])
        {
            InspectPath(path, complaints);

            foreach (var (method, operation) in item.Operations ?? [])
            {
                var where = $"{method} {path}";
                InspectOperation(operation, where, describedTags, operationIds, complaints);
            }
        }

        return complaints;
    }

    private static void InspectDocument(OpenApiDocument document, List<LintComplaint> complaints)
    {
        if (string.IsNullOrWhiteSpace(document.Info?.Title))
        {
            complaints.Add(new("document-info", "info", "The document has no title."));
        }

        if (string.IsNullOrWhiteSpace(document.Info?.Version))
        {
            complaints.Add(new("document-info", "info", "The document declares no version."));
        }

        if (string.IsNullOrWhiteSpace(document.Info?.Description))
        {
            complaints.Add(new(
                "document-info",
                "info",
                "The document has no description. A reader arriving at it has to learn the versioning, "
                + "error and correlation conventions somewhere."));
        }

        if (document.Servers is not { Count: > 0 })
        {
            complaints.Add(new("document-servers", "servers", "The document names no server."));
        }

        if (document.Components?.SecuritySchemes is not { Count: > 0 })
        {
            complaints.Add(new(
                "document-security-schemes",
                "components.securitySchemes",
                "The document declares no security scheme, so no operation can name one."));
        }

        if (document.Components?.Schemas?.ContainsKey(ApiDocument.ProblemSchema) != true)
        {
            complaints.Add(new(
                "document-problem-schema",
                $"components.schemas.{ApiDocument.ProblemSchema}",
                "The shared error schema is missing, so the error contract is undocumented."));
        }
    }

    private static void InspectPath(string path, List<LintComplaint> complaints)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            complaints.Add(new(
                "path-prefix",
                path,
                "Every published path is under /api/. A path outside it is either an internal route that "
                + "should declare .InternalEndpoint(...), or a surface nobody agreed to publish."));
            return;
        }

        if (!path.StartsWith(ApiDocument.SurfacePrefix + "/", StringComparison.Ordinal)
            && !string.Equals(path, UnversionedPath, StringComparison.Ordinal))
        {
            complaints.Add(new(
                "path-version",
                path,
                $"The major version lives in the URL, so every published path is under "
                + $"{ApiDocument.SurfacePrefix}/. The one exception is {UnversionedPath}."));
        }
    }

    private static void InspectOperation(
        OpenApiOperation operation,
        string where,
        HashSet<string> describedTags,
        Dictionary<string, string> operationIds,
        List<LintComplaint> complaints)
    {
        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            complaints.Add(new(
                "operation-id",
                where,
                "The operation has no identifier. Declare one with .WithName(\"...\"); a generated "
                + "client names its method after it, so an operation without one is unnameable."));
        }
        else if (!operationIds.TryAdd(operation.OperationId, where))
        {
            complaints.Add(new(
                "operation-id-unique",
                where,
                $"The operation identifier '{operation.OperationId}' is already used by "
                + $"{operationIds[operation.OperationId]}."));
        }

        if (string.IsNullOrWhiteSpace(operation.Summary))
        {
            complaints.Add(new(
                "operation-summary",
                where,
                "The operation has no summary. Declare one with .WithSummary(\"...\")."));
        }

        InspectTags(operation, where, describedTags, complaints);
        InspectSecurity(operation, where, complaints);
        InspectResponses(operation, where, complaints);
        InspectExamples(operation, where, complaints);

        if (operation.Deprecated
            && (operation.Description is null
                || !operation.Description.Contains("Replaced by", StringComparison.OrdinalIgnoreCase)))
        {
            complaints.Add(new(
                "deprecation-names-replacement",
                where,
                "A deprecated operation names its replacement in its description, so that a client "
                + "reading the document knows where to go (conventions.md section 5.3)."));
        }
    }

    private static void InspectTags(
        OpenApiOperation operation,
        string where,
        HashSet<string> describedTags,
        List<LintComplaint> complaints)
    {
        if (operation.Tags is not { Count: > 0 })
        {
            complaints.Add(new(
                "operation-tag",
                where,
                "The operation names no tag, so it appears in no group of the published document."));
            return;
        }

        foreach (var tag in operation.Tags.Where(tag => !describedTags.Contains(tag.Name ?? string.Empty)))
        {
            complaints.Add(new(
                "tag-described",
                where,
                $"The tag '{tag.Name}' has no description at document level. Describe it in "
                + "ApiDocumentTransformer so that a reader learns what the group is."));
        }
    }

    private static void InspectSecurity(
        OpenApiOperation operation,
        string where,
        List<LintComplaint> complaints)
    {
        if (operation.Security is null)
        {
            complaints.Add(new(
                "operation-security",
                where,
                "The operation declares no security requirement at all. An operation that needs nothing "
                + "says so with an empty list; omitting the member means the document does not know."));
            return;
        }

        if (operation.Security.Count == 0
            && operation.Extensions?.ContainsKey("x-tailor360-anonymous") != true)
        {
            complaints.Add(new(
                "anonymous-justified",
                where,
                "The operation requires no credential and records no anonymous justification. Declare "
                + "the exposure with AllowAnonymousWithJustification so the set can be reviewed."));
        }

        foreach (var requirement in operation.Security)
        {
            foreach (var scheme in requirement.Keys.Where(scheme => scheme.Reference?.Id is null))
            {
                complaints.Add(new(
                    "security-scheme-resolved",
                    where,
                    $"The security requirement names a scheme that resolves to nothing: {scheme}."));
            }
        }
    }

    private static void InspectResponses(
        OpenApiOperation operation,
        string where,
        List<LintComplaint> complaints)
    {
        var responses = operation.Responses;

        if (responses is null || responses.Count == 0)
        {
            complaints.Add(new("operation-responses", where, "The operation documents no response."));
            return;
        }

        if (!responses.Keys.Any(IsSuccess))
        {
            complaints.Add(new(
                "operation-success-response",
                where,
                "The operation documents no successful response, so the document says what can go "
                + "wrong and not what going right looks like."));
        }

        foreach (var (status, response) in responses.Where(entry => IsSuccess(entry.Key)))
        {
            InspectSuccessResponse(status, response, where, complaints);
        }

        foreach (var status in UniversalFailures.Where(status =>
            !responses.ContainsKey(status.ToString(CultureInfo.InvariantCulture))))
        {
            complaints.Add(new(
                "problem-response-coverage",
                where,
                $"The operation does not document {status}. Every operation can be sent a malformed "
                + "request, be throttled, or fail unexpectedly."));
        }

        var declares401 = responses.ContainsKey("401");
        var declares403 = responses.ContainsKey("403");
        if (declares401 != declares403)
        {
            complaints.Add(new(
                "problem-response-pairing",
                where,
                "401 and 403 are documented together or not at all: a client that cannot tell 'who are "
                + "you' from 'not you' either loops on a pointless re-authentication or reports a "
                + "refusal to somebody whose session merely expired."));
        }

        foreach (var (status, response) in responses.Where(entry => IsFailure(entry.Key)))
        {
            InspectFailureResponse(status, response, where, complaints);
        }
    }

    /// <summary>
    /// A success that returns a body says what shape the body has.
    /// </summary>
    /// <remarks>
    /// Without this the document describes every failure precisely and every success as the word "OK",
    /// which is the state a generated client cannot be built from: the request types come out and the
    /// response types do not, so every call site casts. It is the easiest rule in this file to breach by
    /// accident, because a minimal-API handler whose branches return <c>IResult</c> publishes no
    /// response type at all unless the endpoint declares one with <c>.Produces&lt;T&gt;()</c>, and
    /// nothing about writing the endpoint suggests otherwise.
    /// </remarks>
    private static void InspectSuccessResponse(
        string status,
        IOpenApiResponse response,
        string where,
        List<LintComplaint> complaints)
    {
        // 204 and 304 are defined to carry no body, so a schema on either would be the document
        // describing something the transport forbids.
        if (status is "204" or "304")
        {
            if (response.Content is { Count: > 0 })
            {
                complaints.Add(new(
                    "success-response-body",
                    where,
                    $"The operation documents a body on {status}, which is defined to have none."));
            }

            return;
        }

        if (response.Content?.TryGetValue("application/json", out var media) != true)
        {
            complaints.Add(new(
                "success-response-schema",
                where,
                $"The operation's {status} documents no JSON body. Declare the payload with "
                + ".Produces<T>(...) so the document says what a caller receives; a handler whose "
                + "branches return IResult publishes nothing on its own."));
            return;
        }

        if (media?.Schema is null)
        {
            complaints.Add(new(
                "success-response-schema",
                where,
                $"The operation's {status} documents a JSON body with no schema."));
        }
    }

    private static void InspectFailureResponse(
        string status,
        IOpenApiResponse response,
        string where,
        List<LintComplaint> complaints)
    {
        if (response.Content?.TryGetValue(ApiDocument.ProblemMediaType, out var media) != true)
        {
            complaints.Add(new(
                "problem-media-type",
                where,
                $"The {status} response is not returned as {ApiDocument.ProblemMediaType}. Errors are "
                + "RFC 9457 problem details everywhere, so a client can parse one handler for all of "
                + "them."));
            return;
        }

        if (media?.Schema is not OpenApiSchemaReference reference
            || reference.Reference?.Id != ApiDocument.ProblemSchema)
        {
            complaints.Add(new(
                "problem-schema",
                where,
                $"The {status} response does not reference the shared {ApiDocument.ProblemSchema} "
                + "schema, so the error contract is described twice and can drift."));
        }
    }

    private static void InspectExamples(
        OpenApiOperation operation,
        string where,
        List<LintComplaint> complaints)
    {
        if (operation.RequestBody?.Content is not { Count: > 0 } content)
        {
            return;
        }

        foreach (var (mediaType, media) in content)
        {
            if (media.Example is null && media.Examples is not { Count: > 0 })
            {
                complaints.Add(new(
                    "request-example",
                    where,
                    $"The {mediaType} request body carries no example. Register one in PayloadExamples "
                    + $"under the operation identifier '{operation.OperationId}'; a schema tells a "
                    + "reader the shape, an example tells them the request."));
            }
        }
    }

    private static bool IsSuccess(string status)
        => int.TryParse(status, CultureInfo.InvariantCulture, out var code) && code is >= 200 and < 300;

    private static bool IsFailure(string status)
        => int.TryParse(status, CultureInfo.InvariantCulture, out var code) && code >= 400;
}

/// <summary>One breach of one lint rule.</summary>
/// <param name="Rule">The rule identifier, matching the rule name in <c>.spectral.yaml</c>.</param>
/// <param name="Where">The operation or document member the breach is in.</param>
/// <param name="Detail">What is wrong, and what to do about it.</param>
public sealed record LintComplaint(string Rule, string Where, string Detail)
{
    /// <inheritdoc />
    public override string ToString() => $"[{Rule}] {Where}: {Detail}";
}
