using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.ContractTests;

/// <summary>
/// Every endpoint that is not gated on a permission cites where its exposure was reviewed. This asserts
/// that the citation points at something.
/// </summary>
/// <remarks>
/// <para>
/// The exemption register ARCH-007 relies on is those citations. When the document one of them names
/// does not exist, the register is circular: each endpoint says "this was reviewed over there", and
/// there is no over there. That is exactly the state the authentication module shipped in — sixteen
/// declarations naming a threat model that had never been written, while the whole set passed review
/// because "reviewedIn is non-empty" was all anything checked.
/// </para>
/// <para>
/// The markdown link checker cannot catch this: the citations live in C# string literals, not in links,
/// so <c>./scripts/dev docs</c> reports every relative link resolving while the reference dangles.
/// </para>
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointReviewCitationTests(WebHostFixture fixture)
{
    [Fact]
    public void EveryReviewCitationNamesADocumentThatExists()
    {
        var root = RepositoryRoot();
        var dangling = new List<string>();

        foreach (var (endpoint, citation) in Citations())
        {
            foreach (var path in DocumentPaths(citation))
            {
                if (!File.Exists(Path.Combine(root, path)))
                {
                    dangling.Add($"{endpoint} cites {path}, which is not in the repository.");
                }
            }
        }

        dangling.ShouldBeEmpty(
            "These endpoints name a review document that does not exist, so their deny-by-default "
            + "exemption cites nothing:\n" + string.Join('\n', dangling.Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryReviewCitationNamesAnIssueOrADocument()
    {
        foreach (var (endpoint, citation) in Citations())
        {
            var names = citation.Contains('#', StringComparison.Ordinal)
                || DocumentPaths(citation).Count > 0;

            names.ShouldBeTrue(
                $"{endpoint} records its review as \"{citation}\", which names neither an issue nor a "
                + "document. ARCH-007 requires the exposure to have been reviewed somewhere a reader "
                + "can go and look.");
        }
    }

    /// <summary>
    /// The review citation on every endpoint that declares one, whether it is anonymous or gated on the
    /// caller's own session. The self-service metadata is read reflectively because it is declared by a
    /// module and this project composes the host without referencing any module directly.
    /// </summary>
    private List<(string Endpoint, string Citation)> Citations()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var citations = new List<(string, string)>();

        foreach (var endpoint in sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "(unnamed route)";

            if (endpoint.Metadata.GetMetadata<AnonymousJustificationMetadata>() is { } anonymous)
            {
                citations.Add((route, anonymous.ReviewedIn));
            }

            foreach (var metadata in endpoint.Metadata)
            {
                if (metadata?.GetType().Name is not "SelfServiceMetadata")
                {
                    continue;
                }

                var reviewedIn = metadata.GetType()
                    .GetProperty("ReviewedIn", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(metadata) as string;

                if (!string.IsNullOrWhiteSpace(reviewedIn))
                {
                    citations.Add((route, reviewedIn));
                }
            }
        }

        citations.Count.ShouldBeGreaterThan(
            0, "No endpoint declares a review citation, so this rule is asserting nothing.");

        return citations;
    }

    /// <summary>
    /// The repository-relative document paths a citation names. A citation is prose — "#23,
    /// docs/security/threat-models/authentication.md" — so what is checked is every whitespace- or
    /// comma-separated token that looks like a path to a markdown file.
    /// </summary>
    private static List<string> DocumentPaths(string citation)
        => [.. citation
            .Split([' ', ',', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.EndsWith(".md", StringComparison.OrdinalIgnoreCase))];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root above " + AppContext.BaseDirectory);
    }
}
