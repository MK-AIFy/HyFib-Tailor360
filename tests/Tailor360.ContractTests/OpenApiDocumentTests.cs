using System.Net.Http;
using Microsoft.OpenApi;
using Shouldly;
using Tailor360.Web.OpenApi;

namespace Tailor360.ContractTests;

/// <summary>
/// The published API document: that the committed copy is the one the application produces, and that it
/// passes the lint the contract gate is written in terms of.
/// </summary>
/// <remarks>
/// <para>
/// The committed document at <c>docs/api/openapi.v1.json</c> is not a convenience copy. It is what the
/// breaking-change gate diffs, what the client's types are generated from, and what a reviewer reads to
/// see the shape of a change. If it can drift from the running application it is worse than nothing,
/// because it is then a confident description of a surface that does not exist — hence this test.
/// </para>
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class OpenApiDocumentTests(WebHostFixture fixture)
{
    [Fact]
    public async Task TheCommittedDocumentIsTheGeneratedDocument()
    {
        var document = await ApiDocumentSource.GenerateAsync(fixture.Services, TestContext.Current.CancellationToken);
        var generated = await ApiDocumentSource.SerialiseAsync(document);
        var path = RepositoryFiles.ApiDocument;

        if (RepositoryFiles.Regenerating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(path).ShouldBeTrue(
            $"The published API document is missing. Write it with:\n  {RepositoryFiles.RegenerateCommand}");

        var committed = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
            .ReplaceLineEndings("\n");

        committed.ShouldBe(
            generated,
            "The committed API document no longer matches the application's routes. This is the "
            + "intended failure when an endpoint or a payload changes: regenerate it, read the diff, and "
            + "commit it with the change that caused it.\n"
            + $"  {RepositoryFiles.RegenerateCommand}");
    }

    [Fact]
    public async Task TheGeneratedDocumentPassesTheLint()
    {
        var document = await ApiDocumentSource.GenerateAsync(fixture.Services, TestContext.Current.CancellationToken);

        var complaints = OpenApiLint.Inspect(document);

        complaints.ShouldBeEmpty(
            "The published API document fails its own lint:\n"
            + string.Join('\n', complaints.Select(complaint => "  " + complaint)));
    }

    [Fact]
    public async Task TheDocumentDescribesTheOneSchemeTheApplicationRegisters()
    {
        var document = await ApiDocumentSource.GenerateAsync(fixture.Services, TestContext.Current.CancellationToken);

        var schemes = document.Components?.SecuritySchemes
            ?? new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        // The session cookie is the only way to authenticate (ARCH-019). The anti-forgery token is a
        // second thing a state-changing request carries, not a second way to be somebody, so an
        // operation that needs both names both inside one requirement rather than offering a choice.
        schemes.Keys.ShouldBe(
            [ApiDocument.SessionSecurityScheme, ApiDocument.AntiForgerySecurityScheme],
            ignoreOrder: true);

        foreach (var requirement in document.Paths
            .SelectMany(path => path.Value.Operations ?? [])
            .SelectMany(operation => operation.Value.Security ?? []))
        {
            requirement.Count.ShouldBeGreaterThan(0);
        }

        document.Paths
            .SelectMany(path => path.Value.Operations ?? [])
            .SelectMany(operation => operation.Value.Security ?? [])
            .Count(requirement => requirement.Keys.Any(scheme =>
                scheme.Reference?.Id == ApiDocument.SessionSecurityScheme))
            .ShouldBeGreaterThan(0, "no operation names the session cookie, which cannot be right.");
    }

    [Fact]
    public async Task EveryDocumentedOperationCarriesTheAnnotationsTheInventoryReadsFrom()
    {
        var document = await ApiDocumentSource.GenerateAsync(fixture.Services, TestContext.Current.CancellationToken);

        foreach (var (path, item) in document.Paths)
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                operation.Extensions.ShouldNotBeNull($"{method} {path} carries no annotations.");
                operation.Extensions.ShouldContainKey(
                    "x-tailor360-authorisation",
                    $"{method} {path} does not say how it is authorised.");
            }
        }
    }

    /// <summary>
    /// The canonical form is stable: serialising the same document twice produces the same bytes, which
    /// is what makes the committed copy diffable rather than noisy.
    /// </summary>
    [Fact]
    public async Task TheCanonicalFormIsStable()
    {
        var document = await ApiDocumentSource.GenerateAsync(fixture.Services, TestContext.Current.CancellationToken);

        var first = await ApiDocumentSource.SerialiseAsync(document);
        var second = await ApiDocumentSource.SerialiseAsync(document);

        second.ShouldBe(first);
        first.ShouldEndWith("\n");
        first.ShouldNotContain("\r");
        ApiDocumentSource.Canonicalise(first).ShouldBe(first, "canonicalising is not idempotent.");
    }

    // ---- Negative controls -------------------------------------------------------------------
    //
    // A lint that has quietly stopped detecting is indistinguishable from a document that passes it.
    // Each control below breaks exactly one thing in a document that otherwise passes, and asserts that
    // the rule it breaks is the rule that fires.

    [Fact]
    public void TheLintPassesASoundDocument()
        => OpenApiLint.Inspect(Sound()).ShouldBeEmpty();

    [Fact]
    public void TheLintDetectsAnOperationThatDeclaresNoSecurity()
    {
        var document = Sound();
        Operation(document).Security = null;

        Rules(document).ShouldContain("operation-security");
    }

    [Fact]
    public void TheLintDetectsAnAnonymousOperationWithNoRecordedJustification()
    {
        var document = Sound();
        Operation(document).Security = [];
        Operation(document).Extensions?.Remove("x-tailor360-anonymous");

        Rules(document).ShouldContain("anonymous-justified");
    }

    [Fact]
    public void TheLintDetectsAMissingUniversalFailureResponse()
    {
        var document = Sound();
        Operation(document).Responses!.Remove("429");

        Rules(document).ShouldContain("problem-response-coverage");
    }

    [Fact]
    public void TheLintDetectsAnErrorThatIsNotProblemDetails()
    {
        var document = Sound();
        Operation(document).Responses!["400"] = new OpenApiResponse
        {
            Description = "A hand-rolled error shape.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String } },
            },
        };

        Rules(document).ShouldContain("problem-media-type");
    }

    [Fact]
    public void TheLintDetectsAnErrorThatDoesNotUseTheSharedSchema()
    {
        var document = Sound();
        Operation(document).Responses!["400"] = new OpenApiResponse
        {
            Description = "Problem details, described again from scratch.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                [ApiDocument.ProblemMediaType] = new()
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
                },
            },
        };

        Rules(document).ShouldContain("problem-schema");
    }

    [Fact]
    public void TheLintDetectsASuccessWithNoDocumentedBody()
    {
        var document = Sound();
        Operation(document).Responses!["201"] = new OpenApiResponse { Description = "Created." };

        Rules(document).ShouldContain("success-response-schema");
    }

    [Fact]
    public void TheLintDetectsASuccessWhoseBodyHasNoSchema()
    {
        var document = Sound();
        Operation(document).Responses!["201"] = new OpenApiResponse
        {
            Description = "Created.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new(),
            },
        };

        Rules(document).ShouldContain("success-response-schema");
    }

    /// <summary>
    /// The converse, so the rule is not simply "every success has a body": 204 is defined to have none,
    /// and a document that described one would be describing something the transport forbids.
    /// </summary>
    [Fact]
    public void TheLintDetectsABodyDocumentedOnANoContentResponse()
    {
        var document = Sound();
        Operation(document).Responses!.Remove("201");
        Operation(document).Responses!["204"] = new OpenApiResponse
        {
            Description = "Done.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
                },
            },
        };

        Rules(document).ShouldContain("success-response-body");
    }

    /// <summary>Positive control: a bodiless 204 is accepted, which is what most commands answer.</summary>
    [Fact]
    public void TheLintAcceptsANoContentResponseWithNoBody()
    {
        var document = Sound();
        Operation(document).Responses!.Remove("201");
        Operation(document).Responses!["204"] = new OpenApiResponse { Description = "Done." };

        OpenApiLint.Inspect(document).ShouldBeEmpty();
    }

    [Fact]
    public void TheLintDetectsAnUnpairedAuthorisationFailure()
    {
        var document = Sound();
        Operation(document).Responses!.Remove("403");

        Rules(document).ShouldContain("problem-response-pairing");
    }

    [Fact]
    public void TheLintDetectsARequestBodyWithNoExample()
    {
        var document = Sound();
        ((OpenApiRequestBody)Operation(document).RequestBody!).Content!["application/json"].Example = null;

        Rules(document).ShouldContain("request-example");
    }

    [Fact]
    public void TheLintDetectsAnOperationWithNoIdentifier()
    {
        var document = Sound();
        Operation(document).OperationId = null;

        Rules(document).ShouldContain("operation-id");
    }

    [Fact]
    public void TheLintDetectsARepeatedOperationIdentifier()
    {
        var document = Sound();
        var duplicate = new OpenApiPathItem();
        duplicate.AddOperation(HttpMethod.Post, Operation(document));
        document.Paths["/api/v1/things/other"] = duplicate;

        Rules(document).ShouldContain("operation-id-unique");
    }

    [Fact]
    public void TheLintDetectsAnOperationWithNoSummary()
    {
        var document = Sound();
        Operation(document).Summary = null;

        Rules(document).ShouldContain("operation-summary");
    }

    [Fact]
    public void TheLintDetectsATagNobodyDescribed()
    {
        var document = Sound();
        Operation(document).Tags = new HashSet<OpenApiTagReference>
        {
            new("Undescribed", document),
        };

        Rules(document).ShouldContain("tag-described");
    }

    [Fact]
    public void TheLintDetectsAPathOutsideTheVersionedSurface()
    {
        var document = Sound();
        document.Paths["/api/things"] = document.Paths["/api/v1/things"];
        document.Paths.Remove("/api/v1/things");

        Rules(document).ShouldContain("path-version");
    }

    [Fact]
    public void TheLintDetectsADeprecatedOperationThatNamesNoReplacement()
    {
        var document = Sound();
        Operation(document).Deprecated = true;

        Rules(document).ShouldContain("deprecation-names-replacement");
    }

    [Fact]
    public void TheLintDetectsADocumentWithNoErrorSchema()
    {
        var document = Sound();
        document.Components!.Schemas!.Remove(ApiDocument.ProblemSchema);

        Rules(document).ShouldContain("document-problem-schema");
    }

    private static IReadOnlyList<string> Rules(OpenApiDocument document)
        => [.. OpenApiLint.Inspect(document).Select(complaint => complaint.Rule)];

    private static OpenApiOperation Operation(OpenApiDocument document)
        => document.Paths["/api/v1/things"].Operations!.Values.First();

    /// <summary>A minimal document that passes every rule, for the controls to break one thing in.</summary>
    private static OpenApiDocument Sound()
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "A sound document",
                Version = "1.0.0",
                Description = "Enough of a document for the lint to have something to say.",
            },
            Servers = [new OpenApiServer { Url = "/" }],
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                {
                    [ApiDocument.ProblemSchema] = new OpenApiSchema { Type = JsonSchemaType.Object },
                },
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
                {
                    [ApiDocument.SessionSecurityScheme] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Cookie,
                        Name = "session",
                    },
                },
            },
            Tags = new HashSet<OpenApiTag> { new() { Name = "Things", Description = "Things." } },
        };

        var security = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiDocument.SessionSecurityScheme, document)] = [],
        };

        var operation = new OpenApiOperation
        {
            OperationId = "CreateThing",
            Summary = "Create a thing.",
            Tags = new HashSet<OpenApiTagReference> { new("Things", document) },
            Security = [security],
            RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/json"] = new()
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
                        Example = System.Text.Json.Nodes.JsonNode.Parse("""{"name":"a thing"}"""),
                    },
                },
            },
            Responses = new OpenApiResponses
            {
                ["201"] = new OpenApiResponse
                {
                    Description = "Created.",
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                    {
                        ["application/json"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Object },
                        },
                    },
                },
                ["400"] = Problem(document, "Not valid."),
                ["401"] = Problem(document, "No session."),
                ["403"] = Problem(document, "Not permitted."),
                ["429"] = Problem(document, "Throttled."),
                ["500"] = Problem(document, "Unexpected."),
            },
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                ["x-tailor360-authorisation"] = new JsonNodeExtension(
                    System.Text.Json.Nodes.JsonValue.Create("session")),
                ["x-tailor360-anonymous"] = new JsonNodeExtension(
                    System.Text.Json.Nodes.JsonValue.Create("#53")),
            },
        };

        var item = new OpenApiPathItem();
        item.AddOperation(HttpMethod.Post, operation);
        document.Paths = new OpenApiPaths { ["/api/v1/things"] = item };

        return document;
    }

    private static OpenApiResponse Problem(OpenApiDocument document, string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [ApiDocument.ProblemMediaType] = new()
            {
                Schema = new OpenApiSchemaReference(ApiDocument.ProblemSchema, document),
            },
        },
    };
}
