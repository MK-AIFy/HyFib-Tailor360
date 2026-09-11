using System.Globalization;
using Shouldly;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The Indian financial year runs 1 April to 31 March, and document numbering is scoped to it, so the
/// boundary has to be exact.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinancialYearTests
{
    [Theory]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2026, 9, 4, 2026)]
    [InlineData(2026, 12, 31, 2026)]
    [InlineData(2027, 1, 1, 2026)]
    [InlineData(2027, 3, 31, 2026)]
    [InlineData(2027, 4, 1, 2027)]
    public void StartsOnTheFirstOfApril(int year, int month, int day, int expected)
        => IndiaTimeZone.FinancialYearStarting(new DateOnly(year, month, day)).ShouldBe(expected);

    [Theory]
    [InlineData(2026, 4, 1, "2026-27")]
    [InlineData(2027, 3, 31, "2026-27")]
    [InlineData(2027, 4, 1, "2027-28")]
    [InlineData(2099, 12, 1, "2099-00")]
    public void LabelsTheYearAsShopsWriteIt(int year, int month, int day, string expected)
        => IndiaTimeZone.FinancialYearLabel(new DateOnly(year, month, day)).ShouldBe(expected);

    [Fact]
    public void ResolvesIndianStandardTime()
        => IndiaTimeZone.Instance.GetUtcOffset(new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc))
            .ShouldBe(TimeSpan.FromMinutes(330));

    [Fact]
    public void ResolvesTheBranchTimezoneFromTheTimezoneDatabaseOnEveryPlatform()
    {
        // conventions.md section 2.2: a branch's timezone is an IANA identifier resolved through a
        // tz-database-backed API, and a fixed +05:30 offset is never hard-coded. Invariant globalisation
        // drops the ICU data that maps an IANA identifier on Windows, so this resolution fails there while
        // still passing on Linux — which is exactly the shape of defect this test exists to catch. Asserting
        // on the identifier rather than the offset keeps it honest: an offset can be faked, a lookup cannot.
        TimeZoneInfo.TryFindSystemTimeZoneById(IndiaTimeZone.Id, out var resolved).ShouldBeTrue(
            $"'{IndiaTimeZone.Id}' must resolve from the tz database. If this fails, check that "
            + "InvariantGlobalization is false in Directory.Build.props.");

        resolved.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("en-IN")]
    [InlineData("ta-IN")]
    public void ServesTheDeploymentsCulturesAsRealCultures(string locale)
    {
        // The two locales the deployment serves (ADR-0003). Under invariant globalisation every culture
        // collapses to the invariant one, so this would silently pass its name back while formatting
        // nothing correctly. Comparing against the invariant culture is what detects that collapse.
        var culture = CultureInfo.GetCultureInfo(locale);

        culture.Name.ShouldBe(locale);
        culture.LCID.ShouldNotBe(CultureInfo.InvariantCulture.LCID);
    }

    [Fact]
    public void ConvertsAnInstantToTheBranchLocalDate()
    {
        // 18:45 UTC is already the next day in Indian Standard Time, which is why business dates are
        // evaluated in the branch timezone rather than in UTC.
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 4, 18, 45, 0, TimeSpan.Zero));

        clock.TodayIn(IndiaTimeZone.Instance).ShouldBe(new DateOnly(2026, 9, 5));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }
}
