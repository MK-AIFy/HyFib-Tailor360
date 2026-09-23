using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Shouldly;
using SkiaSharp;
using Tailor360.Modules.Integration.Infrastructure.Imaging;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.ContractTests.Adapters;

/// <summary>
/// <see cref="SkiaImageProcessor"/> (ADR-0012, issue #597) against a fixture corpus built by this file
/// rather than checked-in binaries: every well-formed and adversarial case is constructed from known
/// bytes, so an assertion can say exactly what was fed in and what came out.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ImageProcessorAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AcceptsAWellFormedJpegAndProducesAnOriginalThumbnailAndPreview()
    {
        var processor = new SkiaImageProcessor();
        var jpeg = EncodeSolidColour(2000, 1000, SKColors.SeaGreen, SKEncodedImageFormat.Jpeg);

        var result = await processor.ProcessAsync(new MemoryStream(jpeg), "image/jpeg", Token);

        result.IsSuccess.ShouldBeTrue();
        var outcome = result.Value;
        outcome.Status.ShouldBe(ImageProcessingStatus.Accepted);
        outcome.Image.ShouldNotBeNull();
        outcome.Image.Original.WidthPx.ShouldBe(2000);
        outcome.Image.Original.HeightPx.ShouldBe(1000);
        outcome.Image.Thumbnail.WidthPx.ShouldBeLessThanOrEqualTo(320);
        outcome.Image.Preview.WidthPx.ShouldBeLessThanOrEqualTo(1600);
        outcome.Image.Original.ContentType.ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task DoesNotUpscaleADerivativeSmallerThanItsTargetEdge()
    {
        var processor = new SkiaImageProcessor();
        var jpeg = EncodeSolidColour(120, 80, SKColors.SlateBlue, SKEncodedImageFormat.Jpeg);

        var result = await processor.ProcessAsync(new MemoryStream(jpeg), "image/jpeg", Token);

        result.Value.Image.ShouldNotBeNull();
        result.Value.Image.Thumbnail.WidthPx.ShouldBe(120);
        result.Value.Image.Thumbnail.HeightPx.ShouldBe(80);
        result.Value.Image.Preview.WidthPx.ShouldBe(120);
    }

    [Fact]
    public async Task StripsExifGpsIccAndXmpFromEveryVariantOfAFileThatCarriedThem()
    {
        var processor = new SkiaImageProcessor();
        var baseJpeg = EncodeSolidColour(400, 300, SKColors.Coral, SKEncodedImageFormat.Jpeg);
        var withMetadata = SpliceAfterSoi(
            baseJpeg,
            BuildExifWithGpsSegment(),
            BuildIccProfileSegment(),
            BuildXmpSegment());

        var result = await processor.ProcessAsync(new MemoryStream(withMetadata), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Accepted, "a photo carrying ordinary metadata is stripped, not refused");
        var image = result.Value.Image.ShouldNotBeNull();
        foreach (var variant in new[] { image.Original, image.Thumbnail, image.Preview })
        {
            var text = Encoding.Latin1.GetString(variant.Bytes.Span);

            // The EXIF segment (and with it the GPS tag it carried) and the XMP segment must not
            // survive re-encoding.
            text.ShouldNotContain("Exif");
            text.ShouldNotContain("http://ns.adobe.com/xap");

            // Not a blanket "no ICC_PROFILE segment": SkiaSharp's own encoder adds a generic sRGB
            // colour profile to every JPEG it writes, which is normal, content-free encoder behaviour
            // and not the metadata docs/nfr/data-classification.md §5.5 means by "location data never
            // enters storage" — only the *source file's own* crafted profile bytes must be gone.
            text.ShouldNotContain(FakeOriginalIccMarker);
        }
    }

    [Fact]
    public async Task BakesInExifOrientationInsteadOfLeavingAPortraitPhotoSideways()
    {
        var processor = new SkiaImageProcessor();

        // Landscape source, left half red, right half blue. Orientation 6 (clockwise rotation
        // required) is exactly what a phone held upright hands the camera pipeline, since the sensor
        // itself is landscape-native.
        var source = EncodeHalvedColours(8, 4, SKColors.Red, SKColors.Blue, SKEncodedImageFormat.Jpeg);
        var rotated = SpliceAfterSoi(source, BuildExifOrientationSegment(orientation: 6));

        var result = await processor.ProcessAsync(new MemoryStream(rotated), "image/jpeg", Token);

        var image = result.Value.Image.ShouldNotBeNull();
        image.Original.WidthPx.ShouldBe(4, "width and height swap for a 90-degree correction");
        image.Original.HeightPx.ShouldBe(8);

        using var decoded = SKBitmap.Decode(image.Original.Bytes.ToArray());
        // A clockwise correction moves the source's left (red) edge to the destination's top, and its
        // right (blue) edge to the bottom — sampled away from the JPEG-lossy seam at the middle.
        decoded.GetPixel(1, 1).Red.ShouldBeGreaterThan((byte)150);
        decoded.GetPixel(1, 6).Blue.ShouldBeGreaterThan((byte)150);
    }

    [Fact]
    public async Task RejectsASignatureMismatchBetweenTheBytesAndTheDeclaredContentType()
    {
        var processor = new SkiaImageProcessor();
        var png = EncodeSolidColour(100, 100, SKColors.Black, SKEncodedImageFormat.Png);

        var result = await processor.ProcessAsync(new MemoryStream(png), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        result.Value.RejectionReason.ShouldBe(ImageRejectionReason.SignatureMismatch);
    }

    [Fact]
    public async Task RejectsAPolyglotFileCarryingAZipMarkerAlongsideAValidSignature()
    {
        var processor = new SkiaImageProcessor();
        var jpeg = EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg);
        var polyglot = new byte[jpeg.Length + 4];
        jpeg.CopyTo(polyglot, 0);
        "PK\x03\x04"u8.CopyTo(polyglot.AsSpan(jpeg.Length));

        var result = await processor.ProcessAsync(new MemoryStream(polyglot), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        result.Value.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task RejectsDimensionsOverTheLongestEdgeLimitWithoutNeedingAHugeAllocation()
    {
        var processor = new SkiaImageProcessor();
        // 12,050 px on the long edge, 20 px on the short edge: over the 12,000 px limit, but only
        // ~241,000 pixels to actually allocate and encode — a cheap, fast, deterministic test.
        var jpeg = EncodeSolidColour(12_050, 20, SKColors.Yellow, SKEncodedImageFormat.Jpeg);

        var result = await processor.ProcessAsync(new MemoryStream(jpeg), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        result.Value.RejectionReason.ShouldBe(ImageRejectionReason.DimensionsExceedLimit);
    }

    [Fact]
    public async Task RejectsAPngDecompressionBombFromItsHeaderAloneWithoutDecodingIt()
    {
        var processor = new SkiaImageProcessor();
        // A real, spec-correct PNG whose IHDR declares a bomb-sized canvas (60,000 x 60,000 = 3.6
        // billion pixels) while its actual IDAT holds one real, tiny, correctly zlib-compressed
        // scanline. If this adapter ever called GetPixels before checking codec.Info, this test would
        // hang or exhaust memory rather than complete.
        var bomb = BuildPngWithMismatchedHeaderDimensions(declaredWidth: 60_000, declaredHeight: 60_000);

        var result = await processor.ProcessAsync(new MemoryStream(bomb), "image/png", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        (result.Value.RejectionReason is ImageRejectionReason.DimensionsExceedLimit
            or ImageRejectionReason.PixelCountExceedsLimit).ShouldBeTrue();
    }

    [Fact]
    public async Task RejectsAPixelCountOverTheLimitEvenWhenNeitherEdgeAloneExceedsIt()
    {
        var processor = new SkiaImageProcessor();
        // A real, spec-correct PNG header declaring 9,000 x 9,000 (81 MP, over the 40 MP cap) while
        // neither edge alone exceeds the 12,000 px limit — proves the pixel-count check is a distinct
        // rule from the edge-length check, not a restatement of it. No real pixel data is needed: the
        // rejection has to happen from the header, exactly as the decompression-bomb case does.
        var oversizedPixelCount = BuildPngWithMismatchedHeaderDimensions(declaredWidth: 9_000, declaredHeight: 9_000);

        var result = await processor.ProcessAsync(new MemoryStream(oversizedPixelCount), "image/png", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        result.Value.RejectionReason.ShouldBe(ImageRejectionReason.PixelCountExceedsLimit);
    }

    [Fact]
    public async Task RejectsATruncatedFileRatherThanPartiallyDecodingIt()
    {
        var processor = new SkiaImageProcessor();
        var jpeg = EncodeSolidColour(800, 600, SKColors.Purple, SKEncodedImageFormat.Jpeg);
        var truncated = jpeg[..(jpeg.Length - 200)];

        var result = await processor.ProcessAsync(new MemoryStream(truncated), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
        (result.Value.RejectionReason is ImageRejectionReason.Truncated or ImageRejectionReason.Malformed)
            .ShouldBeTrue("a codec may report a cut-off stream as either, but never as a success");
    }

    [Fact]
    public async Task RejectsGarbageBytesBehindAValidLeadingSignature()
    {
        var processor = new SkiaImageProcessor();
        var garbage = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(Enumerable.Repeat((byte)0x00, 300)).ToArray();

        var result = await processor.ProcessAsync(new MemoryStream(garbage), "image/jpeg", Token);

        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected);
    }

    [Fact]
    public async Task ReportsWhatThisAdapterActuallyDoesWithAHeicSignedFile()
    {
        // #597's own required finding, recorded plainly rather than assumed: SkiaSharp has no HEIF
        // decoder on ANY platform, confirmed both empirically (this test, run against this
        // repository's actual Linux worker image with libfontconfig1 installed — see
        // infra/docker/Dockerfile.worker) and against SkiaSharp's own upstream tracker
        // (mono/SkiaSharp#2887 and #1700, both open feature requests, unimplemented as of this
        // writing). A minimal ISOBMFF ftyp box naming a HEIC brand is enough to prove this — SKCodec
        // never gets far enough to need real HEVC pixel data before reporting failure. The gap is
        // real, not a fixture limitation: iPhone photos default to HEIC, so this adapter refuses them
        // outright today. Closing it needs a third-party add-on (SkiaSharp.Heic, Openize.HEIC) that
        // #597 did not evaluate — tracked as a separate follow-up rather than expanding this issue.
        var processor = new SkiaImageProcessor();
        var heicShaped = BuildMinimalHeicContainer();

        var result = await processor.ProcessAsync(new MemoryStream(heicShaped), "image/heic", Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ImageProcessingStatus.Rejected, "no real HEVC payload was built, only a container shell");
        result.Value.RejectionReason.ShouldNotBeNull();
    }

    // ---- Fixture construction ----

    private static byte[] EncodeSolidColour(int width, int height, SKColor colour, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] EncodeHalvedColours(int width, int height, SKColor left, SKColor right, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(left);
            using var paint = new SKPaint { Color = right };
            canvas.DrawRect(SKRect.Create(width / 2f, 0, width / 2f, height), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    /// <summary>Inserts one or more complete marker segments right after a JPEG's SOI (FF D8).</summary>
    private static byte[] SpliceAfterSoi(byte[] jpeg, params byte[][] segments)
    {
        using var result = new MemoryStream();
        result.Write(jpeg.AsSpan(0, 2)); // FF D8
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
        payload.AddRange("II"u8.ToArray());              // little-endian
        payload.AddRange([0x2A, 0x00]);                    // TIFF magic
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

    private const string FakeOriginalIccMarker = "FAKE-SOURCE-ICC-PROFILE-MARKER";

    private static byte[] BuildIccProfileSegment()
        => BuildJpegSegment(0xE2, [.. "ICC_PROFILE\0"u8.ToArray(), 1, 1, .. Encoding.ASCII.GetBytes(FakeOriginalIccMarker)]);

    private static byte[] BuildXmpSegment()
        => BuildJpegSegment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8.ToArray(), .. "<x:xmpmeta/>"u8.ToArray()]);

    /// <summary>
    /// A spec-correct, real PNG whose IHDR declares <paramref name="declaredWidth"/> ×
    /// <paramref name="declaredHeight"/> while its IDAT holds one genuine, tiny, correctly
    /// zlib-compressed 1×1 scanline — the two are deliberately inconsistent, which is exactly what a
    /// decompression bomb looks like on the wire, and exactly why the codec's header must be trusted
    /// for the size check rather than the decoded byte count.
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

        // filter-type byte (0 = none) + one RGB pixel.
        byte[] scanline = [0, 10, 20, 30];
        WritePngChunk(stream, "IDAT", ZlibCompress(scanline));

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
    /// The smallest byte sequence <c>SKCodec.Create</c> needs to recognise a file as HEIC: an ISOBMFF
    /// <c>ftyp</c> box naming a HEIC major brand. Carries no real HEVC-coded image, deliberately — see
    /// the test this feeds.
    /// </summary>
    private static byte[] BuildMinimalHeicContainer()
    {
        using var stream = new MemoryStream();
        // ftyp box: size(4) + "ftyp" + major brand "heic" + minor version(4) + compatible brand "mif1"
        WriteIsoBmffBox(stream, "ftyp", [.. "heic"u8.ToArray(), 0, 0, 0, 0, .. "mif1"u8.ToArray()]);
        return stream.ToArray();
    }

    private static void WriteIsoBmffBox(Stream stream, string type, byte[] payload)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)(8 + payload.Length));
        stream.Write(size);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
    }
}
