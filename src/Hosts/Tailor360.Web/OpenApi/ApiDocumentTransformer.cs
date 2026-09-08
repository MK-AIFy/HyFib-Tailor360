using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// Adds everything the document says about itself: what the API is, where it is served, how a caller
/// authenticates, what an error looks like, and what each tag groups.
/// </summary>
/// <remarks>
/// The two security schemes describe the credentials a request carries, which is not the same thing as
/// the authentication schemes the application registers. ASP.NET Core registers exactly one of the
/// latter — the session cookie (ARCH-019) — and the anti-forgery token is not a second way to
/// authenticate: it is a second thing a state-changing request must carry, which is why the operations
/// that need both name both in one requirement rather than in two alternatives.
/// </remarks>
internal sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>What each tag groups. A tag used by an operation and missing here fails the lint.</summary>
    private static readonly Dictionary<string, string> TagDescriptions = new(StringComparer.Ordinal)
    {
        ["Authentication"] =
            "Signing in, second factors, passkeys and account recovery. These are the only operations "
            + "that can be reached before a session exists, and every one of them still requires the "
            + "anti-forgery token.",
        ["Sessions"] =
            "The caller's own session and device inventory, and revoking a session from it.",
        ["Administration"] =
            "Administering the organisation itself: staff accounts and their standing. Every operation "
            + "here demands a permission, a second factor answered recently, and a written reason, and "
            + "every one of them is recorded in the audit trail with the administrator's name.",
        ["Customers"] =
            "The customer record: finding one, creating one, correcting it, withdrawing it from use, "
            + "and recording that a second branch has begun serving the person. A search answers across "
            + "the whole organisation and returns a masked disambiguation card for a record the "
            + "caller's branches cannot see, because a revealed last-four is cheaper than the duplicate "
            + "record it prevents. Contact details are a separate permission from finding the record at "
            + "all, so a role without `customers.read_contact` receives those fields as null.",
        ["Platform"] =
            "Cross-cutting operations owned by the backend-for-frontend rather than by a module: the "
            + "anti-forgery token pair and the build description the client shell reads on start-up.",
    };

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        DescribeApi(document);
        DescribeServers(document);
        DescribeSecuritySchemes(document);
        DescribeErrorContract(document);
        DescribeSharedResponses(document);
        DescribeTags(document);

        return Task.CompletedTask;
    }

    private static void DescribeApi(OpenApiDocument document)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = ApiDocument.Title;
        document.Info.Version = ApiDocument.Version;
        document.Info.Summary =
            "The same-origin backend-for-frontend serving the HyFib Tailor 360 progressive web "
            + "application.";
        document.Info.Description = string.Join('\n',
            "The staff surface is `/api/v1/**`, served from the same origin as the client so that the",
            "session travels in a cookie script cannot read. The major version lives in the URL and",
            "nothing else negotiates it; inside a major version only additive changes are permitted, and",
            "a breaking one is refused by the diff gate unless it is approved and its deprecation is",
            "recorded (`docs/architecture/conventions.md` section 5).",
            string.Empty,
            "Every request and response carries `X-Correlation-Id`. Errors are RFC 9457 problem details",
            "with a stable `code` and, for validation failures, per-field `errors`; no error ever carries",
            "a stack trace or an exception message. Commands that a client may retry take an",
            "`Idempotency-Key`; editable aggregates carry an `ETag` and require `If-Match`.",
            string.Empty,
            "This document is generated from the composed route table and committed to",
            "`docs/api/openapi.v1.json`. It is not written by hand, and an endpoint that does not appear",
            "in it fails the endpoint inventory test.");
    }

    private static void DescribeServers(OpenApiDocument document)
    {
        // One relative server, because the API is same-origin by design: the client is served by this
        // host, and an absolute URL here would be wrong for every deployment but the one it named.
        document.Servers =
        [
            new OpenApiServer
            {
                Url = "/",
                Description = "The origin serving the progressive web application.",
            },
        ];
    }

    private static void DescribeSecuritySchemes(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[ApiDocument.SessionSecurityScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = SessionAuthenticationDefaults.CookieName,
            Description =
                "An opaque server-side session identifier. The browser sends it automatically; script "
                + "cannot read it, and no operation ever returns it. It is issued by signing in and is "
                + "rotated on every change of assurance.",
        };

        document.Components.SecuritySchemes[ApiDocument.AntiForgerySecurityScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = AntiforgeryDefaults.HeaderName,
            Description =
                "The request half of the anti-forgery token pair, fetched from `GET /api/v1/antiforgery` "
                + "and held in memory. Required on every state-changing request, including signing in: a "
                + "sign-in forged from another origin would plant the attacker's account in the victim's "
                + "browser. It is useless without the paired cookie, which script cannot read.",
        };
    }

    private static void DescribeErrorContract(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        // Written out rather than generated from a type, because the shape is fixed by
        // docs/architecture/conventions.md section 4.3 and is shared by every module. A generated
        // schema would follow whichever class happened to implement it.
        document.Components.Schemas[ApiDocument.ProblemSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Title = "Problem details",
            Description =
                "RFC 9457 problem details. Every failure is reported in this shape, and never as a stack "
                + "trace, an exception message or a bare status code.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "type", "title", "status", "code" },
            AdditionalPropertiesAllowed = true,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["type"] = Text(
                    "The problem type, as `urn:tailor360:problem:<code>`. Stable across releases; a "
                    + "client may branch on it.",
                    format: "uri"),
                ["title"] = Text("A short, human-readable summary of the problem type."),
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Description = "The HTTP status code, repeated so the body is self-contained.",
                },
                ["detail"] = Nullable("What went wrong on this occurrence, in words a person can act on."),
                ["instance"] = Nullable("The request path this occurrence refers to."),
                ["code"] = Text(
                    "The stable error code, `<module>.<kebab-case-reason>` — for example "
                    + "`idempotency.key-reused`. This is what a client branches on."),
                ["correlationId"] = Text(
                    "The correlation identifier of the request, echoed so a support call can name it."),
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    Description =
                        "Field errors, keyed by the field's name in the request payload. Present on "
                        + "validation failures only.",
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                },
                ["retryable"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Boolean | JsonSchemaType.Null,
                    Description =
                        "True when repeating the identical request may succeed. A retry of a command "
                        + "reuses the same `Idempotency-Key`.",
                },
                ["currentVersion"] = Nullable(
                    "The aggregate's current concurrency token, on a version conflict, so the client can "
                    + "re-read and merge without a second round trip."),
                ["currentEtag"] = Nullable(
                    "The same token in `ETag` form, ready to be sent back as `If-Match`."),
            },
        };

        static OpenApiSchema Text(string description, string? format = null) => new()
        {
            Type = JsonSchemaType.String,
            Format = format,
            Description = description,
        };

        static OpenApiSchema Nullable(string description) => new()
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description = description,
        };
    }

    private static void DescribeSharedResponses(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        document.Components.Responses ??= new Dictionary<string, IOpenApiResponse>(StringComparer.Ordinal);

        document.Components.Headers[ApiProblemResponses.CorrelationHeader] = new OpenApiHeader
        {
            Description =
                "The correlation identifier of this request. It is accepted on the request and echoed on "
                + "every response, and it is the identifier a support call names.",
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
        };

        document.Components.Headers[ApiProblemResponses.RetryAfterHeader] = new OpenApiHeader
        {
            Description = "How many seconds to wait before retrying.",
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
        };

        foreach (var problem in ApiProblemResponses.Catalogue)
        {
            var headers = new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal)
            {
                [ApiProblemResponses.CorrelationHeader] =
                    new OpenApiHeaderReference(ApiProblemResponses.CorrelationHeader, document),
            };

            if (problem.RetryAfter)
            {
                headers[ApiProblemResponses.RetryAfterHeader] =
                    new OpenApiHeaderReference(ApiProblemResponses.RetryAfterHeader, document);
            }

            document.Components.Responses[problem.ComponentName] = new OpenApiResponse
            {
                Description = problem.Description,
                Headers = headers,
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [ApiDocument.ProblemMediaType] = new()
                    {
                        Schema = new OpenApiSchemaReference(ApiDocument.ProblemSchema, document),
                    },
                },
            };
        }
    }

    private static void DescribeTags(OpenApiDocument document)
    {
        var used = document.Paths
            .SelectMany(path => path.Value.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .SelectMany(operation => operation.Tags ?? new HashSet<OpenApiTagReference>())
            .Select(tag => tag.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var described = new SortedSet<OpenApiTag>(TagNameComparer.Instance);

        foreach (var name in used)
        {
            described.Add(new OpenApiTag
            {
                Name = name,
                Description = TagDescriptions.GetValueOrDefault(name!),
            });
        }

        document.Tags = described;
    }

    /// <summary>Orders and compares document tags by name, so the committed document is stable.</summary>
    private sealed class TagNameComparer : IComparer<OpenApiTag>
    {
        public static readonly TagNameComparer Instance = new();

        public int Compare(OpenApiTag? x, OpenApiTag? y)
            => string.CompareOrdinal(x?.Name, y?.Name);
    }
}
