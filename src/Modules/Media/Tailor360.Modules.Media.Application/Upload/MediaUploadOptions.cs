using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Media.Application.Upload;

/// <summary>
/// How large an upload this module accepts, bound from <c>Media:Upload</c>.
/// </summary>
/// <remarks>
/// The default is <c>docs/adr/0005-object-storage-authorised-delivery.md</c> §4's own stated figure —
/// sourced, not invented — kept as configuration rather than a domain constant because a deployment
/// may need to raise or lower it without a code change, the same reason the object-storage bulkhead
/// figures are options rather than constants.
/// </remarks>
public sealed class MediaUploadOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Media:Upload";

    /// <summary>The largest upload this module accepts, in bytes.</summary>
    [Range(1, long.MaxValue)]
    public long MaximumSizeBytes { get; set; } = 15 * 1024 * 1024;
}
