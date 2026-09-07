using Shouldly;
using Tailor360.Platform.Abstractions.Versioning;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The ordering the client-version handshake refuses on. Every case here is one the middleware decides
/// something by: a version that parses is compared, and one that does not is treated as absent, so a
/// parse that quietly succeeded on nonsense would turn a typo into a refusal nobody could explain.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClientVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("1.2.3")]
    [InlineData("10.20.30")]
    [InlineData("1.2.3-alpha")]
    [InlineData("1.2.3-alpha.7")]
    [InlineData("1.2.3+abcdef")]
    [InlineData("1.2.3-alpha+abcdef")]
    [InlineData("  1.2.3  ")]
    public void AVersionIsParsed(string candidate)
        => ClientVersion.TryParse(candidate, out _).ShouldBeTrue(candidate);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1 . 2 . 3")]
    public void SomethingThatIsNotAVersionIsRefused(string? candidate)
        => ClientVersion.TryParse(candidate, out _).ShouldBeFalse(candidate ?? "null");

    /// <summary>
    /// A bound on the header, because this runs before authentication on every request and the value is
    /// whatever the caller sent.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongValueIsRefusedRatherThanParsed()
        => ClientVersion.TryParse(new string('1', ClientVersion.MaxLength + 1), out _).ShouldBeFalse();

    [Theory]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.2.3", "1.3.0")]
    [InlineData("1.2.3", "2.0.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.2.3-alpha", "1.2.3")]
    [InlineData("1.2.3-alpha", "1.2.3-beta")]
    public void TheOlderVersionOrdersFirst(string older, string newer)
    {
        ClientVersion.TryParse(older, out var first).ShouldBeTrue();
        ClientVersion.TryParse(newer, out var second).ShouldBeTrue();

        (first < second).ShouldBeTrue($"{older} should order before {newer}");
        (second > first).ShouldBeTrue($"{newer} should order after {older}");
    }

    /// <summary>
    /// Build metadata is not part of the version. Two builds of the same source differ there, and a
    /// comparison that saw them as different versions would refuse a client for having been rebuilt.
    /// </summary>
    [Fact]
    public void BuildMetadataDoesNotChangeTheVersion()
    {
        ClientVersion.TryParse("1.2.3+one", out var first).ShouldBeTrue();
        ClientVersion.TryParse("1.2.3+two", out var second).ShouldBeTrue();

        first.CompareTo(second).ShouldBe(0);
        first.ShouldBe(second);
    }

    [Fact]
    public void AVersionRoundTripsThroughItsText()
    {
        ClientVersion.TryParse("2.5.9-rc.1+ignored", out var version).ShouldBeTrue();

        version.ToString().ShouldBe("2.5.9-rc.1");
        version.Major.ShouldBe(2);
        version.Minor.ShouldBe(5);
        version.Patch.ShouldBe(9);
        version.PreRelease.ShouldBe("rc.1");
    }
}
