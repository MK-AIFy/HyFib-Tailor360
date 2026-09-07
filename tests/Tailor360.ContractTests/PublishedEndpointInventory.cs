using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.ContractTests;

/// <summary>
/// Reconciles the routes the application publishes with the operations the API document describes.
/// </summary>
/// <remarks>
/// <para>
/// The reconciliation is the point. Either side on its own proves nothing: a document can describe an
/// endpoint that was deleted last month, and an endpoint can be mapped, reachable and completely absent
/// from the contract its clients are generated from. Only holding the two against each other fails on
/// the day they disagree.
/// </para>
/// <para>
/// A route may be missing from the document for exactly two reasons — it declared
/// <c>.InternalEndpoint(reason, reviewedIn)</c>, or it is one of the health probes, whose exemption is
/// recorded in <c>docs/api/openapi-gates.md</c> and in the ARCH-017 rule. Excluding a route from the
/// document without saying so is itself a complaint, because otherwise <c>ExcludeFromDescription</c>
/// would be the way to escape documenting an endpoint.
/// </para>
/// </remarks>
public static partial class PublishedEndpointInventory
{
    /// <summary>The method a route answering every verb is listed under.</summary>
    public const string AnyMethod = "ANY";

    /// <summary>Reads the published routes from the composed route table.</summary>
    /// <param name="sources">The composed route table.</param>
    /// <returns>One row per route and method.</returns>
    public static IReadOnlyList<PublishedRoute> ReadRoutes(IEnumerable<EndpointDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return
        [
            .. sources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(Describe)
                .OrderBy(route => route.Path, StringComparer.Ordinal)
                .ThenBy(route => route.Method, StringComparer.Ordinal),
        ];
    }

    /// <summary>Reads the operations the document describes.</summary>
    /// <param name="document">The generated document.</param>
    /// <returns>One row per path and method.</returns>
    public static IReadOnlyList<DocumentedOperation> ReadOperations(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. from path in document.Paths ?? []
               from operation in path.Value.Operations ?? []
               select new DocumentedOperation(
                   operation.Key.Method.ToUpperInvariant(),
                   NormalisePath(path.Key),
                   operation.Value.OperationId),
        ];
    }

    /// <summary>Holds the two sides against each other.</summary>
    /// <param name="routes">What the application publishes.</param>
    /// <param name="operations">What the document describes.</param>
    /// <returns>One complaint per disagreement, empty when the two agree.</returns>
    public static IReadOnlyList<InventoryComplaint> Reconcile(
        IReadOnlyList<PublishedRoute> routes,
        IReadOnlyList<DocumentedOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(operations);

        var complaints = new List<InventoryComplaint>();
        var documented = operations
            .Select(operation => Key(operation.Method, operation.Path))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var route in routes)
        {
            var appears = documented.Contains(Key(route.Method, route.Path));

            if (route.IsHealthProbe)
            {
                continue;
            }

            if (route.InternalReason is { Length: > 0 })
            {
                if (appears)
                {
                    complaints.Add(new(
                        "internal-endpoint-documented",
                        $"{route.Method} {route.Path}",
                        "The route declares .InternalEndpoint(...) and is described in the document "
                        + "anyway. One of the two is wrong."));
                }

                continue;
            }

            if (!appears)
            {
                complaints.Add(new(
                    route.ExcludedFromDocument ? "undeclared-exclusion" : "undocumented-endpoint",
                    $"{route.Method} {route.Path}",
                    route.ExcludedFromDocument
                        ? "The route is excluded from the API document but records no reason. Declare "
                        + "it with .InternalEndpoint(reason, reviewedIn) so the exclusion is reviewable, "
                        + "or publish it."
                        : "The route is not in the API document. Every published endpoint appears in "
                        + "docs/api/openapi.v1.json, or declares .InternalEndpoint(reason, reviewedIn) "
                        + "to say why it publishes no contract."));
            }
        }

        var published = routes
            .Select(route => Key(route.Method, route.Path))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var operation in operations
            .Where(operation => !published.Contains(Key(operation.Method, operation.Path))))
        {
            complaints.Add(new(
                "phantom-operation",
                $"{operation.Method} {operation.Path}",
                "The document describes an operation the application does not publish. Regenerate the "
                + $"document: {RepositoryFiles.RegenerateCommand}"));
        }

        return complaints;
    }

    /// <summary>
    /// Reduces a route template to the path the document uses: constraints removed, catch-all markers
    /// removed, no trailing slash.
    /// </summary>
    /// <param name="template">A route template or a document path.</param>
    /// <returns>The comparable form.</returns>
    public static string NormalisePath(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var path = ParameterPattern().Replace(template, match =>
        {
            var name = match.Groups["name"].Value.TrimStart('*');
            return "{" + name + "}";
        });

        path = path.StartsWith('/') ? path : "/" + path;
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static IEnumerable<PublishedRoute> Describe(RouteEndpoint endpoint)
    {
        var path = NormalisePath(endpoint.RoutePattern.RawText ?? string.Empty);
        var internalReason = endpoint.Metadata.GetMetadata<InternalEndpointMetadata>()?.Reason;
        var excluded = endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
            ?.ExcludeFromDescription == true;
        var probe = endpoint.Metadata.GetMetadata<HealthProbeMetadata>() is not null;

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        var declared = methods is { Count: > 0 } ? methods : [AnyMethod];

        return declared.Select(method => new PublishedRoute(
            method.ToUpperInvariant(), path, excluded, internalReason, probe));
    }

    private static string Key(string method, string path) => $"{method} {path}";

    [GeneratedRegex(@"\{(?<name>[^}:?=]+)(?:[:?=][^}]*)?\}")]
    private static partial Regex ParameterPattern();
}

/// <summary>One route the application publishes.</summary>
/// <param name="Method">The HTTP method, or <c>ANY</c> for a route that declares none.</param>
/// <param name="Path">The route template in the document's form.</param>
/// <param name="ExcludedFromDocument">True when the route carries <c>ExcludeFromDescription</c>.</param>
/// <param name="InternalReason">Why the route publishes no contract, when it says.</param>
/// <param name="IsHealthProbe">True for the three probes, the one standing exemption.</param>
public sealed record PublishedRoute(
    string Method,
    string Path,
    bool ExcludedFromDocument,
    string? InternalReason,
    bool IsHealthProbe);

/// <summary>One operation the document describes.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path.</param>
/// <param name="OperationId">The operation identifier.</param>
public sealed record DocumentedOperation(string Method, string Path, string? OperationId);

/// <summary>One disagreement between the route table and the document.</summary>
/// <param name="Rule">Which reconciliation rule failed.</param>
/// <param name="Where">The route or operation it failed on.</param>
/// <param name="Detail">What is wrong, and what to do about it.</param>
public sealed record InventoryComplaint(string Rule, string Where, string Detail)
{
    /// <inheritdoc />
    public override string ToString() => $"[{Rule}] {Where}: {Detail}";
}
