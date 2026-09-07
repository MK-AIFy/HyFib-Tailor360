using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// The published description of the version 1 API: what it is called, which document name it is
/// generated under, and the composition that turns the route table into a document worth committing.
/// </summary>
/// <remarks>
/// <para>
/// The document is a deliverable, not a development convenience. It is committed to
/// <c>docs/api/openapi.v1.json</c>, linted, diffed for breaking changes against the base branch, and
/// reconciled against the live route table so that an endpoint cannot exist without appearing in it.
/// See <c>docs/api/openapi-gates.md</c>.
/// </para>
/// <para>
/// Everything the document says beyond the shape of the payloads is added by the two transformers
/// below rather than being written on each endpoint. That is deliberate: a security requirement, an
/// error contract or a rate-limit note repeated on ninety endpoints is a set of ninety opportunities to
/// disagree with the pipeline, whereas one transformer reading the endpoint's own metadata cannot.
/// </para>
/// </remarks>
public static class ApiDocument
{
    /// <summary>
    /// The document name. It is also the URL segment the generator serves it under in Development
    /// (<c>/openapi/v1.json</c>) and the suffix of the committed file.
    /// </summary>
    public const string Name = "v1";

    /// <summary>The title of the published document.</summary>
    public const string Title = "HyFib Tailor 360 API";

    /// <summary>
    /// The document's own version.
    /// </summary>
    /// <remarks>
    /// It is the version of the <em>contract</em>, not of the build: the major version lives in the URL
    /// (<c>conventions.md</c> section 5.1) and every change inside it is additive, so this number moves
    /// only when a new major surface is published. Binding it to the build version instead would make
    /// the committed document differ on every commit and the diff gate meaningless.
    /// </remarks>
    public const string Version = "1.0.0";

    /// <summary>The route prefix of the staff and progressive-web-application surface.</summary>
    public const string SurfacePrefix = "/api/v1";

    /// <summary>
    /// The name of the major surface, as <c>GET /api/version</c> reports it and as it appears in
    /// <see cref="SurfacePrefix"/>. A client compares it with the surface it was generated against and
    /// refuses to start on a mismatch, rather than discovering the mismatch one failed request at a time.
    /// </summary>
    public const string Major = "v1";

    /// <summary>The security scheme name for the session cookie.</summary>
    public const string SessionSecurityScheme = "sessionCookie";

    /// <summary>The security scheme name for the anti-forgery request token.</summary>
    public const string AntiForgerySecurityScheme = "antiForgeryToken";

    /// <summary>The name of the shared RFC 9457 error schema.</summary>
    public const string ProblemSchema = "ProblemDetails";

    /// <summary>The media type every error is returned as.</summary>
    public const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// Registers the generator for the version 1 document, with the transformers that add the security
    /// schemes, the error contract, the examples and the inventory annotations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, so registration chains.</returns>
    public static IServiceCollection AddTailor360OpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(Name, options =>
        {
            // 3.1 rather than 3.0: it is the version whose JSON Schema dialect matches what the
            // generator produces for nullable reference types, so `string | null` survives into the
            // generated TypeScript client instead of being flattened to `string`.
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

            options.AddDocumentTransformer<ApiDocumentTransformer>();
            options.AddOperationTransformer<EndpointContractTransformer>();
            options.AddSchemaTransformer<OmittedMemberSchemaTransformer>();
        });

        return services;
    }
}
