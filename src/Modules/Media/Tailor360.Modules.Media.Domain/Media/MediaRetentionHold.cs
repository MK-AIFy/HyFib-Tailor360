namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// A legal or business hold that suspends retention deletion for one <see cref="MediaObject"/>,
/// placed and released by issue #192's retention workflow.
/// </summary>
/// <remarks>
/// The table this maps to is created by issue #592, per <c>module-ownership.md</c> §3's "first issue"
/// convention — but nothing in #592 places a hold, so this type carries no public factory yet. Adding
/// one belongs to #192, which is what places and releases holds.
/// </remarks>
public sealed class MediaRetentionHold
{
    private MediaRetentionHold()
    {
        // The persistence layer materialises instances through this constructor. Populated by #192.
    }

    /// <summary>Identity of the hold.</summary>
    public Guid Id { get; private set; }

    /// <summary>The object the hold protects.</summary>
    public Guid MediaObjectId { get; private set; }

    /// <summary>Why the hold was placed. Never blank — CLAUDE.md §4 requires a stated reason.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Who placed it.</summary>
    public Guid PlacedBy { get; private set; }

    /// <summary>When it was placed.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>Who released it, or null while the hold is open.</summary>
    public Guid? ReleasedBy { get; private set; }

    /// <summary>When it was released, or null while the hold is open.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }
}
