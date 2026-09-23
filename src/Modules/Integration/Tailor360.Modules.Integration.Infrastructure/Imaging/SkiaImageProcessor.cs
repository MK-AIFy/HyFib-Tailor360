using System.Text;
using SkiaSharp;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Integration.Infrastructure.Imaging;

/// <summary>
/// Decodes, validates, strips and derives uploaded images with SkiaSharp (ADR-0012, issue #597).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Stripping is a side effect of re-encoding, not a separate step.</strong> Every accepted file
/// is fully decoded to raw pixels and re-encoded from scratch; a decoded <see cref="SKBitmap"/> carries
/// no memory of the source file's segments, so EXIF, ICC, XMP and GPS data cannot survive the round
/// trip whether or not this class ever looks at them directly. This is also why a polyglot's hidden
/// second payload cannot reach a caller: only the pixels the codec actually decoded are ever written
/// out.
/// </para>
/// <para>
/// <strong>EXIF orientation is the one tag baked in rather than discarded.</strong> Stripping it without
/// applying it first would leave the majority of phone-captured photos sideways, since a camera sensor
/// is landscape-native and almost every portrait photo carries a non-default orientation tag.
/// </para>
/// <para>
/// <strong>HEIC is accepted as a declared type but never actually decodes.</strong> SkiaSharp has no
/// HEIF decoder on any platform (confirmed against this repository's own Linux worker image, and
/// against SkiaSharp's own upstream tracker: mono/SkiaSharp#2887 and #1700, both open feature requests
/// as of issue #597) — every HEIC upload is refused by <see cref="ProcessBuffer"/>, honestly rather
/// than silently. Closing this gap needs a third-party add-on this adapter does not carry
/// (<c>SkiaSharp.Heic</c>, <c>Openize.HEIC</c>) and is tracked separately rather than folded in here.
/// </para>
/// </remarks>
public sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>#597's own stated limit: reject anything longer than this on its longest edge.</summary>
    private const int MaxDimensionPx = 12_000;

    /// <summary>#597's own stated limit: reject anything with more decoded pixels than this (40 MP).</summary>
    private const long MaxPixelCount = 40_000_000;

    /// <summary>
    /// Implementation defaults, not sourced product figures — nothing today reads these from
    /// configuration, and nobody has asked for a specific thumbnail or preview size yet.
    /// </summary>
    private const int ThumbnailMaxEdgePx = 320;

    private const int PreviewMaxEdgePx = 1600;
    private const int JpegQuality = 90;

    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";
    private const string WebpContentType = "image/webp";
    private const string HeicContentType = "image/heic";

    /// <summary>
    /// Byte sequences that mean "a second format's structure is present", checked across the whole
    /// buffer once the declared signature has already matched. False positives are astronomically
    /// unlikely at these marker lengths against real photograph bytes; true positives are what a
    /// polyglot upload — an image with an appended archive, script or document — looks like on the wire.
    /// </summary>
    private static readonly byte[][] EmbeddedFormatMarkers =
    [
        "PK\x03\x04"u8.ToArray(),
        "PK\x05\x06"u8.ToArray(),
        "%PDF-"u8.ToArray(),
        "<script"u8.ToArray(),
        "<?php"u8.ToArray(),
    ];

    private static readonly string[] HeicBrands =
        ["heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1"];

    /// <inheritdoc />
    public async Task<Result<ImageProcessingResult>> ProcessAsync(
        Stream content, string declaredContentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredContentType);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var outcome = ProcessBuffer(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), declaredContentType);
            return Result.Success(outcome);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException
            or DllNotFoundException or TypeInitializationException
            or EntryPointNotFoundException or BadImageFormatException))
        {
            // The input is untrusted and adversarial by construction; whatever SkiaSharp's managed
            // binding throws on genuinely malformed bytes means the same thing a failure result code
            // means, so it is reported as a rejection rather than left to fault the caller. The
            // excluded types are deliberately not data-quality problems — a missing or mismatched
            // native library is an environment defect, and reporting it as "the file was bad" would
            // hide a broken deployment behind what looks like ordinary upload traffic (found the hard
            // way: this repository's own worker image was missing libfontconfig1 until issue #597).
            return Result.Success(Rejected(ImageRejectionReason.Malformed));
        }
    }

    private static ImageProcessingResult ProcessBuffer(ReadOnlySpan<byte> bytes, string declaredContentType)
    {
        var signature = DetectSignature(bytes);
        if (signature is null || !string.Equals(signature, declaredContentType, StringComparison.Ordinal))
        {
            return Rejected(ImageRejectionReason.SignatureMismatch);
        }

        if (ContainsEmbeddedFormatMarker(bytes))
        {
            return Rejected(ImageRejectionReason.PolyglotContent);
        }

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var codec = SKCodec.Create(stream, out var openResult);
        if (codec is null)
        {
            return Rejected(openResult == SKCodecResult.IncompleteInput
                ? ImageRejectionReason.Truncated
                : ImageRejectionReason.Malformed);
        }

        // Read from the header before a single pixel is decoded: the whole point of this check is to
        // refuse a decompression bomb — a small file whose header declares an enormous bitmap — before
        // the allocation that would make it one.
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0
            || Math.Max(codec.Info.Width, codec.Info.Height) > MaxDimensionPx)
        {
            return Rejected(ImageRejectionReason.DimensionsExceedLimit);
        }

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixelCount)
        {
            return Rejected(ImageRejectionReason.PixelCountExceedsLimit);
        }

        using var decoded = new SKBitmap(codec.Info);
        var decodeResult = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (decodeResult != SKCodecResult.Success)
        {
            return Rejected(decodeResult switch
            {
                SKCodecResult.IncompleteInput => ImageRejectionReason.Truncated,
                SKCodecResult.Unimplemented => ImageRejectionReason.UnsupportedFormat,
                _ => ImageRejectionReason.Malformed,
            });
        }

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        var image = new ProcessedImage(
            Original: Encode(oriented),
            Thumbnail: EncodeDerivative(oriented, ThumbnailMaxEdgePx),
            Preview: EncodeDerivative(oriented, PreviewMaxEdgePx));

        return new ImageProcessingResult(ImageProcessingStatus.Accepted, Image: image);
    }

    private static ImageProcessingResult Rejected(ImageRejectionReason reason)
        => new(ImageProcessingStatus.Rejected, reason);

    private static string? DetectSignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return JpegContentType;
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= pngSignature.Length && bytes[..pngSignature.Length].SequenceEqual(pngSignature))
        {
            return PngContentType;
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return WebpContentType;
        }

        if (bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p'
            && HeicBrands.Contains(Encoding.ASCII.GetString(bytes.Slice(8, 4))))
        {
            return HeicContentType;
        }

        return null;
    }

    private static bool ContainsEmbeddedFormatMarker(ReadOnlySpan<byte> bytes)
    {
        foreach (var marker in EmbeddedFormatMarkers)
        {
            if (bytes.IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bakes the codec's reported orientation into the pixels themselves, on a freshly sized
    /// destination bitmap, so the caller never needs the tag this adapter is about to strip.
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            // Ownership passes to the caller either way; return a copy so every path can be disposed
            // uniformly by the caller without double-disposing the decode buffer.
            return source.Copy();
        }

        var swapsDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var destination = new SKBitmap(
            swapsDimensions ? source.Height : source.Width,
            swapsDimensions ? source.Width : source.Height,
            source.ColorType,
            source.AlphaType);

        using (var canvas = new SKCanvas(destination))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(destination.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(destination.Width, destination.Height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, destination.Height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(destination.Width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, destination.Height);
                    canvas.RotateDegrees(-90);
                    break;
                case SKEncodedOrigin.LeftTop:
                    // Transpose: mirror across the top-left/bottom-right diagonal. Not produced by any
                    // camera or phone this product photographs a garment with — only by some scanner
                    // software — approximated here as a rotate-and-mirror rather than exhaustively
                    // proven, unlike the pure-rotation cases above.
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightBottom:
                    // Transverse: the anti-diagonal mirror. Same caveat as LeftTop above.
                    canvas.Translate(destination.Width, destination.Height);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                default:
                    break;
            }

            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        return destination;
    }

    private static ProcessedImageVariant EncodeDerivative(SKBitmap source, int maxEdgePx)
    {
        var longestEdge = Math.Max(source.Width, source.Height);
        var scale = Math.Min(1.0, (double)maxEdgePx / longestEdge);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        if (width == source.Width && height == source.Height)
        {
            return Encode(source);
        }

        var targetInfo = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        using var resized = source.Resize(targetInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return Encode(resized ?? source);
    }

    private static ProcessedImageVariant Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return new ProcessedImageVariant(encoded.ToArray(), JpegContentType, bitmap.Width, bitmap.Height);
    }
}
