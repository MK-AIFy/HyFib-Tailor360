namespace Tailor360.Platform.Abstractions.Time;

/// <summary>The production <see cref="IClock"/>, reading the host system clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
    {
        ArgumentNullException.ThrowIfNull(branchTimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }
}
