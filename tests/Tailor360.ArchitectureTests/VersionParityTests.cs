using System.Text.RegularExpressions;
using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// The repository has exactly one release version, in <c>VERSION</c> at the repository root
/// (<c>docs/process/versioning.md</c>). <c>clients/pwa/package.json</c> declares its own and the .NET
/// build never reads it, so nothing before this test stopped the two from disagreeing about which
/// release is being built.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class VersionParityTests
{
    [Fact]
    public void TheVersionFileAndThePwaPackageAgreeOnASemanticVersion()
    {
        var version = ReadVersionFile();
        var packageVersion = ReadPackageJsonVersion();

        var complaint = CheckParity(version, packageVersion);

        complaint.ShouldBeNull(complaint);
    }

    /// <summary>
    /// The detector <see cref="NegativeControlTests"/> exercises directly with synthetic values, per
    /// <c>docs/architecture/architecture-rules.md</c> section 2's negative-control row: <c>null</c> when
    /// <paramref name="versionFileContent"/> is a semantic version and the two inputs agree, otherwise a
    /// message naming both files and both values. Never "helpfully" normalises either input.
    /// </summary>
    internal static string? CheckParity(string versionFileContent, string packageJsonVersion)
    {
        if (!SemanticVersionPattern().IsMatch(versionFileContent))
        {
            return $"VERSION ('{versionFileContent}') is not a semantic version. Expected " +
                "MAJOR.MINOR.PATCH, with an optional -prerelease suffix such as '1.2.3-rc.1'.";
        }

        if (!string.Equals(versionFileContent, packageJsonVersion, StringComparison.Ordinal))
        {
            return $"VERSION ('{versionFileContent}') and clients/pwa/package.json's \"version\" " +
                $"('{packageJsonVersion}') disagree. Both name the same release and must change together.";
        }

        return null;
    }

    private static string ReadVersionFile()
        => File.ReadAllText(Path.Combine(RepositoryLayout.Root, "VERSION")).Trim();

    private static string ReadPackageJsonVersion()
    {
        var json = File.ReadAllText(
            Path.Combine(RepositoryLayout.Root, "clients", "pwa", "package.json"));

        var match = PackageJsonVersionPattern().Match(json);
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex(
        "\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex PackageJsonVersionPattern();
}
