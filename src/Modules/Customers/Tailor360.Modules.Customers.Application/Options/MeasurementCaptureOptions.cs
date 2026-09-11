using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Customers.Application.Options;

/// <summary>
/// How long a garment stays half-measured before the draft stops being worth confirming.
/// </summary>
/// <remarks>
/// <para>
/// A measurement draft is not evidence and goes stale: a customer measured on Monday and a draft confirmed on
/// Friday would file Monday's numbers under Friday's date, with nobody able to say which day the tape was
/// actually held. INV-MSR-07 makes an expired draft one of the few things this system hard-deletes.
/// </para>
/// <para>
/// <strong>The number is a default, not a decision.</strong> Two days is long enough for a customer measured at
/// closing time to be confirmed the next morning, and short enough that nothing a week old is confirmed as if it
/// were fresh. What the shop actually wants is a product decision; it is configurable so that answering it later
/// is a setting rather than a release.
/// </para>
/// </remarks>
public sealed class MeasurementCaptureOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Customers:MeasurementCapture";

    /// <summary>How long a draft stays work in progress.</summary>
    [Required]
    public TimeSpan DraftLifetime { get; set; } = TimeSpan.FromDays(2);

    /// <summary>Whether the lifetime is one a shop could work to.</summary>
    /// <remarks>
    /// A lifetime under an hour would expire a draft while a garment is still being measured; one over thirty days
    /// would let a month-old measurement be confirmed as today's, which is the thing the expiry exists to prevent.
    /// </remarks>
    public bool IsLifetimeUsable
        => DraftLifetime >= TimeSpan.FromHours(1) && DraftLifetime <= TimeSpan.FromDays(30);
}
