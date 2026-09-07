namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Media module: uploading, streaming and deleting stored images.
/// </summary>
/// <remarks>
/// Holding <see cref="Read"/> is necessary and never sufficient. Every object is streamed by an
/// endpoint that re-authorises the request against the owning entity — who captured it, which garment
/// job it belongs to, whether the caller is the assigned Tailor — and writes the access log
/// (<c>docs/architecture/module-ownership.md</c> section 5.4,
/// <c>docs/nfr/data-classification.md</c> DC-08 and DC-09). This permission is the outer gate, not the
/// inner one.
/// </remarks>
public static class MediaPermissions
{
    /// <summary>Upload an image into the quarantine bucket.</summary>
    public const string Upload = "media.upload";

    /// <summary>Stream an image the caller is separately authorised to see.</summary>
    public const string Read = "media.read";

    /// <summary>Delete a stored image before its retention date.</summary>
    public const string Delete = "media.delete";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(Upload, "Upload an image for scanning and re-encoding.", PermissionModules.Media),
        new(Read, "Stream a stored image, subject to the per-object authorisation check.",
            PermissionModules.Media),
        new(Delete, "Delete a stored image before its retention date.",
            PermissionModules.Media, RequiresReason: true),
    ];
}
