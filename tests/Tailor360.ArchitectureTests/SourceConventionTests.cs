using System.Text.RegularExpressions;
using Shouldly;
using Tailor360.Platform.Persistence.Auditing;

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
    /// the failure mode that turns a modular monolith back into a single tangled schema. The one named
    /// exception — a module context additionally mapping <c>platform.audit_events</c> through
    /// <see cref="AuditEventMapping"/>, so its own audit entry commits in the same
    /// <c>SaveChangesAsync</c> call as the change it describes (ADR-0015, issue #179) — is recognised by
    /// name, not by switching the check off: anything else reaching outside a module's own schema, by an
    /// explicit <c>ToTable(name, schema)</c> overload or otherwise, still fails.
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
                var text = File.ReadAllText(file);

                foreach (Match match in SchemaDeclarationPattern().Matches(text))
                {
                    var declared = match.Groups["schema"].Value;
                    if (!string.Equals(declared, expected, StringComparison.Ordinal))
                    {
                        failures.Add($"{Relative(file)} declares schema '{declared}', expected '{expected}'");
                    }
                }

                foreach (var (schema, table) in ForeignSchemaMappings(text))
                {
                    if (string.Equals(schema, expected, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsSanctionedSharedMechanismTable(schema, table))
                    {
                        continue;
                    }

                    failures.Add(
                        $"{Relative(file)} maps table '{table}' into schema '{schema}', expected '{expected}'");
                }
            }
        }

        failures.ShouldBeEmpty(
            "ARCH-005: a module may map tables only in its own schema, with ARCH-005's one named "
            + "exception for platform.audit_events through AuditEventMapping. Offending declarations:\n"
            + string.Join('\n', failures));
    }

    /// <summary>
    /// Every table an Infrastructure file maps into a schema other than through
    /// <c>HasDefaultSchema</c>: an explicit <c>ToTable(name, schema)</c> overload literal, and a call to
    /// <see cref="AuditEventMapping.Configure"/>, which is read as declaring exactly
    /// <c>platform</c>/<c>audit_events</c> because that pairing is the only thing the helper ever maps
    /// (see its own remarks) — a textual proxy for a runtime fact, the same idiom
    /// <see cref="SchemaDeclarationPattern"/> already uses for <c>HasDefaultSchema</c>.
    /// </summary>
    internal static IEnumerable<(string Schema, string Table)> ForeignSchemaMappings(string text)
    {
        foreach (Match match in ExplicitTableSchemaPattern().Matches(text))
        {
            yield return (match.Groups["schema"].Value, match.Groups["table"].Value);
        }

        if (AuditEventMappingCallPattern().IsMatch(text))
        {
            yield return (AuditEventMapping.SchemaName, SanctionedSharedMechanismTable);
        }
    }

    /// <summary>
    /// ARCH-005's one named exception, checked by the exact pair rather than by schema alone: any other
    /// table reaching into <c>platform</c> — or this table reaching anywhere else — still fails.
    /// </summary>
    internal static bool IsSanctionedSharedMechanismTable(string schema, string table)
        => string.Equals(schema, AuditEventMapping.SchemaName, StringComparison.Ordinal)
            && string.Equals(table, SanctionedSharedMechanismTable, StringComparison.Ordinal);

    /// <summary>The one table ARCH-005 names as shareable, and only through <see cref="AuditEventMapping"/>.</summary>
    internal const string SanctionedSharedMechanismTable = "audit_events";

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

    [GeneratedRegex(
        @"ToTable\s*\(\s*""(?<table>[a-zA-Z0-9_]+)""\s*,\s*""(?<schema>[a-z_]+)""", RegexOptions.None, 500)]
    private static partial Regex ExplicitTableSchemaPattern();

    [GeneratedRegex(@"\bAuditEventMapping\s*\.\s*Configure\s*\(", RegexOptions.None, 500)]
    private static partial Regex AuditEventMappingCallPattern();
}
