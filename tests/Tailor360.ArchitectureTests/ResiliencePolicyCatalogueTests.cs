using System.Text.RegularExpressions;
using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// <c>docs/architecture/resilience-policies.md</c>'s own acceptance criterion: "every policy it claims is
/// implemented is traceable to a file in <c>src/</c>" — asserted by a test over the document's file
/// references, not by reading. A citation that stops resolving because a file moved is exactly the kind
/// of drift a reader cannot catch by eye.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class ResiliencePolicyCatalogueTests
{
    [Fact]
    public void EveryFileReferenceInTheResiliencePolicyCatalogueResolves()
    {
        var path = Path.Combine(RepositoryLayout.Root, "docs", "architecture", "resilience-policies.md");
        File.Exists(path).ShouldBeTrue($"the catalogue itself must exist at {path}.");

        var text = File.ReadAllText(path);
        var references = FileReferencePattern().Matches(text)
            .Select(match => match.Groups["path"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        references.ShouldNotBeEmpty("the catalogue should cite at least one file backing a claimed control.");

        var missing = references
            .Where(reference => !File.Exists(Path.Combine(RepositoryLayout.Root, reference.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        missing.ShouldBeEmpty(
            $"every file the catalogue cites must exist: {string.Join(", ", missing)}");
    }

    [GeneratedRegex(@"`(?<path>(?:src|tests)/[A-Za-z0-9_.\-/]+\.cs)`", RegexOptions.None, 500)]
    private static partial Regex FileReferencePattern();
}
