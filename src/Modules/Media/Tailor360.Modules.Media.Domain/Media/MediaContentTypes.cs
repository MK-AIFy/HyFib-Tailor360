namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// The raster formats this module accepts, by declared <c>Content-Type</c>. Issue #592 checks the
/// declared type only, at the upload endpoint, so a bad or spoofed upload is refused before a single
/// byte reaches storage; issue #593 validates the decoded signature against these same formats before
/// anything downstream trusts the file (<c>docs/adr/0005-object-storage-authorised-delivery.md</c>
/// §4). Neither check alone is the security boundary — together they are.
/// </summary>
public static class MediaContentTypes
{
    /// <summary>The declared content types this module accepts.</summary>
    public static readonly IReadOnlyCollection<string> Allowed =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
    ];

    /// <summary>True when <paramref name="contentType"/> is one this module accepts.</summary>
    public static bool IsAllowed(string? contentType)
        => contentType is not null && Allowed.Contains(contentType);
}
