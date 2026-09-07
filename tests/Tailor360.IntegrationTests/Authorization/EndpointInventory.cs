using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// The routes the application actually publishes, read from the composed route table and reduced to
/// the facts the authorisation matrix is written in terms of.
/// </summary>
/// <remarks>
/// <para>
/// It is built from <see cref="EndpointDataSource"/> rather than from source text on purpose. An
/// endpoint's policy, audit action and resource declaration are conventions applied while the
/// application composes itself; a source scan would see the call and not the outcome, and would miss
/// entirely a route added by a group convention, a filter or a library. What the matrix has to be
/// held against is what the server will actually serve.
/// </para>
/// <para>
/// <b>Nothing here silently skips an endpoint.</b> A route the inventory cannot classify becomes an
/// <see cref="EndpointDeclarationKind.Undeclared"/> row rather than being dropped, because a route that
/// disappears from the inventory is a route that disappears from the matrix, and the whole point of
/// the matrix is that a route cannot get out of it.
/// </para>
/// </remarks>
public static partial class EndpointInventory
{
    /// <summary>
    /// The method a route answering every verb is listed under. A fallback declares no method at all,
    /// and a blank cell in the matrix would read as an omission rather than as the fact it is.
    /// </summary>
    public const string AnyMethod = "ANY";

    /// <summary>The path prefix whose probes are the matrix's one standing exemption.</summary>
    /// <remarks>
    /// Health probes answer before authentication is a meaningful concept — an orchestrator asking
    /// whether the process is alive has no session and never will. They are exempted here and named in
    /// the matrix document rather than being quietly absent from both.
    /// </remarks>
    public const string HealthProbePrefix = "/health";

    /// <summary>Reads every route the application publishes.</summary>
    /// <param name="sources">The composed route table, resolved from the running host.</param>
    public static IReadOnlyList<EndpointDeclaration> Read(IEnumerable<EndpointDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return
        [
            .. sources
                .SelectMany(source => source.Endpoints)
                .SelectMany(endpoint => endpoint is RouteEndpoint route ? Describe(route) : Unroutable(endpoint))
                .OrderBy(declaration => declaration.Route, StringComparer.Ordinal)
                .ThenBy(declaration => declaration.Method, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// An endpoint that is not a route endpoint, as an <see cref="EndpointDeclarationKind.Undeclared"/>
    /// row keyed on its display name.
    /// </summary>
    /// <remarks>
    /// Nothing in the application produces one today. It is here because dropping it would be the one
    /// hole the remark above says does not exist: an endpoint absent from the inventory is absent from
    /// both sides of the reconciliation, and passes by not being there.
    /// </remarks>
    private static IEnumerable<EndpointDeclaration> Unroutable(Endpoint endpoint) =>
    [
        new EndpointDeclaration(
            AnyMethod,
            endpoint.DisplayName ?? "(unnamed endpoint)",
            EndpointDeclarationKind.Undeclared,
            null,
            null,
            null,
            false,
            null,
            false,
            null,
            false),
    ];

    /// <summary>
    /// Describes one route endpoint, once per HTTP method it answers.
    /// </summary>
    /// <remarks>
    /// A single endpoint answering both <c>GET</c> and <c>POST</c> is two rows in the matrix, because
    /// the two are two different things to be allowed to do. An endpoint that declares no method at all
    /// — a fallback — is one row under <c>*</c>.
    /// </remarks>
    private static IEnumerable<EndpointDeclaration> Describe(RouteEndpoint endpoint)
    {
        var route = Normalise(endpoint.RoutePattern.RawText ?? string.Empty);
        var permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>();
        var resource = endpoint.Metadata.GetMetadata<ResourceScopeMetadata>();
        var ownership = endpoint.Metadata.GetMetadata<ResourceOwnershipMetadata>();
        var anonymous = endpoint.Metadata.GetMetadata<AnonymousJustificationMetadata>();
        var audited = endpoint.Metadata.GetMetadata<AuditedEndpointMetadata>();
        var stepUp = endpoint.Metadata.GetMetadata<StepUpMetadata>();
        var assurance = SelfServiceAssurance(endpoint);

        // Anonymous first, and deliberately ahead of the permission. An endpoint carrying both is
        // anonymous on the wire — the authorisation middleware short-circuits on the AllowAnonymous
        // metadata and never evaluates the policy — so classifying it by its permission would put the
        // word "permission" in an owner-approved document about a route anybody can reach. The
        // contract tier refuses the combination outright (ARCH-022); this is what the matrix says
        // while such a route exists.
        var kind = anonymous is not null ? EndpointDeclarationKind.Anonymous
            : permission is not null ? EndpointDeclarationKind.Permission
            : assurance is not null ? EndpointDeclarationKind.SelfService
            : route.StartsWith(HealthProbePrefix, StringComparison.Ordinal)
                ? EndpointDeclarationKind.HealthProbe
                : EndpointDeclarationKind.Undeclared;

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        var declared = methods is { Count: > 0 } ? methods : [AnyMethod];

        return declared.Select(method => new EndpointDeclaration(
            method.ToUpperInvariant(),
            route,
            kind,
            permission?.PermissionKey,
            permission?.Scope,
            resource?.ResourceKind,
            ownership is not null,
            assurance,
            audited is not null,
            audited?.Action,
            stepUp is not null));
    }

    /// <summary>
    /// Reads the assurance level of a self-service endpoint, by type name.
    /// </summary>
    /// <remarks>
    /// By name, and by reflection, because the metadata belongs to the Identity module's <c>Api</c>
    /// project and a test project that referenced it would be reaching across a module boundary to read
    /// a type the platform deliberately does not know about. The name is asserted to exist by the
    /// non-empty guard in the matrix tests, so a rename fails loudly rather than reclassifying every
    /// self-service route as undeclared.
    /// </remarks>
    private static string? SelfServiceAssurance(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata.FirstOrDefault(item =>
            item is not null && item.GetType().Name == "SelfServiceMetadata");

        var level = metadata?.GetType().GetProperty("Assurance")?.GetValue(metadata);
        return level is null ? null : Hyphenate(level.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Strips route constraints, so <c>{sessionId:guid}</c> reads as <c>{sessionId}</c>.
    /// </summary>
    /// <remarks>
    /// The matrix is approved by a person, and a constraint is an implementation detail of the router
    /// rather than a fact about who may do what. Stripping it also means tightening a constraint —
    /// which is a good thing to be free to do — does not require an owner to re-approve a row.
    /// </remarks>
    private static string Normalise(string route)
    {
        var trimmed = route.StartsWith('/') ? route : "/" + route;
        return ConstraintPattern().Replace(trimmed, "{$1}");
    }

    private static string Hyphenate(string pascalCase)
        => string.Concat(pascalCase.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? "-" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()));

    [GeneratedRegex(@"\{(\*{0,2}[A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}")]
    private static partial Regex ConstraintPattern();
}

/// <summary>How an endpoint decides whether the caller may reach it.</summary>
public enum EndpointDeclarationKind
{
    /// <summary>The route demands a permission from the catalogue.</summary>
    Permission,

    /// <summary>The route is deliberately reachable without a session, with a recorded justification.</summary>
    Anonymous,

    /// <summary>The route is gated on the state of the caller's own session rather than on a permission.</summary>
    SelfService,

    /// <summary>The route is an orchestrator health probe, exempt by path.</summary>
    HealthProbe,

    /// <summary>
    /// The route declares nothing this inventory recognises. It exists so that such a route appears in
    /// the matrix as a failure rather than not appearing at all.
    /// </summary>
    Undeclared,
}

/// <summary>One route, reduced to the facts the authorisation matrix is written in terms of.</summary>
/// <param name="Method">The HTTP method, upper case.</param>
/// <param name="Route">The route template with constraints stripped.</param>
/// <param name="Kind">How the route decides who may reach it.</param>
/// <param name="PermissionKey">The permission demanded, when the route demands one.</param>
/// <param name="Scope">The branch reach declared alongside that permission.</param>
/// <param name="ResourceKind">The resource the answer is decided against, when there is one.</param>
/// <param name="RequiresAssignment">True when the route is limited to the resource's assignees.</param>
/// <param name="Assurance">The self-service assurance level, hyphenated, when the route is self-service.</param>
/// <param name="Audited">True when the route carries the audit filter.</param>
/// <param name="AuditAction">The audit action name, when it carries one.</param>
/// <param name="StepUpDeclared">True when the route declares a fresh re-authentication.</param>
public sealed record EndpointDeclaration(
    string Method,
    string Route,
    EndpointDeclarationKind Kind,
    string? PermissionKey,
    BranchScope? Scope,
    string? ResourceKind,
    bool RequiresAssignment,
    string? Assurance,
    bool Audited,
    string? AuditAction,
    bool StepUpDeclared)
{
    /// <summary>How a row of the matrix names this route.</summary>
    public string Signature => $"{Method} {Route}";

    /// <summary>The word the matrix document uses for this route's declaration.</summary>
    public string DeclarationText => Kind switch
    {
        EndpointDeclarationKind.Permission => "permission",
        EndpointDeclarationKind.Anonymous => "anonymous",
        EndpointDeclarationKind.SelfService => $"self-service:{Assurance}",
        EndpointDeclarationKind.HealthProbe => "health-probe",
        _ => "undeclared",
    };
}
