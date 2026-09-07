using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Endpoints;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// Turns what an endpoint declares in code into what the document says about it: which credentials the
/// request carries, which failures it can answer with, what a request body looks like, and the
/// annotations that let the document double as the endpoint inventory.
/// </summary>
/// <remarks>
/// Every fact here is read from the endpoint's own metadata — its authorisation policy, its anonymous
/// justification, its rate-limit policy, its audit action. Nothing is configured twice, so the document
/// cannot describe a surface the pipeline does not enforce.
/// </remarks>
internal sealed class EndpointContractTransformer : IOpenApiOperationTransformer
{
    /// <summary>Names the operation's authorisation model in one word, for the inventory.</summary>
    private const string AuthorisationExtension = "x-tailor360-authorisation";

    /// <summary>Names where an anonymous exposure was reviewed.</summary>
    private const string AnonymousExtension = "x-tailor360-anonymous";

    /// <summary>Names the audit action a state change is recorded under.</summary>
    private const string AuditExtension = "x-tailor360-audit";

    /// <summary>Names the rate-limit policy the endpoint declares.</summary>
    private const string RateLimitExtension = "x-tailor360-rate-limit";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var anonymous = Find<AnonymousJustificationMetadata>(metadata);
        var permission = Find<RequiredPermissionMetadata>(metadata);
        var audited = Find<AuditedEndpointMetadata>(metadata);
        var rateLimit = Find<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>(metadata);

        // "Requires authorisation" is read from the composed policy rather than from any one declaration
        // verb, because there are three of them — a permission, a self-service assurance level and a
        // step-up demand — and all three end in the same place: an authorisation policy on the endpoint.
        var requiresAuthorisation =
            metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

        var isStateChanging = IsStateChanging(context.Description.HttpMethod);

        DescribeSecurity(operation, context, requiresAuthorisation, isStateChanging);
        DescribeFailures(operation, context, requiresAuthorisation);
        DescribeRequestExample(operation);
        Annotate(operation, requiresAuthorisation, anonymous, permission, audited, rateLimit);

        return Task.CompletedTask;
    }

    private static void DescribeSecurity(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        bool requiresAuthorisation,
        bool isStateChanging)
    {
        var requirement = new OpenApiSecurityRequirement();

        if (requiresAuthorisation)
        {
            requirement[new OpenApiSecuritySchemeReference(
                ApiDocument.SessionSecurityScheme, context.Document)] = [];
        }

        // The anti-forgery token is not an alternative credential, so it goes into the same requirement
        // object as the session rather than into a second one: OpenAPI reads the entries of one
        // requirement as "all of these", and a list of two requirements as "either of these".
        if (isStateChanging)
        {
            requirement[new OpenApiSecuritySchemeReference(
                ApiDocument.AntiForgerySecurityScheme, context.Document)] = [];
        }

        // An empty list is not the same as no list: it is how OpenAPI says "this operation needs
        // nothing", which is exactly true of a safe, anonymous read.
        operation.Security = requirement.Count > 0 ? [requirement] : [];
    }

    private static void DescribeFailures(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        bool requiresAuthorisation)
    {
        operation.Responses ??= [];

        var hasPathParameter = operation.Parameters?
            .Any(parameter => parameter.In == ParameterLocation.Path) == true;
        var hasRequestBody = operation.RequestBody is not null;

        // The version handshake is the one operation the handshake never refuses, so it is the one
        // operation that does not document 426.
        var honoursClientVersion = !string.Equals(
            context.Description.RelativePath is null ? string.Empty : "/" + context.Description.RelativePath,
            VersionEndpoints.Path,
            StringComparison.Ordinal);

        var applicable = ApiProblemResponses.Applicable(
            requiresAuthorisation, hasPathParameter, hasRequestBody, honoursClientVersion);

        foreach (var problem in ApiProblemResponses.Catalogue
            .Where(candidate => applicable.Contains(candidate.Status)))
        {
            var status = problem.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // An endpoint that declared this status itself has said something more specific than the
            // shared component can; leave it alone.
            if (operation.Responses.ContainsKey(status))
            {
                continue;
            }

            operation.Responses[status] =
                new OpenApiResponseReference(problem.ComponentName, context.Document);
        }
    }

    private static void DescribeRequestExample(OpenApiOperation operation)
    {
        if (operation.OperationId is not { Length: > 0 } operationId
            || operation.RequestBody is not OpenApiRequestBody body
            || PayloadExamples.For(operationId) is not { } example)
        {
            return;
        }

        foreach (var content in body.Content ?? new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal))
        {
            content.Value.Example ??= example.DeepClone();
        }
    }

    private static void Annotate(
        OpenApiOperation operation,
        bool requiresAuthorisation,
        AnonymousJustificationMetadata? anonymous,
        RequiredPermissionMetadata? permission,
        AuditedEndpointMetadata? audited,
        Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute? rateLimit)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);

        var model = permission is not null ? $"permission:{permission.PermissionKey}"
            : requiresAuthorisation ? "session"
            : "anonymous";

        Set(AuthorisationExtension, model);

        if (anonymous is not null)
        {
            Set(AnonymousExtension, anonymous.ReviewedIn);
        }

        if (audited is not null)
        {
            Set(AuditExtension, audited.Action);
        }

        if (rateLimit?.PolicyName is { Length: > 0 } policy)
        {
            Set(RateLimitExtension, policy);
        }

        void Set(string key, string value)
            => operation.Extensions[key] = new JsonNodeExtension(JsonValue.Create(value));
    }

    private static bool IsStateChanging(string? method)
        => method is not null
            && (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
                || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase));

    private static T? Find<T>(IEnumerable<object> metadata)
        where T : class
        => metadata.OfType<T>().LastOrDefault();
}
