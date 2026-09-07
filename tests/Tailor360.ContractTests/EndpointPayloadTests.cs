using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Web.OpenApi;

namespace Tailor360.ContractTests;

/// <summary>
/// ARCH-013: every payload type on the public API is declared in an <c>Api</c> project, and no domain
/// type is ever returned from an endpoint.
/// </summary>
/// <remarks>
/// <para>
/// It lives with the contract tier rather than the architecture tier because the rule is about the
/// composed route table and the document generated from it, not about the project graph: a domain
/// entity reaches the wire by being referenced from a payload, which no <c>.csproj</c> records.
/// </para>
/// <para>
/// Returning an aggregate publishes the domain model as an API. A renamed field then breaks the
/// progressive web application; an added property silently discloses whatever it holds — a customer's
/// contact details, a measurement, an internal cost — to everybody entitled to the response; and the
/// field-level minimisation the permission model depends on has nothing to attach to. The `Api`
/// project's payload is also the only place a versioned, documented, diffed shape can live.
/// </para>
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointPayloadTests(WebHostFixture fixture)
{
    [Fact]
    public void Arch013_EndpointPayloadTypesAreDeclaredInApiProjects()
    {
        var complaints = new List<string>();

        foreach (var (endpoint, role, declared) in PayloadTypes())
        {
            foreach (var type in PayloadTypeInspector.Walk(declared))
            {
                var assembly = type.Assembly.GetName().Name ?? "(unknown)";
                var origin = PayloadTypeInspector.Classify(assembly, type.FullName ?? type.Name);

                if (origin is PayloadOrigin.PublishedApi
                    or PayloadOrigin.Framework
                    or PayloadOrigin.SerialisablePlatform)
                {
                    continue;
                }

                complaints.Add(
                    $"{endpoint} {role} {declared.Name} reaches {type.FullName} in {assembly} "
                    + $"({origin}). {Advice(origin)}");
            }
        }

        complaints.ShouldBeEmpty(
            "ARCH-013: these endpoints publish a type that is not declared in an Api project:\n"
            + string.Join('\n', complaints.Select(complaint => "  " + complaint)));
    }

    /// <summary>
    /// The same rule read from the other end: every schema in the generated document is one of the
    /// payload types the route table declares, or the shared error contract.
    /// </summary>
    /// <remarks>
    /// The CLR walk above and this check answer different questions. The walk asks what the endpoints
    /// publish; this asks what the document publishes, which is what a generated client will actually
    /// carry. A schema in the document that nothing in the route table declares is a shape somebody has
    /// added to the contract without an endpoint behind it.
    /// </remarks>
    [Fact]
    public async Task Arch013_EverySchemaInTheDocumentIsADeclaredPayload()
    {
        var document = await ApiDocumentSource.GenerateAsync(
            fixture.Services, TestContext.Current.CancellationToken);

        var reachable = PayloadTypes()
            .SelectMany(payload => PayloadTypeInspector.Walk(payload.Declared))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        // The shared RFC 9457 shape is written by the document transformer rather than generated from a
        // type, because conventions.md section 4.3 owns it. It is the rule's named exception.
        reachable.Add(ApiDocument.ProblemSchema);

        var unexplained = (document.Components?.Schemas?.Keys ?? [])
            .Where(name => !reachable.Contains(name))
            .ToList();

        unexplained.ShouldBeEmpty(
            "ARCH-013: the document declares schemas that no endpoint payload reaches: "
            + string.Join(", ", unexplained));
    }

    /// <summary>
    /// Every endpoint that accepts a body declares the type it accepts, so that the walk above has
    /// something to walk. An endpoint binding a raw stream or an untyped body would pass ARCH-013 by
    /// publishing nothing the rule can see.
    /// </summary>
    [Fact]
    public void EveryEndpointThatAcceptsABodyNamesItsType()
    {
        var untyped = new List<string>();

        foreach (var endpoint in Endpoints())
        {
            foreach (var accepts in endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>()
                .Where(accepts => accepts.RequestType is null))
            {
                untyped.Add(Describe(endpoint));
            }
        }

        untyped.ShouldBeEmpty(
            "these endpoints accept a body without declaring its type, so nothing can check what they "
            + "publish:\n" + string.Join('\n', untyped));
    }

    // ---- Negative controls -------------------------------------------------------------------
    //
    // Two detectors, two sets of controls. The classifier decides whether an assembly may declare a
    // payload; the walker decides which types a payload reaches. Either could stop detecting without
    // the rule ever failing, which is exactly the failure these controls exist to make impossible.

    [Theory]
    [InlineData("Tailor360.Modules.Orders.Api", PayloadOrigin.PublishedApi)]
    [InlineData("Tailor360.Web", PayloadOrigin.PublishedApi)]
    [InlineData("System.Private.CoreLib", PayloadOrigin.Framework)]
    [InlineData("Microsoft.AspNetCore.Http.Abstractions", PayloadOrigin.Framework)]
    [InlineData("Tailor360.Modules.Orders.Domain", PayloadOrigin.Domain)]
    [InlineData("Tailor360.Modules.Orders.Contracts", PayloadOrigin.ModuleContracts)]
    [InlineData("Tailor360.Modules.Orders.Application", PayloadOrigin.InternalLayer)]
    [InlineData("Tailor360.Modules.Orders.Infrastructure", PayloadOrigin.InternalLayer)]
    [InlineData("Tailor360.Platform.Persistence", PayloadOrigin.InternalLayer)]
    public void TheClassifierPlacesEachKindOfAssembly(string assembly, PayloadOrigin expected)
        => PayloadTypeInspector.Classify(assembly, "Some.Type").ShouldBe(expected);

    [Fact]
    public void TheClassifierAllowsTheNamedSerialisablePlatformTypes()
    {
        foreach (var allowed in PayloadTypeInspector.SerialisablePlatformTypes)
        {
            PayloadTypeInspector.Classify("Tailor360.Platform.Abstractions", allowed)
                .ShouldBe(PayloadOrigin.SerialisablePlatform);
        }

        // The allowlist is by type, not by assembly: a different type from the same assembly is still
        // an internal type. Otherwise adding one value object would open the whole library.
        PayloadTypeInspector
            .Classify("Tailor360.Platform.Abstractions", "Tailor360.Platform.Abstractions.Events.DomainEvent")
            .ShouldBe(PayloadOrigin.InternalLayer);
    }

    [Fact]
    public void TheWalkerReachesATypeNestedInsideAPayload()
    {
        var reached = PayloadTypeInspector.Walk(typeof(OuterPayload));

        reached.ShouldContain(typeof(NestedPayload));
        reached.ShouldContain(typeof(DeeplyNestedPayload));
    }

    [Fact]
    public void TheWalkerReachesTheElementTypeOfACollection()
        => PayloadTypeInspector.Walk(typeof(CollectionPayload)).ShouldContain(typeof(DeeplyNestedPayload));

    [Fact]
    public void TheWalkerReachesTheUnderlyingTypeOfANullable()
        => PayloadTypeInspector.Walk(typeof(NullablePayload)).ShouldContain(typeof(NestedEnum));

    [Fact]
    public void TheWalkerTerminatesOnACycle()
        => PayloadTypeInspector.Walk(typeof(CyclicPayload)).ShouldContain(typeof(CyclicPayload));

    [Fact]
    public void TheRuleWouldFailOnADomainTypeNestedTwoLevelsDown()
    {
        // The two detectors composed, over a payload built to break the rule: the walker has to reach
        // the third level, and the classifier has to refuse the assembly it is declared in.
        var origins = PayloadTypeInspector.Walk(typeof(OuterPayload))
            .Select(type => PayloadTypeInspector.Classify(
                type == typeof(DeeplyNestedPayload) ? "Tailor360.Modules.Orders.Domain" : "System.Private.CoreLib",
                type.FullName ?? type.Name))
            .ToList();

        origins.ShouldContain(PayloadOrigin.Domain);
    }

    private static string Advice(PayloadOrigin origin) => origin switch
    {
        PayloadOrigin.Domain =>
            "Declare a payload record in the module's Api project and project the aggregate onto it.",
        PayloadOrigin.ModuleContracts =>
            "A Contracts type is for module-to-module use. Re-declare the same shape in the Api "
            + "project, so that changing the internal contract does not change the public one.",
        _ =>
            "Move the shape into the module's Api project, or add the type to the serialisable platform "
            + "allowlist in a pull request that says why publishing it is safe.",
    };

    private IEnumerable<(string Endpoint, string Role, Type Declared)> PayloadTypes()
    {
        foreach (var endpoint in Endpoints())
        {
            var describe = Describe(endpoint);

            foreach (var accepts in endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
            {
                if (accepts.RequestType is { } request)
                {
                    yield return (describe, "accepts", request);
                }
            }

            foreach (var produces in endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>())
            {
                if (produces.Type is { } response && response != typeof(void))
                {
                    yield return (describe, "produces", response);
                }
            }
        }
    }

    private List<RouteEndpoint> Endpoints()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return [.. sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
    }

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }

    // Fixtures for the walker's controls. They are deliberately shaped like real payloads — a nested
    // record, a collection of records, a nullable enum and a self-reference — because those are the four
    // shapes a naive walker gets wrong.
    private sealed record OuterPayload(string Name, NestedPayload Nested);

    private sealed record NestedPayload(IReadOnlyList<DeeplyNestedPayload> Items);

    private sealed record DeeplyNestedPayload(Guid Id);

    private sealed record CollectionPayload(DeeplyNestedPayload[] Items);

    private sealed record NullablePayload(NestedEnum? Kind);

    private sealed record CyclicPayload(CyclicPayload? Parent, string Label);

    private enum NestedEnum
    {
        None = 0,
    }
}
