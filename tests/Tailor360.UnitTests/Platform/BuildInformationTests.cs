using Shouldly;
using Tailor360.Web.Configuration;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// <see cref="BuildInformation"/> is the one place this repository turns the SDK's
/// <c>VersionPrefix+SourceRevisionId</c> shape into what <c>GET /api/version</c> reports, and — until
/// this file — the first thing it did was never exercised by a test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BuildInformationTests
{
    [Fact]
    public void ResolveVersionPrefersTheInformationalVersionWhenPresent()
        => BuildInformation.ResolveVersion("0.1.0+abc123def456", "1.0.0.0").ShouldBe("0.1.0+abc123def456");

    [Fact]
    public void ResolveVersionFallsBackToTheAssemblyVersionWhenTheAttributeIsAbsent()
        => BuildInformation.ResolveVersion(null, "1.0.0.0").ShouldBe("1.0.0.0");

    [Fact]
    public void ResolveVersionFallsBackToAStableDefaultWhenBothAreAbsent()
        => BuildInformation.ResolveVersion(null, null).ShouldBe("0.0.0");

    [Fact]
    public void ResolveVersionTreatsAnEmptyInformationalVersionAsAbsent()
        => BuildInformation.ResolveVersion(string.Empty, "1.0.0.0").ShouldBe("1.0.0.0");

    [Fact]
    public void ExtractHashSplitsAPreReleaseVersionOnTheFirstPlusOnly()
        => BuildInformation.ExtractHash("1.2.0-rc.1+abc123def456").ShouldBe("abc123def456");

    [Fact]
    public void ExtractHashTruncatesALongerHashToTwelveCharacters()
        => BuildInformation.ExtractHash("1.2.0+abcdef0123456789").ShouldBe("abcdef012345");

    [Fact]
    public void ExtractHashFallsBackToLocalWhenThereIsNoRevisionSuffix()
        => BuildInformation.ExtractHash("1.2.0").ShouldBe("local");

    [Fact]
    public void ExtractHashFallsBackToLocalWhenThePlusCarriesNothing()
        => BuildInformation.ExtractHash("1.2.0+").ShouldBe("local");

    /// <summary>
    /// The whole point of the fallback chain: whatever build produced this test run, <c>Version</c> is
    /// never null or empty, so a caller of <c>GET /api/version</c> is never told nothing.
    /// </summary>
    [Fact]
    public void VersionIsNeverNullOrEmpty()
        => BuildInformation.Version.ShouldNotBeNullOrWhiteSpace();
}
