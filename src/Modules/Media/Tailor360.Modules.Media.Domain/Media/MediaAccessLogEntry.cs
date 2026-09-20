namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// One record of a request to stream a <see cref="MediaObject"/>'s bytes — who, which object, when,
/// under which correlation id. Written by issue #188's streaming endpoint on every request, successful
/// or refused; append-only in the database (the migration protects it with a trigger, the same way
/// every audit-shaped table in this system is).
/// </summary>
/// <remarks>
/// <para>
/// The table this maps to is created by issue #592, per <c>module-ownership.md</c> §3's "first issue"
/// convention — but nothing in #592 writes a row, so this type carries no public factory yet. Adding
/// one belongs to #188, which is what streams objects and logs the access.
/// </para>
/// <para>
/// Carries no foreign key to <c>media_objects</c>, the same reasoning <c>CatalogReferenceBreach</c>
/// gives for the same shape: the log outlives the interest in — and outlives the very existence of —
/// the object it is about, and a cascade from the object would delete the record of the access along
/// with it. It carries metadata only, never image bytes and never a client-supplied filename
/// (<c>docs/nfr/data-classification.md</c> §5.5 "In logs" — never).
/// </para>
/// </remarks>
public sealed class MediaAccessLogEntry
{
    private MediaAccessLogEntry()
    {
        // The persistence layer materialises instances through this constructor. Populated by #188.
    }

    /// <summary>Identity of the log entry.</summary>
    public Guid Id { get; private set; }

    /// <summary>The object that was requested. Not a foreign key — see this type's remarks.</summary>
    public Guid MediaObjectId { get; private set; }

    /// <summary>Who made the request.</summary>
    public Guid AccessedBy { get; private set; }

    /// <summary>When the request was made.</summary>
    public DateTimeOffset AccessedAt { get; private set; }

    /// <summary>The correlation id the request carried, where it carried one.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Which variant was requested.</summary>
    public MediaAccessVariant Variant { get; private set; }

    /// <summary>Whether the request was allowed to stream the bytes.</summary>
    public MediaAccessOutcome Outcome { get; private set; }
}

/// <summary>Which form of the object a streaming request named.</summary>
public enum MediaAccessVariant
{
    /// <summary>The full, promoted original.</summary>
    Original = 0,

    /// <summary>The preview derivative.</summary>
    Preview = 1,

    /// <summary>The thumbnail derivative.</summary>
    Thumbnail = 2,
}

/// <summary>Whether a streaming request was allowed to proceed.</summary>
public enum MediaAccessOutcome
{
    /// <summary>The bytes were streamed.</summary>
    Streamed = 0,

    /// <summary>The request was refused — wrong branch, no permission, no job assignment, or the object is not Ready.</summary>
    Denied = 1,
}
