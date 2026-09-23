using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Decodes, validates, strips and derives an uploaded image (ADR-0012, issue #597). Given a stream of
/// declared-type bytes, produces a metadata-stripped, re-encoded original plus a thumbnail and a
/// preview derivative — or refuses the file with a specific, distinguishable reason before anything
/// downstream would trust it. Never given a destination and never told where the bytes came from or
/// go to; the caller (the worker job, issue #598) is what writes the result to object storage and to
/// <c>media_derivatives</c>.
/// </summary>
public interface IImageProcessor
{
    /// <summary>Decodes, validates and derives an image, or reports why it was refused.</summary>
    /// <param name="content">The declared-type bytes, read fully. Not disposed by the callee.</param>
    /// <param name="declaredContentType">
    /// The content type the caller declared — one of <c>image/jpeg</c>, <c>image/png</c>,
    /// <c>image/webp</c> or <c>image/heic</c>. Checked against the bytes' own signature, never trusted
    /// on its own.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ImageProcessingResult>> ProcessAsync(
        Stream content, string declaredContentType, CancellationToken cancellationToken = default);
}

/// <summary>Whether <see cref="IImageProcessor.ProcessAsync"/> accepted or refused the file.</summary>
public enum ImageProcessingStatus
{
    /// <summary>Decoded, stripped and derived; <see cref="ImageProcessingResult.Image"/> is populated.</summary>
    Accepted,

    /// <summary>
    /// Refused before anything downstream would trust the file; see
    /// <see cref="ImageProcessingResult.RejectionReason"/>.
    /// </summary>
    Rejected,
}

/// <summary>Why <see cref="IImageProcessor.ProcessAsync"/> refused a file, distinguishable per case.</summary>
public enum ImageRejectionReason
{
    /// <summary>The bytes' own signature does not match the declared content type.</summary>
    SignatureMismatch,

    /// <summary>The file carries a second format's marker alongside the declared one.</summary>
    PolyglotContent,

    /// <summary>Width or height on the longest edge exceeds the configured limit.</summary>
    DimensionsExceedLimit,

    /// <summary>Decoded pixel count (width × height) exceeds the configured limit.</summary>
    PixelCountExceedsLimit,

    /// <summary>The stream ended before decoding completed.</summary>
    Truncated,

    /// <summary>The bytes matched the declared signature but did not decode.</summary>
    Malformed,

    /// <summary>The signature was recognised but this adapter cannot decode the format.</summary>
    UnsupportedFormat,
}

/// <summary>One re-encoded, metadata-stripped image: its bytes, content type and pixel dimensions.</summary>
public sealed record ProcessedImageVariant(ReadOnlyMemory<byte> Bytes, string ContentType, int WidthPx, int HeightPx);

/// <summary>The stripped, re-encoded original plus its thumbnail and preview derivatives.</summary>
public sealed record ProcessedImage(
    ProcessedImageVariant Original, ProcessedImageVariant Thumbnail, ProcessedImageVariant Preview);

/// <summary>The outcome of <see cref="IImageProcessor.ProcessAsync"/>.</summary>
public sealed record ImageProcessingResult(
    ImageProcessingStatus Status, ImageRejectionReason? RejectionReason = null, ProcessedImage? Image = null);
