using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Tailor360.Web.Telemetry;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// Documents <c>POST /api/v1/telemetry/client</c>'s request body and its two failure responses that
/// <see cref="EndpointContractTransformer"/>'s generic derivation does not reach: <c>415</c>, because the
/// derivation only adds it when the operation already has a request body, and this one deliberately does
/// not get one from <c>.Accepts&lt;T&gt;()</c> (see below); and <c>413</c>, which is not in the shared
/// catalogue at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>The request body is built by hand rather than with <c>.Accepts&lt;T&gt;()</c>.</b> The handler
/// reads and measures the raw body itself — that is how <c>ClientTelemetry:MaxBodyBytes</c> and the
/// content-type check are enforced before anything is parsed — so no parameter is bound from it, and nothing
/// about the endpoint declaration says its shape. <c>.Accepts&lt;T&gt;()</c> was tried first and reverted:
/// it does not only describe the document, it also registers <c>IAcceptsMetadata</c> that ASP.NET Core's
/// own routing reads, and for a request whose content type does not match, routing then leaves
/// <c>HttpContext.GetEndpoint()</c> unset — so <see cref="Tailor360.Platform.Security.Antiforgery.AntiforgeryExemptionMetadata"/>
/// was never found, and the request fell through to full anti-forgery validation instead of ever reaching
/// this route's own <c>IsJson</c> check. Building the document here, entirely separately from routing,
/// is what keeps "what the document says" and "what the pipeline enforces" from being the same
/// declaration with two different jobs.
/// </para>
/// <para>
/// This route can also answer <c>403</c> — the declared-origin filter and the global cross-site check
/// both refuse with it — but it is deliberately left undocumented rather than added here. The shared
/// catalogue's own rule (<see cref="ApiProblemResponses"/>) is that <c>401</c> and <c>403</c> are declared
/// together or not at all, because together they are how a client tells "who are you" from "you,
/// specifically, may not"; this route has no session and therefore no <c>401</c> to pair it with, and
/// inventing one would document a response that can never occur. A client that flushes telemetry with
/// <c>navigator.sendBeacon</c> does not read the status of a refusal in any case — see
/// <c>docs/api/conventions.md</c> section 5.1, "the client must not wait on it, and must not retry a
/// refusal".
/// </para>
/// <para>
/// <c>415</c> references the same shared component every other operation with a body does; <c>413</c> has
/// no shared component to reference, since no other operation answers it, so it is built here with the
/// same schema and header the shared ones carry.
/// </para>
/// <para>
/// Runs after <see cref="EndpointContractTransformer"/> in registration order, and has to: that
/// transformer reads <c>operation.RequestBody</c> to decide whether <c>415</c> and a request example
/// apply, and this transformer is what sets it. Reversed, the generic derivation would see no request
/// body yet and add neither.
/// </para>
/// </remarks>
internal sealed class ClientTelemetryOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Description.RelativePath is null ? string.Empty : "/" + context.Description.RelativePath;
        var isClientTelemetryIngest =
            string.Equals(path, ClientTelemetryEndpoints.Path, StringComparison.Ordinal)
            && string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (!isClientTelemetryIngest)
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = BuildRequestBody(operation.OperationId);
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["415"] = new OpenApiResponseReference("UnsupportedMediaType", context.Document);
        operation.Responses["413"] = BuildPayloadTooLargeResponse(context.Document);

        return Task.CompletedTask;
    }

    private static OpenApiRequestBody BuildRequestBody(string? operationId) => new()
    {
        Required = true,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            ["application/json"] = new OpenApiMediaType
            {
                Schema = BuildBatchSchema(),
                Example = operationId is { Length: > 0 } id ? PayloadExamples.For(id) : null,
            },
        },
    };

    private static OpenApiSchema BuildBatchSchema() => new()
    {
        Type = JsonSchemaType.Object,
        Description =
            "A client-generated flush: an envelope identifying the client, carrying the events it "
            + "observed since the last flush.",
        Required = new HashSet<string>(StringComparer.Ordinal) { "batchId", "events" },
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["batchId"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uuid",
                Description = "A client-generated identifier for this flush, for de-duplication at the log level.",
            },
            ["clientVersion"] = NullableString("The build the client is running."),
            ["routeName"] = NullableString("The client route the batch was flushed from, by name — never a URL."),
            ["deviceClass"] = NullableString(
                "The device class the client detected itself as, for example `shop-floor-phone`."),
            ["engine"] = NullableString("The rendering engine, for example `blink`."),
            ["operatingSystemFamily"] = NullableString("The operating-system family, for example `android`."),
            ["events"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Description = "The events flushed in this batch, bounded by `ClientTelemetry:MaxEventsPerBatch`.",
                Items = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                    {
                        ["type"] = NullableString(
                            "The event type. Only a closed set of names survives the server-side "
                            + "allowlist; anything else drops the whole event."),
                        ["timestamp"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String | JsonSchemaType.Null,
                            Format = "date-time",
                            Description = "When the client observed it.",
                        },
                        ["attributes"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object | JsonSchemaType.Null,
                            Description =
                                "A bounded attribute bag. Only the names and shapes the allowlist declares "
                                + "for this event's type survive; everything else is dropped before it "
                                + "reaches a sink.",
                            AdditionalPropertiesAllowed = true,
                        },
                    },
                },
            },
        },
    };

    private static OpenApiSchema NullableString(string description) => new()
    {
        Type = JsonSchemaType.String | JsonSchemaType.Null,
        Description = description,
    };

    private static OpenApiResponse BuildPayloadTooLargeResponse(OpenApiDocument? document) => new()
    {
        Description = "The batch body exceeded ClientTelemetry:MaxBodyBytes.",
        Headers = new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal)
        {
            [ApiProblemResponses.CorrelationHeader] =
                new OpenApiHeaderReference(ApiProblemResponses.CorrelationHeader, document),
        },
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [ApiDocument.ProblemMediaType] = new()
            {
                Schema = new OpenApiSchemaReference(ApiDocument.ProblemSchema, document),
            },
        },
    };
}
