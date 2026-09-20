namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// Tracks one object awaiting the worker's validation and malware scan (issue #593).
/// </summary>
/// <remarks>
/// A separate row from <see cref="MediaObject"/> rather than columns on it, because it is the worker's
/// working state — attempts, the scan outcome, the last error — none of which belongs on the object's
/// own permanent record. Issue #592 creates the row when the upload lands; issue #593 is the only
/// thing that ever updates it.
/// </remarks>
public sealed class MediaQuarantineEntry
{
    private MediaQuarantineEntry()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private MediaQuarantineEntry(Guid mediaObjectId, string quarantineKey, DateTimeOffset now)
    {
        MediaObjectId = mediaObjectId;
        QuarantineKey = quarantineKey;
        AttemptCount = 0;
        CreatedAt = now;
    }

    /// <summary>The object this entry tracks. Also this table's primary key: at most one entry per object.</summary>
    public Guid MediaObjectId { get; private set; }

    /// <summary>The quarantine-bucket key the bytes were written under.</summary>
    public string QuarantineKey { get; private set; } = string.Empty;

    /// <summary>How many times the worker has attempted to process this object.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The scan outcome, once the worker has one. Null until issue #593 runs.</summary>
    public MediaScanOutcome? ScanOutcome { get; private set; }

    /// <summary>When the scan completed, or null until it has.</summary>
    public DateTimeOffset? ScannedAt { get; private set; }

    /// <summary>What the last failed attempt said, or null when there has not been one.</summary>
    public string? LastError { get; private set; }

    /// <summary>When the object entered quarantine.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates the entry for a freshly uploaded object.</summary>
    /// <param name="mediaObjectId">The object this entry tracks.</param>
    /// <param name="quarantineKey">The quarantine-bucket key the bytes were written under.</param>
    /// <param name="now">The current instant.</param>
    public static MediaQuarantineEntry Create(Guid mediaObjectId, string quarantineKey, DateTimeOffset now)
        => new(mediaObjectId, quarantineKey, now);
}

/// <summary>The malware scan's answer for one object (issue #593).</summary>
public enum MediaScanOutcome
{
    /// <summary>No threat found.</summary>
    Clean = 0,

    /// <summary>A threat was found. The object is rejected and never promoted.</summary>
    Infected = 1,

    /// <summary>The scanner could not answer. Treated the same as infected — never promoted.</summary>
    Unavailable = 2,
}
