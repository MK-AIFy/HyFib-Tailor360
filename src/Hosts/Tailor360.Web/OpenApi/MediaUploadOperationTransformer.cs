using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// Documents <c>POST /api/v1/media</c>'s multipart request body as the object it actually is — a file
/// plus five plain form fields — rather than the bare <c>IFormFile</c> schema
/// <c>.Accepts&lt;IFormFile&gt;()</c> produces on its own, which describes only the file and would leave
/// a reader with no way to learn that <c>purpose</c>, <c>customerId</c>, <c>orderId</c>, <c>jobId</c> and
/// <c>altText</c> exist. That mismatch is also why <c>docs/api/openapi.v1.json</c> failed its own lint:
/// <c>PayloadExamples</c>'s object-shaped example does not validate against a schema of type
/// <c>string</c>.
/// </summary>
/// <remarks>
/// The endpoint itself keeps <c>.Accepts&lt;IFormFile&gt;("multipart/form-data")</c> rather than a
/// form-DTO type — <c>MediaEndpoints.UploadAsync</c> reads the multipart form itself (its own purpose
/// parsing, its own GUID defaulting) rather than binding a parameter from it, the same reason
/// <see cref="ClientTelemetryOperationTransformer"/> hand-builds its
/// own request body instead of using <c>.Accepts&lt;T&gt;()</c>: that call does not only describe the
/// document, it also registers <c>IAcceptsMetadata</c> that routing reads, and changing which type it
/// names is a routing change — a document-only concern must never become one. Overriding the schema here,
/// entirely separately from the endpoint's own accepted-type declaration, is what keeps the two apart.
/// </remarks>
internal sealed class MediaUploadOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The names of <c>MediaPurpose</c>'s members, spelled out rather than reflected off the
    /// enum type: the Web host may reference a module's <c>Api</c> project but not its <c>Domain</c>
    /// (ARCH-006), and this document-only concern is not worth an exception for.</summary>
    private static readonly string[] PurposeNames =
        ["Material", "Reference", "Diagram", "Illustration", "QcEvidence", "DeliveryEvidence"];

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        // Matched on the operation id rather than the route: MediaEndpoints maps this route through
        // MapGroup(GroupPrefix).MapPost("/", ...), and reconstructing what ApiDescription.RelativePath
        // reports for a grouped route is one more thing to get exactly right for no benefit — the id
        // from .WithName("UploadMedia") already names this operation and nothing else.
        var isMediaUpload = string.Equals(operation.OperationId, "UploadMedia", StringComparison.Ordinal);

        if (!isMediaUpload || operation.RequestBody is not OpenApiRequestBody body)
        {
            return Task.CompletedTask;
        }

        body.Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            ["multipart/form-data"] = new OpenApiMediaType
            {
                Schema = BuildFormSchema(),
                Example = operation.OperationId is { Length: > 0 } id ? PayloadExamples.For(id) : null,
            },
        };

        return Task.CompletedTask;
    }

    private static OpenApiSchema BuildFormSchema() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "The file, plus the plain form fields the handler reads from the multipart body itself.",
        Required = new HashSet<string>(StringComparer.Ordinal) { "file", "purpose" },
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["file"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "binary",
                Description = "The image bytes.",
            },
            ["purpose"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "What the image was captured or supplied for.",
                Enum = [.. PurposeNames.Select(name => (JsonNode)JsonValue.Create(name))],
            },
            ["customerId"] = NullableUuid("The customer the object is attached to, where it is attached to one."),
            ["orderId"] = NullableUuid("The order the object is attached to, where it is attached to one."),
            ["jobId"] = NullableUuid("The garment job the object is attached to, where it is attached to one."),
            ["altText"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Description =
                    "Describes the image in words. Required for Diagram and Illustration; optional otherwise.",
            },
        },
    };

    private static OpenApiSchema NullableUuid(string description) => new()
    {
        Type = JsonSchemaType.String | JsonSchemaType.Null,
        Format = "uuid",
        Description = description,
    };
}
