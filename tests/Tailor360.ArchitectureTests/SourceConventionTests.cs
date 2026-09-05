using System.Text.RegularExpressions;
using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Rules that are about how code is written rather than about which project references which. They are
/// checked against the source text, which catches a violation the compiler is happy with.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class SourceConventionTests
{
    /// <summary>
    /// ARCH-014: the ambient clock is off limits. Time has to arrive through <c>IClock</c> or a
    /// behaviour that depends on a date cannot be tested without waiting for that date.
    /// </summary>
    [Fact]
    public void Arch014_AmbientClockIsNotUsedOutsideTheClockAbstraction()
    {
        string[] sanctioned = ["SystemClock.cs"];

        var violations = ScanSource(AmbientClockPattern(), sanctioned);

        violations.ShouldBeEmpty(
            "ARCH-014: read the time through IClock instead of the ambient clock. Offending lines:\n"
            + string.Join('\n', violations));
    }

    /// <summary>
    /// ARCH-015: entity identifiers come from <c>IIdGenerator</c>, which produces time-ordered UUIDv7
    /// values. A random v4 identifier scattered through the tables costs index locality on every insert.
    /// </summary>
    [Fact]
    public void Arch015_RandomIdentifiersAreNotCreatedOutsideTheIdGenerator()
    {
        string[] sanctioned = ["UuidV7IdGenerator.cs"];

        var violations = ScanSource(NewGuidPattern(), sanctioned);

        violations.ShouldBeEmpty(
            "ARCH-015: allocate identifiers through IIdGenerator. Offending lines:\n"
            + string.Join('\n', violations));
    }

    /// <summary>
    /// ARCH-016: outbound HTTP goes through <c>IOutboundHttp</c>, which is where the server-side request
    /// forgery policy, the timeout and the response-size cap live. A hand-rolled client bypasses all three.
    /// </summary>
    [Fact]
    public void Arch016_HttpClientIsNotConstructedDirectly()
    {
        string[] sanctioned = ["OutboundHttpClient.cs"];

        var violations = ScanSource(HttpClientConstructionPattern(), sanctioned);

        violations.ShouldBeEmpty(
            "ARCH-016: make outbound calls through IOutboundHttp. Offending lines:\n"
            + string.Join('\n', violations));
    }

    /// <summary>
    /// ARCH-005: a module's persistence may declare only its own schema. Two modules sharing a table is
    /// the failure mode that turns a modular monolith back into a single tangled schema.
    /// </summary>
    [Fact]
    public void Arch005_ModulePersistenceDeclaresOnlyItsOwnSchema()
    {
        var failures = new List<string>();

        foreach (var project in RepositoryLayout.ModuleLayer("Infrastructure"))
        {
            var expected = project.Module!.ToLowerInvariant();

            foreach (var file in project.SourceFiles)
            {
                foreach (Match match in SchemaDeclarationPattern().Matches(File.ReadAllText(file)))
                {
                    var declared = match.Groups["schema"].Value;
                    if (!string.Equals(declared, expected, StringComparison.Ordinal))
                    {
                        failures.Add($"{Relative(file)} declares schema '{declared}', expected '{expected}'");
                    }
                }
            }
        }

        failures.ShouldBeEmpty(
            "ARCH-005: a module may map tables only in its own schema. Offending declarations:\n"
            + string.Join('\n', failures));
    }

    private static IReadOnlyList<string> ScanSource(Regex pattern, IReadOnlyList<string> sanctionedFileNames)
    {
        var sourceRoot = Path.Combine(RepositoryLayout.Root, "src");

        var files = RepositoryLayout.Projects
            .Where(project => !project.IsTestProject
                && project.Directory.StartsWith(sourceRoot, StringComparison.Ordinal))
            .SelectMany(project => project.SourceFiles)
            .Select(file => (Path: Relative(file), Text: File.ReadAllText(file)));

        return SourceScanner.Scan(files, pattern, sanctionedFileNames);
    }

    private static string Relative(string path) => Path.GetRelativePath(RepositoryLayout.Root, path);

    [GeneratedRegex(@"\bDateTime(Offset)?\s*\.\s*(UtcNow|Now|Today)\b", RegexOptions.None, 500)]
    private static partial Regex AmbientClockPattern();

    [GeneratedRegex(@"\bGuid\s*\.\s*NewGuid\s*\(", RegexOptions.None, 500)]
    private static partial Regex NewGuidPattern();

    [GeneratedRegex(@"\bnew\s+HttpClient\s*\(", RegexOptions.None, 500)]
    private static partial Regex HttpClientConstructionPattern();

    [GeneratedRegex(@"HasDefaultSchema\s*\(\s*""(?<schema>[a-z_]+)""", RegexOptions.None, 500)]
    private static partial Regex SchemaDeclarationPattern();
}
