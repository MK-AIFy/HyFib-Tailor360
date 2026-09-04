namespace Tailor360.Platform.Abstractions.Time;

/// <summary>
/// The only sanctioned source of the current time. Architecture rule ARCH-014 forbids
/// <c>DateTime.Now</c>, <c>DateTime.UtcNow</c> and <c>DateTimeOffset.Now</c> outside this abstraction
/// so that every time-dependent behaviour is testable.
/// </summary>
public interface IClock
{
    /// <summary>The current instant in UTC. All persisted timestamps are UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// The current local date in the supplied branch timezone. Business dates (financial year,
    /// promised delivery dates, cashier sessions) are evaluated in the branch timezone, never in UTC.
    /// </summary>
    /// <param name="branchTimeZone">An IANA timezone identifier, for example <c>Asia/Kolkata</c>.</param>
    DateOnly TodayIn(TimeZoneInfo branchTimeZone);
}
