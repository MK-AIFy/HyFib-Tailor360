using Shouldly;
using Tailor360.Platform.Observability.Correlation;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The correlation identifier is written into logs, so a caller must not be able to choose a value that
/// forges a log line or bloats the store.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorrelationTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("trace-id_42")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void KeepsAWellFormedIdentifierSuppliedByTheClient(string supplied)
        => CorrelationContext.SanitiseOrCreate(supplied).ShouldBe(supplied);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("newline\ninjected")]
    [InlineData("semi;colon")]
    [InlineData("quote\"mark")]
    public void ReplacesAnythingThatCouldForgeALogLine(string? supplied)
    {
        var result = CorrelationContext.SanitiseOrCreate(supplied);

        result.ShouldNotBe(supplied);
        result.Length.ShouldBe(32);
        result.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c));
    }

    [Fact]
    public void ReplacesAnOverlongIdentifier()
    {
        var tooLong = new string('a', CorrelationContext.MaxLength + 1);

        CorrelationContext.SanitiseOrCreate(tooLong).ShouldNotBe(tooLong);
    }

    [Fact]
    public void GeneratesADistinctIdentifierEachTime()
        => CorrelationContext.SanitiseOrCreate(null).ShouldNotBe(CorrelationContext.SanitiseOrCreate(null));
}
