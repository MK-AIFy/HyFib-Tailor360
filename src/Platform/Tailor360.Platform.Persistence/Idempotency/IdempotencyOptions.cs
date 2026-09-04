using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Persistence.Idempotency;

/// <summary>Retention for idempotency records.</summary>
public sealed class IdempotencyOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Idempotency";

    /// <summary>
    /// The longest a request may sit in a device's offline queue. A scan taken on a tablet that goes
    /// home in someone's bag on Friday can arrive on Monday.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan MaximumOfflineQueueAge { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// How long a record is kept. It must exceed the offline-queue age with margin, or a replayed
    /// request would find its record already deleted and would be executed a second time. Kept at twice
    /// the queue age, with a floor, so the two values cannot drift apart in configuration.
    /// </summary>
    public TimeSpan Retention => TimeSpan.FromTicks(Math.Max(
        MaximumOfflineQueueAge.Ticks * 2,
        TimeSpan.FromDays(7).Ticks));
}
