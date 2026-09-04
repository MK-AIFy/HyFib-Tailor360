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
