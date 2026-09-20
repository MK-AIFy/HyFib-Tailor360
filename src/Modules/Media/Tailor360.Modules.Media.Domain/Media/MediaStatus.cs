namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// Where an object is in the upload pipeline (<c>docs/adr/0005-object-storage-authorised-delivery.md</c>
/// §4). <see cref="MediaObject"/> enforces the transitions; <see cref="Ready"/>,
/// <see cref="Rejected"/> and <see cref="Deleted"/> are terminal.
/// </summary>
public enum MediaStatus
{
    /// <summary>The bytes are written to the quarantine bucket; nothing has validated them yet.</summary>
    Uploading = 0,

    /// <summary>Queued for the worker's validate/scan/strip/derive/promote pipeline (issue #593).</summary>
    Quarantined = 1,

    /// <summary>Promoted to its owned ready prefix, validated, scanned clean, and resolvable.</summary>
    Ready = 2,

    /// <summary>Failed validation or the malware scan. Never promoted; never resolvable as Ready.</summary>
    Rejected = 3,

    /// <summary>Deleted by retention or an explicit request (issue #192). The row is kept, tombstoned, so a reference resolves to "deleted" rather than a broken link.</summary>
    Deleted = 4,
}
