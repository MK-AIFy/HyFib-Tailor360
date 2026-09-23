using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Shouldly;
using SkiaSharp;
using Tailor360.Modules.Integration.Infrastructure.Imaging;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.ContractTests.Adapters;

/// <summary>
/// <see cref="SkiaImageProcessor"/> (ADR-0012, issue #597) against a fixture corpus built by this file rather than
/// checked-in binaries: every well-formed and adversarial case is constructed from known bytes, so an assertion can
/// say exactly what was fed in and what came out.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ImageProcessorAdapterTests
{
    private static readonly SKColor Red = new(255, 0, 0);
    private static readonly SKColor Green = new(0, 255, 0);
    private static readonly SKColor Blue = new(0, 0, 255);
    private static readonly SKColor Yellow = new(255, 255, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- Accepted: decoded, stripped and derived ----

    [Fact]
    public async Task AcceptsAWellFormedJpegAndProducesAnOriginalThumbnailAndPreview()
    {
        var jpeg = EncodeSolidColour(2000, 1000, SKColors.SeaGreen, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
        var image = result.Image.ShouldNotBeNull();
        (image.Original.WidthPx, image.Original.HeightPx).ShouldBe((2000, 1000));
        (image.Preview.WidthPx, image.Preview.HeightPx).ShouldBe((1600, 800));
        (image.Thumbnail.WidthPx, image.Thumbnail.HeightPx).ShouldBe((320, 160));
        foreach (var variant in new[] { image.Original, image.Preview, image.Thumbnail })
        {
            variant.ContentType.ShouldBe("image/jpeg");
            AssertIsJpegOf(variant);
        }
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task ReEncodesAPngOrWebpUploadAsJpeg(string declaredContentType)
    {
        var format = declaredContentType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp;
        var upload = EncodeSolidColour(300, 200, SKColors.SeaGreen, format);

        var result = await Process(upload, declaredContentType);

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
        var original = result.Image.ShouldNotBeNull().Original;
        original.ContentType.ShouldBe("image/jpeg");
        (original.WidthPx, original.HeightPx).ShouldBe((300, 200));
        AssertIsJpegOf(original);
    }

    [Fact]
    public async Task DoesNotUpscaleADerivativeSmallerThanItsTargetEdge()
    {
        var jpeg = EncodeSolidColour(120, 80, SKColors.SlateBlue, SKEncodedImageFormat.Jpeg);

        var image = (await Process(jpeg, "image/jpeg")).Image.ShouldNotBeNull();

        // Already smaller than both targets: each derivative is the original itself, not a second identical encode.
        image.Thumbnail.ShouldBeSameAs(image.Original);
        image.Preview.ShouldBeSameAs(image.Original);
        (image.Original.WidthPx, image.Original.HeightPx).ShouldBe((120, 80));
    }

    [Fact]
    public async Task StripsExifGpsIccAndXmpFromEveryVariantOfAFileThatCarriedThem()
    {
        var baseJpeg = EncodeSolidColour(2000, 1500, SKColors.Coral, SKEncodedImageFormat.Jpeg);
        var withMetadata = SpliceAfterSoi(baseJpeg, BuildExifWithGpsSegment(), BuildIccProfileSegment(), BuildXmpSegment());

        // Proves the fixture: every marker this test later asserts is absent really is in the input.
        var input = Encoding.Latin1.GetString(withMetadata);
        input.ShouldContain("Exif");
        input.ShouldContain(FakeSourceIccMarker);
        input.ShouldContain("http://ns.adobe.com/xap");

        var result = await Process(withMetadata, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted, "a photo carrying ordinary metadata is stripped, not refused");
        var image = result.Image.ShouldNotBeNull();
        foreach (var variant in new[] { image.Original, image.Preview, image.Thumbnail })
        {
            var output = Encoding.Latin1.GetString(variant.Bytes.Span);
            output.ShouldNotContain("Exif");
            output.ShouldNotContain("http://ns.adobe.com/xap");

            // Not "no ICC profile at all": Skia writes an sRGB profile into every JPEG it encodes, the same one for
            // every output. What must be gone is the source file's own profile.
            output.ShouldNotContain(FakeSourceIccMarker);
        }
    }

    /// <summary>
    /// Tracks four coloured quadrants — red top-left, green top-right, blue bottom-left, yellow bottom-right — through
    /// each EXIF orientation. Every one of the eight is a distinct arrangement, so this proves the transform for all of
    /// them rather than only the common rotation. Expected corners follow the EXIF definition of each value.
    /// </summary>
    [Theory]
    [InlineData(1, "RGBY")]
    [InlineData(2, "GRYB")]
    [InlineData(3, "YBGR")]
    [InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")]
    [InlineData(6, "BRYG")]
    [InlineData(7, "YGBR")]
    [InlineData(8, "GYRB")]
    public async Task BakesEachExifOrientationIntoThePixels(int orientation, string expectedCorners)
    {
        var landscape = EncodeQuadrants(64, 32);
        var tagged = SpliceAfterSoi(landscape, BuildExifOrientationSegment((ushort)orientation));

        var original = (await Process(tagged, "image/jpeg")).Image.ShouldNotBeNull().Original;

        var turnsQuarter = orientation >= 5;
        (original.WidthPx, original.HeightPx).ShouldBe(turnsQuarter ? (32, 64) : (64, 32));

        // Sampled at each quadrant's centre, well inside JPEG's 16-pixel blocks, so lossy edges cannot decide it.
        using var decoded = SKBitmap.Decode(original.Bytes.ToArray());
        var (w, h) = (decoded.Width, decoded.Height);
        var corners = string.Concat(
            Classify(decoded.GetPixel(w / 4, h / 4)),
            Classify(decoded.GetPixel(3 * w / 4, h / 4)),
            Classify(decoded.GetPixel(w / 4, 3 * h / 4)),
            Classify(decoded.GetPixel(3 * w / 4, 3 * h / 4)));
        corners.ShouldBe(expectedCorners, $"orientation {orientation}: top-left, top-right, bottom-left, bottom-right");
    }

    [Fact]
    public async Task FlattensTransparencyOntoWhiteRatherThanBlack()
    {
        // JPEG has no alpha channel, and Skia's encoder writes a transparent pixel as black: an unflattened transparent
        // reference image (a garment cut-out, say) would come back as a black rectangle.
        var transparent = EncodeSolidColour(40, 40, SKColors.Transparent, SKEncodedImageFormat.Png);

        var original = (await Process(transparent, "image/png")).Image.ShouldNotBeNull().Original;

        using var decoded = SKBitmap.Decode(original.Bytes.ToArray());
        var pixel = decoded.GetPixel(20, 20);
        pixel.Red.ShouldBeGreaterThan((byte)240);
        pixel.Green.ShouldBeGreaterThan((byte)240);
        pixel.Blue.ShouldBeGreaterThan((byte)240);
    }

    [Fact]
    public async Task DecodesAGrayscaleJpegAndStillAppliesItsOrientation()
    {
        // A single-component JPEG decodes to a colour type a canvas cannot always draw into; the adapter decodes
        // everything into one canonical layout first, which this proves by rotating one.
        var grayscale = EncodeGrayscaleHalves(32, 16);
        var tagged = SpliceAfterSoi(grayscale, BuildExifOrientationSegment(orientation: 6));

        var original = (await Process(tagged, "image/jpeg")).Image.ShouldNotBeNull().Original;

        (original.WidthPx, original.HeightPx).ShouldBe((16, 32));
        using var decoded = SKBitmap.Decode(original.Bytes.ToArray());
        decoded.GetPixel(8, 4).Red.ShouldBeLessThan((byte)60, "the source's dark left half becomes the top");
        decoded.GetPixel(8, 28).Red.ShouldBeGreaterThan((byte)200, "and its light right half the bottom");
    }

    // ---- Rejected: each for a reason the caller can tell apart ----

    [Fact]
    public async Task RejectsASignatureMismatchBetweenTheBytesAndTheDeclaredContentType()
    {
        var png = EncodeSolidColour(100, 100, SKColors.Black, SKEncodedImageFormat.Png);

        var result = await Process(png, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Rejected);
        result.RejectionReason.ShouldBe(ImageRejectionReason.SignatureMismatch);
    }

    [Theory]
    [InlineData("MZ", "image/png")]
    [InlineData("\u007fELF", "image/jpeg")]
    public async Task RejectsAnExecutableDeclaredAsAnImage(string executableMagic, string declaredContentType)
    {
        // An operating system loader needs its magic number at byte 0, where no image signature can also be — so
        // an executable is refused by the signature check before anything decodes it.
        var executable = Encoding.Latin1.GetBytes(executableMagic).Concat(new byte[510]).ToArray();

        var result = await Process(executable, declaredContentType);

        result.RejectionReason.ShouldBe(ImageRejectionReason.SignatureMismatch);
    }

    [Fact]
    public async Task RejectsAJpegWithARealZipArchiveAppended()
    {
        var polyglot = EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg)
            .Concat(BuildZipArchive("payload.txt", "not really a picture"))
            .ToArray();

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task AcceptsAJpegWhoseCommentMerelyContainsZipSignatures()
    {
        // The false positive a bare four-byte scan produced about once per 700 three-megabyte photographs: ZIP's
        // signatures with no archive structure around them are not a ZIP any reader would open.
        var comment = BuildJpegSegment(0xFE, [.. "PK\x03\x04"u8.ToArray(), .. "PK\x05\x06"u8.ToArray(), .. new byte[18]]);
        var jpeg = SpliceAfterSoi(EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg), comment);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
    }

    [Fact]
    public async Task RejectsAPdfHeaderWhereAPdfReaderWouldFindIt()
    {
        var comment = BuildJpegSegment(0xFE, "%PDF-1.7\n1 0 obj << >> endobj\n"u8.ToArray());
        var polyglot = SpliceAfterSoi(EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg), comment);

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task AcceptsAPdfMarkerFarBeyondWhereAPdfReaderLooks()
    {
        var comment = BuildJpegSegment(0xFE, [.. new byte[2000], .. "%PDF-1.7"u8.ToArray()]);
        var jpeg = SpliceAfterSoi(EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg), comment);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
    }

    [Fact]
    public async Task RejectsScriptBearingMarkupNearTheStartWhateverItsCase()
    {
        var comment = BuildJpegSegment(0xFE, "<SCRIPT>alert(document.cookie)</SCRIPT>"u8.ToArray());
        var polyglot = SpliceAfterSoi(EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg), comment);

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task RejectsAPhpOpenTagAnywhereInTheFile()
    {
        // Deliberately past the markup window: PHP runs an open tag wherever it sits in an included file.
        var comment = BuildJpegSegment(0xFE, [.. new byte[4000], .. "<?PHP echo 1; ?>"u8.ToArray()]);
        var polyglot = SpliceAfterSoi(EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg), comment);

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task RejectsDimensionsOverTheLongestEdgeLimitWithoutNeedingAHugeAllocation()
    {
        // 12,050 px on the long edge, 20 px on the short edge: over the 12,000 px limit, but only ~241,000 pixels to
        // actually allocate and encode — a cheap, fast, deterministic test.
        var jpeg = EncodeSolidColour(12_050, 20, SKColors.Yellow, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.DimensionsExceedLimit);
    }

    [Fact]
    public async Task RejectsAPngDecompressionBombFromItsHeaderAloneWithoutDecodingIt()
    {
        // A real, spec-correct PNG whose IHDR declares a bomb-sized canvas (60,000 x 60,000 = 3.6 billion pixels)
        // while its actual IDAT holds one tiny, correctly zlib-compressed scanline. If this adapter ever allocated
        // before checking codec.Info, this test would exhaust memory rather than complete.
        var bomb = BuildPngWithMismatchedHeaderDimensions(declaredWidth: 60_000, declaredHeight: 60_000);

        var result = await Process(bomb, "image/png");

        result.RejectionReason.ShouldBe(ImageRejectionReason.DimensionsExceedLimit);
    }

    [Fact]
    public async Task RejectsAPixelCountOverTheLimitEvenWhenNeitherEdgeAloneExceedsIt()
    {
        // 9,000 x 9,000 is 81 MP, over the 40 MP cap, while neither edge exceeds 12,000 px — so the pixel-count check
        // is a distinct rule, not a restatement of the edge check. Refused from the header, like the bomb above.
        var oversized = BuildPngWithMismatchedHeaderDimensions(declaredWidth: 9_000, declaredHeight: 9_000);

        var result = await Process(oversized, "image/png");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PixelCountExceedsLimit);
    }

    [Fact]
    public async Task RejectsATruncatedFileRatherThanPartiallyDecodingIt()
    {
        var jpeg = EncodeSolidColour(800, 600, SKColors.Purple, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg[..(jpeg.Length - 200)], "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Truncated);
    }

    [Fact]
    public async Task RejectsGarbageBytesBehindAValidLeadingSignature()
    {
        var garbage = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(Enumerable.Repeat((byte)0x00, 300)).ToArray();

        var result = await Process(garbage, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Rejected);
        (result.RejectionReason is ImageRejectionReason.Malformed or ImageRejectionReason.Truncated)
            .ShouldBeTrue("a signature followed by nothing decodable is corrupt or cut short, never accepted");
    }

    [Fact]
    public async Task RefusesHeicAsUnsupportedWithoutHandingItToAnyDecoder()
    {
        // SkiaSharp has no HEIF decoder on any platform (mono/SkiaSharp#2887, #1700; confirmed on Windows and in the
        // Linux worker image), so HEIC is refused by name. How HEIC should be handled instead is #643.
        var result = await Process(BuildMinimalHeicContainer(), "image/heic");

        result.RejectionReason.ShouldBe(ImageRejectionReason.UnsupportedFormat);
    }

    [Fact]
    public async Task RefusesAHeicDeclaredFileThatAnotherDecoderWouldHaveAccepted()
    {
        // Twelve bytes that carry a HEIC brand at offsets 4-11 and are also a valid 8x1 WBMP from byte 0. Before HEIC
        // was refused by name, Skia's own sniffing handed these to its WBMP decoder and the upload was accepted.
        byte[] wbmpWearingAHeicBrand = [0x00, 0x00, 0x08, 0x01, .. "ftypheic"u8.ToArray()];
        using (var codec = SKCodec.Create(new MemoryStream(wbmpWearingAHeicBrand)))
        {
            codec.ShouldNotBeNull("the premise: Skia really would decode these bytes");
            codec.EncodedFormat.ShouldBe(SKEncodedImageFormat.Wbmp);
        }

        var result = await Process(wbmpWearingAHeicBrand, "image/heic");

        result.RejectionReason.ShouldBe(ImageRejectionReason.UnsupportedFormat);
    }

    [Fact]
    public async Task PropagatesCancellationRatherThanReportingARejection()
    {
        // A cancelled or failed run says nothing about the file, so it must not come back as a rejection — a
        // rejection is final, and the caller would never retry a valid photo.
        var jpeg = EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg);

        var exception = await Record.ExceptionAsync(
            () => new SkiaImageProcessor().ProcessAsync(new MemoryStream(jpeg), "image/jpeg", new CancellationToken(canceled: true)));

        exception.ShouldBeAssignableTo<OperationCanceledException>();
    }

    // ---- Helpers ----

    private static async Task<ImageProcessingResult> Process(byte[] upload, string declaredContentType)
    {
        var result = await new SkiaImageProcessor().ProcessAsync(new MemoryStream(upload), declaredContentType, Token);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static void AssertIsJpegOf(ProcessedImageVariant variant)
    {
        variant.Bytes.Span[..3].ToArray().ShouldBe([0xFF, 0xD8, 0xFF]);
        using var decoded = SKBitmap.Decode(variant.Bytes.ToArray());
        (decoded.Width, decoded.Height).ShouldBe((variant.WidthPx, variant.HeightPx), "reported dimensions match the bytes");
    }

    private static char Classify(SKColor pixel)
    {
        (char Name, SKColor Colour)[] references = [('R', Red), ('G', Green), ('B', Blue), ('Y', Yellow)];
        return references.MinBy(reference =>
            Math.Pow(pixel.Red - reference.Colour.Red, 2)
            + Math.Pow(pixel.Green - reference.Colour.Green, 2)
            + Math.Pow(pixel.Blue - reference.Colour.Blue, 2)).Name;
    }

    // ---- Fixture construction ----

    private const string FakeSourceIccMarker = "FAKE-SOURCE-ICC-PROFILE-MARKER";

    private static byte[] EncodeSolidColour(int width, int height, SKColor colour, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>Red top-left, green top-right, blue bottom-left, yellow bottom-right, at JPEG quality 100.</summary>
    private static byte[] EncodeQuadrants(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            float halfWidth = width / 2f, halfHeight = height / 2f;
            foreach (var (x, y, colour) in new[] { (0f, 0f, Red), (halfWidth, 0f, Green), (0f, halfHeight, Blue), (halfWidth, halfHeight, Yellow) })
            {
                using var paint = new SKPaint { Color = colour };
                canvas.DrawRect(SKRect.Create(x, y, halfWidth, halfHeight), paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>A genuine single-component JPEG: black left half, white right half.</summary>
    private static byte[] EncodeGrayscaleHalves(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var pixels = new byte[bitmap.RowBytes * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                pixels[(row * bitmap.RowBytes) + column] = column < width / 2 ? (byte)0 : (byte)255;
            }
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>Inserts one or more complete marker segments right after a JPEG's SOI (FF D8).</summary>
    private static byte[] SpliceAfterSoi(byte[] jpeg, params byte[][] segments)
    {
        using var result = new MemoryStream();
        result.Write(jpeg.AsSpan(0, 2));
        foreach (var segment in segments)
        {
            result.Write(segment);
        }

        result.Write(jpeg.AsSpan(2));
        return result.ToArray();
    }

    private static byte[] BuildJpegSegment(byte marker, byte[] payload)
    {
        using var segment = new MemoryStream();
        segment.WriteByte(0xFF);
        segment.WriteByte(marker);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        segment.Write(length);
        segment.Write(payload);
        return segment.ToArray();
    }

    /// <summary>A minimal, valid Exif TIFF/IFD0 carrying only the Orientation tag (0x0112).</summary>
    private static byte[] BuildExifOrientationSegment(ushort orientation)
    {
        var payload = new List<byte>();
        payload.AddRange("Exif\0\0"u8.ToArray());
        payload.AddRange("II"u8.ToArray());                // little-endian
        payload.AddRange([0x2A, 0x00]);                     // TIFF magic
        payload.AddRange([0x08, 0x00, 0x00, 0x00]);         // offset to IFD0
        payload.AddRange([0x01, 0x00]);                     // 1 entry
        payload.AddRange([0x12, 0x01]);                     // tag 0x0112 Orientation
        payload.AddRange([0x03, 0x00]);                     // type SHORT
        payload.AddRange([0x01, 0x00, 0x00, 0x00]);         // count 1
        payload.AddRange([(byte)orientation, 0x00, 0x00, 0x00]);
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);         // next IFD: none
        return BuildJpegSegment(0xE1, [.. payload]);
    }

    /// <summary>A minimal, valid Exif TIFF/IFD0 whose one entry points at a one-tag GPS IFD.</summary>
    private static byte[] BuildExifWithGpsSegment()
    {
        var payload = new List<byte>();
        payload.AddRange("Exif\0\0"u8.ToArray());
        payload.AddRange("II"u8.ToArray());
        payload.AddRange([0x2A, 0x00]);
        payload.AddRange([0x08, 0x00, 0x00, 0x00]);         // offset to IFD0
        payload.AddRange([0x01, 0x00]);                     // 1 entry in IFD0
        payload.AddRange([0x25, 0x88]);                     // tag 0x8825 GPSInfo pointer
        payload.AddRange([0x04, 0x00]);                     // type LONG
        payload.AddRange([0x01, 0x00, 0x00, 0x00]);         // count 1
        payload.AddRange([0x1A, 0x00, 0x00, 0x00]);         // GPS IFD at offset 26
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);         // next IFD: none

        // GPS IFD at offset 26 (8 header + 18 IFD0): one entry, GPSLatitudeRef = "N".
        payload.AddRange([0x01, 0x00]);                     // 1 entry
        payload.AddRange([0x01, 0x00]);                     // tag 0x0001 GPSLatitudeRef
        payload.AddRange([0x02, 0x00]);                     // type ASCII
        payload.AddRange([0x02, 0x00, 0x00, 0x00]);         // count 2
        payload.AddRange([(byte)'N', 0x00, 0x00, 0x00]);
        payload.AddRange([0x00, 0x00, 0x00, 0x00]);         // next IFD: none
        return BuildJpegSegment(0xE1, [.. payload]);
    }

    private static byte[] BuildIccProfileSegment()
        => BuildJpegSegment(0xE2, [.. "ICC_PROFILE\0"u8.ToArray(), 1, 1, .. Encoding.ASCII.GetBytes(FakeSourceIccMarker)]);

    private static byte[] BuildXmpSegment()
        => BuildJpegSegment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8.ToArray(), .. "<x:xmpmeta/>"u8.ToArray()]);

    private static byte[] BuildZipArchive(string entryName, string content)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(archive.CreateEntry(entryName).Open());
            entry.Write(content);
        }

        return output.ToArray();
    }

    /// <summary>
    /// A spec-correct PNG whose IHDR declares <paramref name="declaredWidth"/> × <paramref name="declaredHeight"/> while
    /// its IDAT holds one genuine, tiny, correctly zlib-compressed 1×1 scanline — deliberately inconsistent, which is
    /// what a decompression bomb looks like on the wire.
    /// </summary>
    private static byte[] BuildPngWithMismatchedHeaderDimensions(int declaredWidth, int declaredHeight)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), declaredWidth);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), declaredHeight);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour RGB
        WritePngChunk(stream, "IHDR", ihdr);

        // Filter-type byte (0 = none) and one RGB pixel.
        WritePngChunk(stream, "IDAT", ZlibCompress([0, 10, 20, 30]));
        WritePngChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData, 4);
        stream.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>
    /// An ISOBMFF <c>ftyp</c> box of the size a real HEIC file starts with — major brand <c>heic</c>, compatible brands
    /// <c>mif1</c> and <c>heic</c> — and no image after it.
    /// </summary>
    private static byte[] BuildMinimalHeicContainer()
    {
        using var stream = new MemoryStream();
        byte[] payload = [.. "heic"u8.ToArray(), 0, 0, 0, 0, .. "mif1"u8.ToArray(), .. "heic"u8.ToArray(), .. "miaf"u8.ToArray(), .. "MiHB"u8.ToArray()];
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)(8 + payload.Length));
        stream.Write(size);
        stream.Write("ftyp"u8);
        stream.Write(payload);
        return stream.ToArray();
    }
}
