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
/// carries is the sRGB profile Skia writes into every JPEG it encodes, identical whatever the source was.
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
/// <strong>An exception means the environment or this adapter failed, never that the file was bad.</strong> Every
/// problem with the bytes themselves is a <see cref="ImageProcessingStatus.Rejected"/> outcome. Memory exhaustion, a
/// missing native library, cancellation, or a Skia call failing in a way no input should cause all escape to the
/// caller, whose retry policy is the right place for them: reported as a rejection, they would permanently refuse a
/// valid photo and look like ordinary upload traffic while doing it.
/// </para>
/// </remarks>
public sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>#597's own stated limit: reject anything longer than this on its longest edge.</summary>
    private const int MaxDimensionPx = 12_000;

    /// <summary>#597's own stated limit: reject anything with more decoded pixels than this (40 MP).</summary>
    private const long MaxPixelCount = 40_000_000;

    /// <summary>
    /// Implementation defaults, not sourced product figures — nothing today reads these from configuration, and
    /// nobody has asked for a specific thumbnail or preview size yet.
    /// </summary>
    private const int ThumbnailMaxEdgePx = 320;

    private const int PreviewMaxEdgePx = 1600;
    private const int JpegQuality = 90;

    private const string JpegContentType = "image/jpeg";

    /// <summary>Acrobat looks for a PDF header within the first 1,024 bytes of a file, not only at byte 0.</summary>
    private const int PdfHeaderWindow = 1024;

    /// <summary>How far into a file a content-sniffing reader looks for markup; 1,024 covers every browser's window.</summary>
    private const int MarkupSniffWindow = 1024;

    private const int ZipEndRecordLength = 22;
    private const int Zip64LocatorLength = 20;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions DownscaleSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly string[] HeicBrands =
        ["heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1"];

    private static readonly byte[][] ScriptBearingMarkup =
        ["<script"u8.ToArray(), "<html"u8.ToArray(), "<iframe"u8.ToArray(), "<svg"u8.ToArray(), "<!doctype html"u8.ToArray()];

    /// <inheritdoc />
    public async Task<Result<ImageProcessingResult>> ProcessAsync(
        Stream content, string declaredContentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredContentType);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return Result.Success(Process(buffer.GetBuffer(), (int)buffer.Length, declaredContentType, cancellationToken));
    }

    private static ImageProcessingResult Process(
        byte[] buffer, int length, string declaredContentType, CancellationToken cancellationToken)
    {
        var bytes = buffer.AsSpan(0, length);

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

        // Wraps the buffer rather than copying it: the file is already in memory once.
        using var stream = new MemoryStream(buffer, 0, length, writable: false);
        using var codec = SKCodec.Create(stream, out var openResult);
        if (codec is null)
        {
            return Rejected(openResult switch
            {
                SKCodecResult.IncompleteInput => ImageRejectionReason.Truncated,
                SKCodecResult.Unimplemented => ImageRejectionReason.UnsupportedFormat,
                _ => ImageRejectionReason.Malformed,
            });
        }

        // Skia chooses its decoder by sniffing the bytes itself; the declared type only chose which signature was
        // checked above. They agree for every accepted format today, and this keeps it that way.
        if (codec.EncodedFormat != expectedDecoder)
        {
            return Rejected(ImageRejectionReason.SignatureMismatch);
        }

        // Read from the header before a single pixel is decoded: the whole point is to refuse a decompression bomb —
        // a small file whose header declares an enormous bitmap — before the allocation that would make it one.
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || Math.Max(width, height) > MaxDimensionPx)
        {
            return Rejected(ImageRejectionReason.DimensionsExceedLimit);
        }

        if ((long)width * height > MaxPixelCount)
        {
            return Rejected(ImageRejectionReason.PixelCountExceedsLimit);
        }

        // One canonical pixel format whatever the source was — grayscale, CMYK, palette or 16-bit — converted into
        // sRGB by the codec as it decodes, so everything after this line handles a single well-known layout.
        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul, Srgb);
        using var decoded = Allocate(info);
        var decodeResult = codec.GetPixels(info, decoded.GetPixels());
        switch (decodeResult)
        {
            case SKCodecResult.Success:
                break;
            case SKCodecResult.IncompleteInput:
                return Rejected(ImageRejectionReason.Truncated);
            case SKCodecResult.ErrorInInput or SKCodecResult.InvalidInput:
                return Rejected(ImageRejectionReason.Malformed);
            case SKCodecResult.Unimplemented:
                return Rejected(ImageRejectionReason.UnsupportedFormat);
            case SKCodecResult.InternalError:
                throw new InsufficientMemoryException(
                    $"SkiaSharp reported an internal error, which it documents as memory exhaustion, decoding a {width}x{height} image.");
            default:
                throw new InvalidOperationException(
                    $"SkiaSharp refused a decode this adapter requested ({decodeResult}); no input should cause that.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var normalised = Normalise(decoded, codec.EncodedOrigin, codec.Info.AlphaType != SKAlphaType.Opaque);
        var source = normalised ?? decoded;
        cancellationToken.ThrowIfCancellationRequested();

        var original = Encode(source);
        cancellationToken.ThrowIfCancellationRequested();
        var preview = EncodeDerivative(source, PreviewMaxEdgePx, original);
        cancellationToken.ThrowIfCancellationRequested();
        var thumbnail = EncodeDerivative(source, ThumbnailMaxEdgePx, original);

        return new ImageProcessingResult(
            ImageProcessingStatus.Accepted, Image: new ProcessedImage(original, thumbnail, preview));
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
    /// True when a second format's reader would also accept the file. Each check looks exactly where that format's
    /// own reader looks, rather than for a short marker anywhere: a real photograph is megabytes of compressed data,
    /// and a four-byte marker occurs in that by chance about once in every 700 three-megabyte files.
    /// </summary>
    private static bool ContainsSecondFormat(ReadOnlySpan<byte> bytes)
        => ContainsZipArchive(bytes)
            || bytes[..Math.Min(bytes.Length, PdfHeaderWindow)].IndexOf("%PDF-"u8) >= 0
            || ContainsMarkup(bytes[..Math.Min(bytes.Length, MarkupSniffWindow)])
            || ContainsPhpOpenTag(bytes);

    /// <summary>
    /// A ZIP reader (and so a JAR, an APK or an Office document) finds its end-of-central-directory record by
    /// scanning back from the end of the file — no further than a 65,535-byte comment — and then reads the central
    /// directory that sits immediately before it. Only that shape makes a file a working archive, and requiring both
    /// signatures in their structural positions takes the chance of a photograph matching it to roughly one in 10^14.
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
        var directoryStart = (long)endRecord - directorySize;
        return directorySize > 0 && directoryStart >= 0
            && bytes.Slice((int)directoryStart, 4).SequenceEqual("PK\x01\x02"u8);
    }

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
    /// buffer. PHP requires whitespace after the tag, which keeps the chance of a photograph matching by accident to
    /// about one in 600,000 at the 15 MB upload ceiling.
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
    /// neither — the common case for a JPEG, which then costs no second full-size bitmap at all.
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
    /// Scales down to <paramref name="maxEdgePx"/> on the longest edge, never up: an image already that small is its
    /// own derivative, and gets the original's bytes rather than a second identical encode.
    /// </summary>
    private static ProcessedImageVariant EncodeDerivative(SKBitmap source, int maxEdgePx, ProcessedImageVariant original)
    {
        var scale = (double)maxEdgePx / Math.Max(source.Width, source.Height);
        if (scale >= 1.0)
        {
            return original;
        }

        var info = source.Info.WithSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        using var resized = source.Resize(info, DownscaleSampling)
            ?? throw new InsufficientMemoryException($"SkiaSharp could not resize to {info.Width}x{info.Height}.");
        return Encode(resized);
    }

    private static ProcessedImageVariant Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new InvalidOperationException($"SkiaSharp could not encode a {bitmap.Width}x{bitmap.Height} JPEG.");
        return new ProcessedImageVariant(encoded.ToArray(), JpegContentType, bitmap.Width, bitmap.Height);
    }

    /// <summary>The content type a signature identifies, and the Skia decoder that should handle it (none for HEIC).</summary>
    private sealed record DeclaredFormat(string ContentType, SKEncodedImageFormat? Decoder);
}
