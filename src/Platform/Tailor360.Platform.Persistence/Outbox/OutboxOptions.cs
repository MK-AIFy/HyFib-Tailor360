using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Persistence.Outbox;

/// <summary>Outbox dispatcher configuration.</summary>
public sealed class OutboxOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Outbox";

    /// <summary>How many messages one dispatcher claims per cycle.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;

    /// <summary>How long to wait between polls when the last cycle found nothing.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00")]
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a claim is held. Long enough for a slow handler to finish, short enough that a crashed
    /// dispatcher's work is picked up again without an operator noticing.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:30:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How many attempts a message gets before it is dead-lettered.</summary>
    [Range(1, 50)]
    public int MaximumAttempts { get; set; } = 8;

    /// <summary>The base of the exponential backoff between attempts.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest backoff applied, so a message never disappears for hours.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan RetryMaximumDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The backlog age at which the health check reports the outbox degraded.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan BacklogWarningAge { get; set; } = TimeSpan.FromMinutes(2);
}
