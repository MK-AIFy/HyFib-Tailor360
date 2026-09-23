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
    /// <returns>
    /// An accepted image, or a rejection naming the reason. Every problem with the bytes themselves is a rejection,
    /// and a rejection is final: retrying the same bytes gets the same answer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An exception — an allocation failure, a missing native library, cancellation — means the environment or the
    /// adapter failed, never that the file was bad, so it is the caller's retry policy that should see it. Real memory
    /// exhaustion usually ends the process rather than throwing, so a caller must also bound its attempts durably —
    /// recorded before each attempt — or one pathological file can crash-loop it.
    /// </para>
    /// <para>
    /// Only the returned variants are safe to store or serve: they are re-encoded from decoded pixels. The bytes the
    /// caller passed in are not, whatever the outcome — the polyglot check is defence in depth, not a guarantee.
    /// </para>
    /// </remarks>
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
