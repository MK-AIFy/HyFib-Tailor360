using Tailor360.Modules.Media.Domain.Media;

namespace Tailor360.Modules.Media.Api;

/// <summary>What a caller is told about an object they just uploaded (ARCH-013: never the domain type itself).</summary>
/// <param name="Id">Identity of the object.</param>
/// <param name="Purpose">What the image was captured or supplied for.</param>
/// <param name="Status">Where the object is in the upload pipeline.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="SizeBytes">The size of the uploaded bytes.</param>
public sealed record MediaObjectPayload(
    Guid Id,
    string Purpose,
    string Status,
    string ContentType,
    long SizeBytes)
{
    /// <summary>Maps the domain object to the payload a caller is shown. Never the object key: that is never handed out (CLAUDE.md §4 rule 9).</summary>
    /// <param name="mediaObject">The uploaded object.</param>
    public static MediaObjectPayload From(MediaObject mediaObject)
    {
        ArgumentNullException.ThrowIfNull(mediaObject);

        return new MediaObjectPayload(
            mediaObject.Id,
            mediaObject.Purpose.ToString(),
            mediaObject.Status.ToString(),
            mediaObject.ContentType,
            mediaObject.SizeBytes);
    }
}
