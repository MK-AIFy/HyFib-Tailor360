using System.Buffers.Binary;
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
/// <strong>Stripping is a side effect of re-encoding, not a separate step.</strong> Every accepted file is decoded to
/// raw sRGB pixels and encoded again as a new JPEG. Nothing from the source file's segments survives that — not its
/// EXIF or GPS tags, its XMP packet, its own ICC profile, nor a polyglot's second payload. The one profile an output
/// carries is the sRGB profile Skia writes into every JPEG it encodes, identical whatever the source was. That
/// re-encode is the control: <see cref="ImageRejectionReason.PolyglotContent"/> is best-effort defence in depth, and a
/// caller must store and serve only the re-encoded variants, never the bytes it was given.
/// </para>
/// <para>
/// <strong>Two things are baked into the pixels rather than discarded.</strong> EXIF orientation, because a camera
/// sensor is landscape-native and stripping the tag unapplied would leave most portrait phone photos sideways; and
/// transparency, flattened onto white, because JPEG has no alpha channel and Skia's encoder writes a transparent pixel
/// as black.
/// </para>
/// <para>
/// <strong>HEIC is refused, not decoded.</strong> SkiaSharp has no HEIF decoder (mono/SkiaSharp#2887 and #1700), so an
/// <c>image/heic</c> upload is rejected as <see cref="ImageRejectionReason.UnsupportedFormat"/> before any decoder sees
/// it. Passing it to <see cref="SKCodec"/> anyway would let whichever of Skia's other decoders recognised the leading
/// bytes — WBMP, BMP or ICO — decode it and report success. How HEIC should be handled instead is #643.
/// </para>
/// <para>
/// <strong>What a rejection and an exception each mean.</strong> Every problem with the bytes themselves is a
/// <see cref="ImageProcessingStatus.Rejected"/> outcome; an allocation failure this adapter can see, a missing native
/// library, cancellation, or a Skia call failing in a way no input should cause escapes as an exception, for the
/// caller's retry policy. Two limits on that: an allocation failure deep inside a decoder can come back from Skia as
/// corrupt input and so as <see cref="ImageRejectionReason.Malformed"/>, and real memory exhaustion usually ends the
/// process instead of throwing — so a caller must bound its attempts durably, not only by catching.
/// </para>
/// <para>
/// <strong>What <see cref="ImageRejectionReason.Truncated"/> can and cannot tell apart.</strong> Skia reports every error
/// libpng raises as incomplete input, so a corrupt PNG is reported as truncated, not malformed; only JPEG distinguishes
/// the two. And a JPEG whose scan data is damaged but still followed by a marker is not rejected at all: libjpeg treats
/// that as a warning and fills the missing blocks with grey. Neither is a way past the checks — the output is still a
/// fresh encode of whatever pixels decoded — but a reason code is only as precise as the decoder behind it.
/// </para>
/// </remarks>
public sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>#597's own stated limit: reject anything longer than this on its longest edge.</summary>
    private const int MaxDimensionPx = 12_000;

    /// <summary>#597's own stated limit: reject anything with more decoded pixels than this (40 MP).</summary>
    private const long MaxPixelCount = 40_000_000;

    /// <summary>
    /// The re-encode target for the stored original: docs/nfr/capacity-and-performance.md §2.5 proposes 2,400 px on the
    /// long edge. Still open decision CP-06, where 1,600 px is the alternative — change it here if CP-06 decides so.
    /// </summary>
    private const int OriginalMaxEdgePx = 2400;

    /// <summary>docs/nfr/capacity-and-performance.md §2.5: previews at 1,024 px and thumbnails at 256 px on the long edge.</summary>
    private const int PreviewMaxEdgePx = 1024;

    private const int ThumbnailMaxEdgePx = 256;

    /// <summary>
    /// Not sourced: §2.5 leaves the encoder settings for #31 to confirm, and this is the adapter's proposal until it
    /// does — high enough that a fabric's weave survives the preview.
    /// </summary>
    private const int JpegQuality = 90;

    private const string JpegContentType = "image/jpeg";

    /// <summary>PDFium accepts a <c>%PDF</c> header that starts anywhere in the first 1,025 bytes.</summary>
    private const int PdfHeaderLastOffset = 1024;

    /// <summary>How far into a file a content-sniffing reader looks for markup; 1,024 covers every browser's window.</summary>
    private const int MarkupSniffWindow = 1024;

    private const int ZipEndRecordLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int PngSignatureLength = 8;
    private const int PngChunkOverhead = 12;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions DownscaleSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly string[] HeicBrands =
        ["heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1"];

    private static readonly byte[][] ScriptBearingMarkup =
        ["<script"u8.ToArray(), "<html"u8.ToArray(), "<iframe"u8.ToArray(), "<svg"u8.ToArray(), "<!doctype html"u8.ToArray()];

    /// <summary>
    /// The ancillary PNG chunks that can change a decoded pixel. Every other ancillary chunk — the text chunks above
    /// all — is dropped before libpng sees the file; critical chunks are always kept, so an unknown one still fails.
    /// <c>cICP</c>, <c>mDCV</c> and <c>cLLI</c> are PNG's third-edition colour and HDR metadata: the decoder in
    /// SkiaSharp 4.152 does not read them yet, but a newer one does, and a small uncompressed chunk costs nothing to
    /// keep against the day an upgrade would otherwise start mis-colouring wide-gamut images.
    /// </summary>
    private static readonly string[] PixelAffectingPngChunks =
        ["tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "eXIf", "cICP", "mDCV", "cLLI"];

    /// <inheritdoc />
    public async Task<Result<ImageProcessingResult>> ProcessAsync(
        Stream content, string declaredContentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredContentType);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
        return Result.Success(Process(bytes, declaredContentType, cancellationToken));
    }

    /// <summary>
    /// Returns <paramref name="png"/> without the ancillary chunks that cannot change a decoded pixel, or unchanged when
    /// it has none. libpng inflates every compressed text chunk it is handed — about 13 ms of uncancellable work per
    /// chunk, and a thousand of them fit under the upload cap — so they are removed before it is handed any. A chunk
    /// that runs past the end of the file is kept as it is, so a truncated file still reads as truncated.
    /// </summary>
    internal static ArraySegment<byte> WithoutInertPngChunks(ArraySegment<byte> png)
    {
        var bytes = png.AsSpan();
        var kept = new List<Range>();
        var droppedAny = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var at = PngSignatureLength;
        while (at + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
            var type = Encoding.ASCII.GetString(bytes.Slice(at + 4, 4));
            var end = at + PngChunkOverhead + (long)length;
            if (end > bytes.Length)
            {
                kept.Add(at..bytes.Length);
                break;
            }

            // A lower-case first letter marks an ancillary chunk; each pixel-affecting one is singular by the spec,
            // so only its first occurrence can matter.
            var ancillary = char.IsLower(type[0]);
            if (ancillary && (!PixelAffectingPngChunks.Contains(type) || !seen.Add(type)))
            {
                droppedAny = true;
            }
            else
            {
                kept.Add(at..(int)end);
            }

            at = (int)end;
            if (type == "IEND")
            {
                break;
            }
        }

        if (!droppedAny)
        {
            return png;
        }

        using var rebuilt = new MemoryStream(png.Count);
        rebuilt.Write(bytes[..PngSignatureLength]);
        foreach (var range in kept)
        {
            rebuilt.Write(bytes[range]);
        }

        return new ArraySegment<byte>(rebuilt.GetBuffer(), 0, (int)rebuilt.Length);
    }

    private static ImageProcessingResult Process(
        ArraySegment<byte> bytes, string declaredContentType, CancellationToken cancellationToken)
    {
        var format = DetectFormat(bytes);
        if (format is null || !string.Equals(format.ContentType, declaredContentType, StringComparison.Ordinal))
        {
            return Rejected(ImageRejectionReason.SignatureMismatch);
        }

        if (format.Decoder is not { } expectedDecoder)
        {
            return Rejected(ImageRejectionReason.UnsupportedFormat);
        }

        if (ContainsSecondFormat(bytes))
        {
            return Rejected(ImageRejectionReason.PolyglotContent);
        }

        var decodable = expectedDecoder == SKEncodedImageFormat.Png ? WithoutInertPngChunks(bytes) : bytes;
        var (decoded, rejection) = Decode(decodable, expectedDecoder);
        if (decoded is null)
        {
            return Rejected(rejection!.Value);
        }

        var source = decoded.Pixels;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Normalise(source, decoded.Origin, decoded.MayHaveTransparency) is { } normalised)
            {
                source.Dispose();
                source = normalised;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var original = EncodeWithin(source, OriginalMaxEdgePx, sameSizeAlreadyEncoded: null);
            cancellationToken.ThrowIfCancellationRequested();
            var preview = EncodeWithin(source, PreviewMaxEdgePx, original);
            cancellationToken.ThrowIfCancellationRequested();
            var thumbnail = EncodeWithin(source, ThumbnailMaxEdgePx, original);

            return new ImageProcessingResult(
                ImageProcessingStatus.Accepted, Image: new ProcessedImage(original, thumbnail, preview));
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>
    /// Opens a codec, applies the header limits, and decodes into one canonical layout — or names why it would not.
    /// The codec and its input stream are disposed before this returns, so nothing a decoder holds outlives the decode.
    /// </summary>
    private static (DecodedImage? Image, ImageRejectionReason? Rejection) Decode(
        ArraySegment<byte> bytes, SKEncodedImageFormat expectedDecoder)
    {
        using var stream = new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false);
        using var codec = SKCodec.Create(stream, out var openResult);
        if (codec is null)
        {
            return (null, openResult switch
            {
                SKCodecResult.IncompleteInput => ImageRejectionReason.Truncated,
                SKCodecResult.Unimplemented => ImageRejectionReason.UnsupportedFormat,
                SKCodecResult.InternalError => throw new InsufficientMemoryException(
                    "SkiaSharp reported an internal error, which it documents as memory exhaustion, opening a codec."),
                _ => ImageRejectionReason.Malformed,
            });
        }

        // Skia chooses its decoder by sniffing the bytes itself; the declared type only chose which signature was
        // checked. They agree for every accepted format today, and this keeps it that way.
        if (codec.EncodedFormat != expectedDecoder)
        {
            return (null, ImageRejectionReason.SignatureMismatch);
        }

        // Read from the header before a single pixel is decoded: the whole point is to refuse a decompression bomb —
        // a small file whose header declares an enormous bitmap — before the allocation that would make it one.
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || Math.Max(width, height) > MaxDimensionPx)
        {
            return (null, ImageRejectionReason.DimensionsExceedLimit);
        }

        if ((long)width * height > MaxPixelCount)
        {
            return (null, ImageRejectionReason.PixelCountExceedsLimit);
        }

        // One canonical pixel format whatever the source was — grayscale, CMYK, palette or 16-bit — converted into
        // sRGB by the codec as it decodes, so everything after this handles a single well-known layout.
        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul, Srgb);
        var pixels = Allocate(info);
        var decodeResult = codec.GetPixels(info, pixels.GetPixels());
        ImageRejectionReason? rejection = decodeResult switch
        {
            SKCodecResult.Success => null,
            SKCodecResult.IncompleteInput => ImageRejectionReason.Truncated,
            SKCodecResult.ErrorInInput or SKCodecResult.InvalidInput => ImageRejectionReason.Malformed,
            SKCodecResult.Unimplemented => ImageRejectionReason.UnsupportedFormat,
            _ => null,
        };

        if (decodeResult == SKCodecResult.Success)
        {
            // Never written again: marking it immutable lets drawing and encoding share its pixels instead of
            // copying them, which is most of this adapter's peak memory at the 40 MP limit.
            pixels.SetImmutable();
            return (new DecodedImage(pixels, codec.EncodedOrigin, codec.Info.AlphaType != SKAlphaType.Opaque), null);
        }

        pixels.Dispose();
        if (rejection is not null)
        {
            return (null, rejection);
        }

        throw decodeResult == SKCodecResult.InternalError
            ? new InsufficientMemoryException(
                $"SkiaSharp reported an internal error, which it documents as memory exhaustion, decoding a {width}x{height} image.")
            : new InvalidOperationException(
                $"SkiaSharp refused a decode this adapter requested ({decodeResult}); no input should cause that.");
    }

    private static ImageProcessingResult Rejected(ImageRejectionReason reason)
        => new(ImageProcessingStatus.Rejected, reason);

    private static DeclaredFormat? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return new DeclaredFormat(JpegContentType, SKEncodedImageFormat.Jpeg);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new DeclaredFormat("image/png", SKEncodedImageFormat.Png);
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return new DeclaredFormat("image/webp", SKEncodedImageFormat.Webp);
        }

        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8)
            && HeicBrands.Contains(Encoding.ASCII.GetString(bytes.Slice(8, 4))))
        {
            return new DeclaredFormat("image/heic", Decoder: null);
        }

        return null;
    }

    /// <summary>
    /// True when a second format's reader would also accept the file, for the readers these checks model. Each looks
    /// where that reader looks, rather than for a short marker anywhere: a real photograph is megabytes of compressed
    /// data, and a four-byte marker occurs in that by chance about once in every 700 three-megabyte files. This is
    /// best-effort — lenient readers exist for every format — which is why the re-encode, not this, is the control.
    /// </summary>
    private static bool ContainsSecondFormat(ReadOnlySpan<byte> bytes)
        => ContainsZipArchive(bytes)
            || bytes[..Math.Min(bytes.Length, PdfHeaderLastOffset + 4)].IndexOf("%PDF"u8) >= 0
            || ContainsMarkup(bytes[..Math.Min(bytes.Length, MarkupSniffWindow)])
            || ContainsPhpOpenTag(bytes);

    /// <summary>
    /// A ZIP reader (and so a JAR, an APK or an Office document) finds its end-of-central-directory record by scanning
    /// back from the end of the file, no further than a 65,535-byte comment, then locates the central directory either
    /// immediately before that record (from its size field) or at the absolute offset the record states. A candidate
    /// counts only when a central-directory signature sits at one of those two places, which keeps the chance of a
    /// photograph matching by accident to roughly one in 10^14.
    /// </summary>
    private static bool ContainsZipArchive(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ZipEndRecordLength)
        {
            return false;
        }

        var windowStart = Math.Max(0, bytes.Length - ZipEndRecordLength - ushort.MaxValue);
        var searchEnd = bytes.Length - ZipEndRecordLength + 4;
        while (true)
        {
            var found = bytes[windowStart..searchEnd].LastIndexOf("PK\x05\x06"u8);
            if (found < 0)
            {
                return false;
            }

            var endRecord = windowStart + found;
            if (IsZipEndRecord(bytes, endRecord))
            {
                return true;
            }

            // Exclude this candidate but keep any earlier one that overlaps it.
            searchEnd = endRecord + 3;
        }
    }

    private static bool IsZipEndRecord(ReadOnlySpan<byte> bytes, int endRecord)
    {
        if (endRecord >= Zip64LocatorLength
            && bytes.Slice(endRecord - Zip64LocatorLength, 4).SequenceEqual("PK\x06\x07"u8))
        {
            return true;
        }

        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(endRecord + 12, 4));
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(endRecord + 16, 4));
        return IsCentralDirectoryAt(bytes, directorySize == 0 ? -1 : (long)endRecord - directorySize, endRecord)
            || IsCentralDirectoryAt(bytes, directoryOffset, endRecord);
    }

    private static bool IsCentralDirectoryAt(ReadOnlySpan<byte> bytes, long position, int endRecord)
        => position >= 0 && position + 4 <= endRecord
            && bytes.Slice((int)position, 4).SequenceEqual("PK\x01\x02"u8);

    private static bool ContainsMarkup(ReadOnlySpan<byte> window)
    {
        for (var at = window.IndexOf((byte)'<'); at >= 0; at = NextIndexOf(window, (byte)'<', at))
        {
            foreach (var tag in ScriptBearingMarkup)
            {
                if (window.Length - at >= tag.Length && Ascii.EqualsIgnoreCase(window.Slice(at, tag.Length), tag))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// PHP executes an open tag wherever it appears in an included file, so this one is checked across the whole
    /// buffer. <c>&lt;?php</c> must be followed by whitespace, which keeps the chance of a photograph matching by
    /// accident to about one in 600,000 at the 15 MB upload ceiling. The short echo tag <c>&lt;?=</c> is deliberately
    /// not matched: three fixed bytes occur by chance in almost every large photograph.
    /// </summary>
    private static bool ContainsPhpOpenTag(ReadOnlySpan<byte> bytes)
    {
        for (var at = bytes.IndexOf("<?"u8); at >= 0; at = NextIndexOf(bytes, "<?"u8, at))
        {
            if (bytes.Length - at >= 6
                && Ascii.EqualsIgnoreCase(bytes.Slice(at + 2, 3), "php"u8)
                && bytes[at + 5] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                return true;
            }
        }

        return false;
    }

    private static int NextIndexOf(ReadOnlySpan<byte> bytes, byte value, int after)
    {
        var next = bytes[(after + 1)..].IndexOf(value);
        return next < 0 ? -1 : after + 1 + next;
    }

    private static int NextIndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> value, int after)
    {
        var next = bytes[(after + 1)..].IndexOf(value);
        return next < 0 ? -1 : after + 1 + next;
    }

    /// <summary>
    /// Bakes orientation and transparency into a fresh bitmap, or returns null when the decoded one already needs
    /// neither — the common case for a JPEG. At most two full-size bitmaps are alive at once, and only while this runs:
    /// the caller disposes the decoded one as soon as this returns a replacement.
    /// </summary>
    private static SKBitmap? Normalise(SKBitmap decoded, SKEncodedOrigin origin, bool mayHaveTransparency)
    {
        if (origin == SKEncodedOrigin.TopLeft && !mayHaveTransparency)
        {
            return null;
        }

        var swapsDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var info = decoded.Info.WithSize(
            swapsDimensions ? decoded.Height : decoded.Width,
            swapsDimensions ? decoded.Width : decoded.Height);
        var destination = Allocate(info);

        using (var canvas = new SKCanvas(destination))
        {
            canvas.Clear(SKColors.White);
            ApplyOrigin(canvas, origin, info.Width, info.Height);
            canvas.DrawBitmap(decoded, 0, 0, NearestSampling);
        }

        destination.SetImmutable();
        return destination;
    }

    /// <summary>
    /// Sets the canvas transform that displays an image stored with <paramref name="origin"/> the right way up, per the
    /// EXIF definition of the eight orientation values. A source point (x, y) lands at: TopRight (2) (W−x, y);
    /// BottomRight (3) (W−x, H−y); BottomLeft (4) (x, H−y); LeftTop (5) (y, x); RightTop (6) (H−y, x); RightBottom (7)
    /// (H−y, W−x); LeftBottom (8) (y, W−x) — where W and H are the source's width and height. Every case is proved by a
    /// contract test that tracks four coloured quadrants through the transform.
    /// </summary>
    private static void ApplyOrigin(SKCanvas canvas, SKEncodedOrigin origin, int destinationWidth, int destinationHeight)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(destinationWidth, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(destinationWidth, destinationHeight);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, destinationHeight);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(destinationWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(destinationWidth, destinationHeight);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, destinationHeight);
                canvas.RotateDegrees(-90);
                break;
            default:
                break;
        }
    }

    private static SKBitmap Allocate(SKImageInfo info)
    {
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(info))
        {
            return bitmap;
        }

        bitmap.Dispose();
        throw new InsufficientMemoryException($"SkiaSharp could not allocate a {info.Width}x{info.Height} bitmap.");
    }

    /// <summary>
    /// Encodes <paramref name="source"/> scaled down to <paramref name="maxEdgePx"/> on its longest edge, never up. An
    /// image already within the limit is encoded at its own size — or, when <paramref name="sameSizeAlreadyEncoded"/>
    /// holds that encode already, reuses it rather than producing a second identical one.
    /// </summary>
    private static ProcessedImageVariant EncodeWithin(
        SKBitmap source, int maxEdgePx, ProcessedImageVariant? sameSizeAlreadyEncoded)
    {
        var scale = (double)maxEdgePx / Math.Max(source.Width, source.Height);
        if (scale >= 1.0)
        {
            return sameSizeAlreadyEncoded ?? Encode(source);
        }

        var info = source.Info.WithSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        using var resized = Allocate(info);
        if (!source.ScalePixels(resized, DownscaleSampling))
        {
            throw new InvalidOperationException($"SkiaSharp could not scale to {info.Width}x{info.Height}.");
        }

        return Encode(resized);
    }

    /// <summary>Encodes straight from the bitmap's own pixels: no intermediate image, so no copy of them.</summary>
    private static ProcessedImageVariant Encode(SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new InvalidOperationException($"SkiaSharp could not encode a {bitmap.Width}x{bitmap.Height} JPEG.");
        return new ProcessedImageVariant(encoded.ToArray(), JpegContentType, bitmap.Width, bitmap.Height);
    }

    /// <summary>The content type a signature identifies, and the Skia decoder that should handle it (none for HEIC).</summary>
    private sealed record DeclaredFormat(string ContentType, SKEncodedImageFormat? Decoder);

    /// <summary>Decoded pixels, and what the codec reported that the pixels themselves do not carry.</summary>
    private sealed record DecodedImage(SKBitmap Pixels, SKEncodedOrigin Origin, bool MayHaveTransparency);
}
