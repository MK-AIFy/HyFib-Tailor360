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
    private const string FakeSourceIccMarker = "FAKE-SOURCE-ICC-PROFILE-MARKER";

    private static readonly SKColor Red = new(255, 0, 0);
    private static readonly SKColor Green = new(0, 255, 0);
    private static readonly SKColor Blue = new(0, 0, 255);
    private static readonly SKColor Yellow = new(255, 255, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- Accepted: decoded, stripped and derived ----

    [Fact]
    public async Task AcceptsAWellFormedJpegAndProducesAnOriginalPreviewAndThumbnail()
    {
        var jpeg = EncodeSolidColour(2000, 1000, SKColors.SeaGreen, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
        var image = result.Image.ShouldNotBeNull();

        // docs/nfr/capacity-and-performance.md §2.5: previews at 1,024 px and thumbnails at 256 px on the long edge.
        (image.Original.WidthPx, image.Original.HeightPx).ShouldBe((2000, 1000));
        (image.Preview.WidthPx, image.Preview.HeightPx).ShouldBe((1024, 512));
        (image.Thumbnail.WidthPx, image.Thumbnail.HeightPx).ShouldBe((256, 128));
        foreach (var variant in Variants(image))
        {
            variant.ContentType.ShouldBe("image/jpeg");
            AssertIsJpegOf(variant);
        }
    }

    [Fact]
    public async Task DownscalesAnOriginalLongerThanTheReEncodeTarget()
    {
        // capacity-and-performance.md §2.5 proposes storing originals at 2,400 px on the long edge (open decision CP-06).
        var jpeg = EncodeSolidColour(3000, 1500, SKColors.SeaGreen, SKEncodedImageFormat.Jpeg);

        var image = (await Process(jpeg, "image/jpeg")).Image.ShouldNotBeNull();

        (image.Original.WidthPx, image.Original.HeightPx).ShouldBe((2400, 1200));
        (image.Preview.WidthPx, image.Preview.HeightPx).ShouldBe((1024, 512));
        (image.Thumbnail.WidthPx, image.Thumbnail.HeightPx).ShouldBe((256, 128));
        AssertIsJpegOf(image.Original);
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
        foreach (var variant in Variants(result.Image.ShouldNotBeNull()))
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
    /// each EXIF orientation, into every variant. Each of the eight is a distinct arrangement, so this proves the
    /// transform for all of them, and proves the preview and thumbnail are cut from the oriented image rather than
    /// the decoded one. Expected corners follow the EXIF definition of each value.
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
    public async Task BakesEachExifOrientationIntoEveryVariant(int orientation, string expectedCorners)
    {
        var landscape = EncodeQuadrants(2000, 1000);
        var tagged = SpliceAfterSoi(landscape, BuildExifOrientationSegment((ushort)orientation));

        var image = (await Process(tagged, "image/jpeg")).Image.ShouldNotBeNull();

        var turnsQuarter = orientation >= 5;
        (image.Original.WidthPx, image.Original.HeightPx).ShouldBe(turnsQuarter ? (1000, 2000) : (2000, 1000));
        (image.Preview.WidthPx, image.Preview.HeightPx).ShouldBe(turnsQuarter ? (512, 1024) : (1024, 512));
        (image.Thumbnail.WidthPx, image.Thumbnail.HeightPx).ShouldBe(turnsQuarter ? (128, 256) : (256, 128));
        foreach (var variant in Variants(image))
        {
            Corners(variant).ShouldBe(
                expectedCorners, $"orientation {orientation}, {variant.WidthPx}x{variant.HeightPx}: TL, TR, BL, BR");
        }
    }

    [Fact]
    public async Task FlattensTransparencyOntoWhiteInEveryVariant()
    {
        // JPEG has no alpha channel, and Skia's encoder writes a transparent pixel as black: an unflattened transparent
        // reference image (a garment cut-out, say) would come back as a black rectangle. Large enough that the preview
        // and thumbnail are resized from the flattened image, not reused.
        var transparent = EncodeSolidColour(2000, 2000, SKColors.Transparent, SKEncodedImageFormat.Png);

        var image = (await Process(transparent, "image/png")).Image.ShouldNotBeNull();

        image.Thumbnail.ShouldNotBeSameAs(image.Original);
        foreach (var variant in Variants(image))
        {
            using var decoded = SKBitmap.Decode(variant.Bytes.ToArray());
            var pixel = decoded.GetPixel(decoded.Width / 2, decoded.Height / 2);
            (pixel.Red, pixel.Green, pixel.Blue).ShouldBe(((byte)255, (byte)255, (byte)255), $"{variant.WidthPx} px variant");
        }
    }

    [Fact]
    public async Task DecodesAGrayscaleJpegAndStillAppliesItsOrientation()
    {
        var grayscale = EncodeGrayscaleHalves(32, 16);
        using (var codec = SKCodec.Create(new MemoryStream(grayscale)))
        {
            codec.Info.ColorType.ShouldBe(SKColorType.Gray8, "the premise: a genuine single-component JPEG");
        }

        var tagged = SpliceAfterSoi(grayscale, BuildExifOrientationSegment(orientation: 6));
        var original = (await Process(tagged, "image/jpeg")).Image.ShouldNotBeNull().Original;

        (original.WidthPx, original.HeightPx).ShouldBe((16, 32));
        using var decoded = SKBitmap.Decode(original.Bytes.ToArray());
        decoded.GetPixel(8, 4).Red.ShouldBeLessThan((byte)60, "the source's dark left half becomes the top");
        decoded.GetPixel(8, 28).Red.ShouldBeGreaterThan((byte)200, "and its light right half the bottom");
    }

    [Fact]
    public async Task ConvertsAWideGamutSourceIntoSrgb()
    {
        // A Display P3 photograph — what a recent phone camera writes — carries its own ICC profile. The adapter decodes
        // into sRGB, so the source's profile governs the conversion and never reaches an output.
        var displayP3 = EncodeDisplayP3Red(64, 64);
        using (var codec = SKCodec.Create(new MemoryStream(displayP3)))
        {
            codec.Info.ColorSpace.ShouldNotBeNull();
            codec.Info.ColorSpace.IsSrgb.ShouldBeFalse("the premise: the input really is tagged as another colour space");
        }

        var image = (await Process(displayP3, "image/jpeg")).Image.ShouldNotBeNull();

        foreach (var variant in Variants(image))
        {
            using var codec = SKCodec.Create(new MemoryStream(variant.Bytes.ToArray()));
            codec.Info.ColorSpace.ShouldNotBeNull();
            codec.Info.ColorSpace.IsSrgb.ShouldBeTrue();
        }
    }

    [Fact]
    public void DropsTheAncillaryPngChunksThatCannotChangeAPixel()
    {
        // Text chunks above all: libpng inflates every compressed one it is handed, about 13 ms of uncancellable work
        // each, and a thousand fit under the upload cap. Pixel-affecting ones stay, but only the first of each.
        var png = BuildPng(
            ("IHDR", Ihdr(1, 1)),
            ("tEXt", "Comment\0hello"u8.ToArray()),
            ("gAMA", [0x00, 0x00, 0xB1, 0x8F]),
            ("zTXt", [.. "Comment\0"u8.ToArray(), 0, .. ZlibCompress(new byte[100_000])]),
            ("sRGB", [0]),
            ("sRGB", [0]),
            ("pHYs", new byte[9]),
            ("IDAT", ZlibCompress([0, 10, 20, 30])),
            ("iTXt", [.. "Comment\0"u8.ToArray(), 0, 0, 0, 0, .. "late text"u8.ToArray()]),
            ("IEND", []));

        var filtered = SkiaImageProcessor.WithoutInertPngChunks(new ArraySegment<byte>(png));

        ChunkTypes(filtered).ShouldBe(["IHDR", "gAMA", "sRGB", "IDAT", "IEND"]);
    }

    [Fact]
    public void LeavesAPngWithNothingToDropUntouched()
    {
        var png = BuildPng(("IHDR", Ihdr(1, 1)), ("gAMA", [0x00, 0x00, 0xB1, 0x8F]), ("IDAT", ZlibCompress([0, 1, 2, 3])), ("IEND", []));

        var filtered = SkiaImageProcessor.WithoutInertPngChunks(new ArraySegment<byte>(png));

        filtered.Array.ShouldBeSameAs(png, "no copy is made when there is nothing to drop");
    }

    [Fact]
    public async Task AcceptsAPngCarryingCompressedTextChunksWithoutInflatingThem()
    {
        var textBomb = BuildPngWithCompressedTextChunks(chunkCount: 20, inflatedBytesPerChunk: 7_900_000);

        var result = await Process(textBomb, "image/png");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted, "the text chunks are dropped; the 1x1 image itself is fine");
    }

    [Fact]
    public async Task StillReportsATruncatedPngAsTruncatedAfterDroppingItsTextChunks()
    {
        var png = EncodeNoise(64, 64, SKEncodedImageFormat.Png);
        var withText = SplicePngChunkAfterIhdr(png, "tEXt", "Comment\0hello"u8.ToArray());

        var result = await Process(withText[..(withText.Length * 6 / 10)], "image/png");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Truncated);
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
        var polyglot = SmallJpeg().Concat(BuildZipArchive("payload.txt", "not really a picture")).ToArray();

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task RejectsAnAppendedZipThatOnlyItsAbsoluteDirectoryOffsetLocates()
    {
        // .NET's ZipArchive and Info-ZIP seek to the offset the end record states and ignore its size field, so an
        // archive with offsets rewritten to be absolute and a zeroed size still opens in them.
        var jpeg = SmallJpeg();
        var polyglot = jpeg.Concat(BuildZipArchive("payload.txt", "not really a picture")).ToArray();
        var endRecord = polyglot.Length - 22;
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(polyglot.AsSpan(endRecord + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(polyglot.AsSpan(endRecord + 16, 4), offset + (uint)jpeg.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(polyglot.AsSpan(endRecord + 12, 4), 0);

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task RejectsAnAppendedZip64EndRecord()
    {
        // A ZIP64 archive leaves its classic end record's fields at 0xFFFF…; the locator just before it is the shape.
        byte[] locator = [.. "PK\x06\x07"u8.ToArray(), .. new byte[16]];
        byte[] endRecord = [.. "PK\x05\x06"u8.ToArray(), .. Enumerable.Repeat((byte)0xFF, 16), 0, 0];
        var polyglot = SmallJpeg().Concat(locator).Concat(endRecord).ToArray();

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task AcceptsAJpegWhoseCommentMerelyContainsZipSignatures()
    {
        // The false positive a bare four-byte scan produced about once per 700 three-megabyte photographs. The end-record
        // candidate's size field points four bytes back — at a local-file signature, not a central-directory one — and
        // its offset field at the start of the file, so both structural comparisons run and both fail.
        byte[] payload = [.. "PK\x03\x04"u8.ToArray(), .. "PK\x05\x06"u8.ToArray(), .. new byte[8], 4, 0, 0, 0, .. new byte[6]];
        var jpeg = SpliceAfterSoi(SmallJpeg(), BuildJpegSegment(0xFE, payload));

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1018)]
    public async Task RejectsAPdfHeaderWhereAPdfReaderWouldFindIt(int padding)
    {
        // The comment segment's payload starts at byte 6, so padding 1018 puts the header at byte 1024 — the last
        // offset PDFium accepts.
        var comment = BuildJpegSegment(0xFE, [.. new byte[padding], .. "%PDF-1.7\n1 0 obj << >> endobj\n"u8.ToArray()]);
        var polyglot = SpliceAfterSoi(SmallJpeg(), comment);

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task AcceptsAPdfMarkerFarBeyondWhereAPdfReaderLooks()
    {
        var comment = BuildJpegSegment(0xFE, [.. new byte[2000], .. "%PDF-1.7"u8.ToArray()]);
        var jpeg = SpliceAfterSoi(SmallJpeg(), comment);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
    }

    [Theory]
    [InlineData("<SCRIPT>alert(document.cookie)</SCRIPT>")]
    [InlineData("<Html><body>")]
    [InlineData("<iFrame src=x>")]
    [InlineData("<SVG onload=x>")]
    [InlineData("<!DOCTYPE HTML>")]
    public async Task RejectsScriptBearingMarkupNearTheStartWhateverItsCase(string markup)
    {
        var polyglot = SpliceAfterSoi(SmallJpeg(), BuildJpegSegment(0xFE, Encoding.ASCII.GetBytes(markup)));

        var result = await Process(polyglot, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.PolyglotContent);
    }

    [Fact]
    public async Task AcceptsMarkupFarBeyondWhereABrowserSniffs()
    {
        // Scanning the whole file for markup would reject real photographs: a short tag occurs by chance in megabytes.
        var comment = BuildJpegSegment(0xFE, [.. new byte[2000], .. "<svg onload=x>"u8.ToArray()]);
        var jpeg = SpliceAfterSoi(SmallJpeg(), comment);

        var result = await Process(jpeg, "image/jpeg");

        result.Status.ShouldBe(ImageProcessingStatus.Accepted);
    }

    [Fact]
    public async Task RejectsAPhpOpenTagAnywhereInTheFile()
    {
        // Deliberately past the markup window: PHP runs an open tag wherever it sits in an included file.
        var comment = BuildJpegSegment(0xFE, [.. new byte[4000], .. "<?PHP echo 1; ?>"u8.ToArray()]);
        var polyglot = SpliceAfterSoi(SmallJpeg(), comment);

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
    public async Task RejectsAStreamThatEndsBeforeDecodingFinishes()
    {
        // Only a stream that runs out: a JPEG whose scan data is cut but followed by an end marker is, by libjpeg's
        // rules, a warning — it is grey-filled and accepted (see SkiaImageProcessor's remarks).
        var jpeg = EncodeSolidColour(800, 600, SKColors.Purple, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg[..(jpeg.Length - 200)], "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Truncated);
    }

    [Fact]
    public async Task RejectsAJpegCutOffInsideItsHeaderAsTruncated()
    {
        // Cut before the frame header is complete, so the codec cannot even be opened — the other truncation path.
        var jpeg = EncodeSolidColour(800, 600, SKColors.Purple, SKEncodedImageFormat.Jpeg);

        var result = await Process(jpeg[..100], "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Truncated);
    }

    [Fact]
    public async Task RejectsAJpegWithAnImpossibleFrameHeaderAsMalformed()
    {
        // Every byte present, but a frame header declaring zero colour components: libjpeg calls that an error, not
        // missing input. (A corrupt PNG cannot be used here — Skia reports every libpng error as incomplete input.)
        var jpeg = SmallJpeg();
        var frameHeader = jpeg.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xC0]);
        frameHeader.ShouldBeGreaterThan(0, "the premise: a baseline frame header to corrupt");
        jpeg[frameHeader + 9] = 0;

        var result = await Process(jpeg, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Malformed);
    }

    [Fact]
    public async Task RejectsGarbageBytesBehindAValidLeadingSignatureAsTruncated()
    {
        // libjpeg skips the zeros looking for the next marker and runs out of input doing it — so this is reported as
        // truncated, deliberately pinned here rather than left to either of two answers.
        var garbage = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(Enumerable.Repeat((byte)0x00, 300)).ToArray();

        var result = await Process(garbage, "image/jpeg");

        result.RejectionReason.ShouldBe(ImageRejectionReason.Truncated);
    }

    [Fact]
    public async Task RefusesHeicAsUnsupportedWithoutHandingItToAnyDecoder()
    {
        // SkiaSharp has no HEIF decoder on any platform (mono/SkiaSharp#2887, #1700; confirmed on Windows and in a Linux
        // container), so HEIC is refused by name. How HEIC should be handled instead is #643.
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
    public async Task PropagatesCancellationRequestedBeforeProcessingRatherThanRejecting()
    {
        // Proves only the check before processing starts; the checks between decode and each encode are single
        // statements with nothing in them to test without a seam, and are verified by inspection.
        var exception = await Record.ExceptionAsync(
            () => new SkiaImageProcessor().ProcessAsync(new MemoryStream(SmallJpeg()), "image/jpeg", new CancellationToken(canceled: true)));

        exception.ShouldBeAssignableTo<OperationCanceledException>();
    }

    // ---- Helpers ----

    private static async Task<ImageProcessingResult> Process(byte[] upload, string declaredContentType)
    {
        var result = await new SkiaImageProcessor().ProcessAsync(new MemoryStream(upload), declaredContentType, Token);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static ProcessedImageVariant[] Variants(ProcessedImage image) => [image.Original, image.Preview, image.Thumbnail];

    private static void AssertIsJpegOf(ProcessedImageVariant variant)
    {
        variant.Bytes.Span[..3].ToArray().ShouldBe([0xFF, 0xD8, 0xFF]);
        using var decoded = SKBitmap.Decode(variant.Bytes.ToArray());
        (decoded.Width, decoded.Height).ShouldBe((variant.WidthPx, variant.HeightPx), "reported dimensions match the bytes");
    }

    /// <summary>The colour at each quadrant's centre — top-left, top-right, bottom-left, bottom-right — as R, G, B or Y.</summary>
    private static string Corners(ProcessedImageVariant variant)
    {
        using var decoded = SKBitmap.Decode(variant.Bytes.ToArray());
        var (w, h) = (decoded.Width, decoded.Height);
        return string.Concat(
            Classify(decoded.GetPixel(w / 4, h / 4)),
            Classify(decoded.GetPixel(3 * w / 4, h / 4)),
            Classify(decoded.GetPixel(w / 4, 3 * h / 4)),
            Classify(decoded.GetPixel(3 * w / 4, 3 * h / 4)));
    }

    private static char Classify(SKColor pixel)
    {
        (char Name, SKColor Colour)[] references = [('R', Red), ('G', Green), ('B', Blue), ('Y', Yellow)];
        return references.MinBy(reference =>
            Math.Pow(pixel.Red - reference.Colour.Red, 2)
            + Math.Pow(pixel.Green - reference.Colour.Green, 2)
            + Math.Pow(pixel.Blue - reference.Colour.Blue, 2)).Name;
    }

    private static string[] ChunkTypes(ArraySegment<byte> png)
    {
        var types = new List<string>();
        var bytes = png.AsSpan();
        for (var at = 8; at + 8 <= bytes.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
            types.Add(Encoding.ASCII.GetString(bytes.Slice(at + 4, 4)));
            at += 12 + length;
        }

        return [.. types];
    }

    // ---- Fixture construction ----

    private static byte[] SmallJpeg() => EncodeSolidColour(200, 200, SKColors.Gray, SKEncodedImageFormat.Jpeg);

    private static byte[] EncodeSolidColour(int width, int height, SKColor colour, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var data = bitmap.Encode(format, 90);
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

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
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
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>Pure Display P3 red, written raw into a P3-tagged bitmap, so the JPEG embeds a real P3 ICC profile.</summary>
    private static byte[] EncodeDisplayP3Red(int width, int height)
    {
        var displayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, displayP3));
        var pixels = new byte[bitmap.RowBytes * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)255, (byte)0, (byte)0, (byte)255);
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>Deterministic noise, so the encoded file has a large pixel-data chunk to cut into.</summary>
    private static byte[] EncodeNoise(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = new byte[bitmap.RowBytes * height];
        new Random(597).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var data = bitmap.Encode(format, 100);
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

    private static byte[] Ihdr(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour RGB
        return ihdr;
    }

    private static byte[] BuildPng(params (string Type, byte[] Data)[] chunks)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        foreach (var (type, data) in chunks)
        {
            WritePngChunk(stream, type, data);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A spec-correct PNG whose IHDR declares <paramref name="declaredWidth"/> × <paramref name="declaredHeight"/> while
    /// its IDAT holds one genuine, tiny, correctly zlib-compressed 1×1 scanline — deliberately inconsistent, which is
    /// what a decompression bomb looks like on the wire.
    /// </summary>
    private static byte[] BuildPngWithMismatchedHeaderDimensions(int declaredWidth, int declaredHeight)
        => BuildPng(("IHDR", Ihdr(declaredWidth, declaredHeight)), ("IDAT", ZlibCompress([0, 10, 20, 30])), ("IEND", []));

    /// <summary>
    /// A 1×1 PNG carrying <paramref name="chunkCount"/> zTXt chunks, each a run of zeros that deflates to a few KB and
    /// inflates to <paramref name="inflatedBytesPerChunk"/> — kept under libpng's default 8,000,000-byte per-chunk
    /// ceiling, so libpng would inflate every one of them rather than refusing the chunk.
    /// </summary>
    private static byte[] BuildPngWithCompressedTextChunks(int chunkCount, int inflatedBytesPerChunk)
    {
        byte[] ztxt = [.. "Comment\0"u8.ToArray(), 0, .. ZlibCompress(new byte[inflatedBytesPerChunk])];
        return BuildPng(
            [("IHDR", Ihdr(1, 1)), .. Enumerable.Repeat(("zTXt", ztxt), chunkCount), ("IDAT", ZlibCompress([0, 10, 20, 30])), ("IEND", [])]);
    }

    private static byte[] SplicePngChunkAfterIhdr(byte[] png, string type, byte[] data)
    {
        using var chunk = new MemoryStream();
        WritePngChunk(chunk, type, data);
        const int afterIhdr = 8 + 12 + 13;
        return [.. png.AsSpan(0, afterIhdr), .. chunk.ToArray(), .. png.AsSpan(afterIhdr)];
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
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
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
        byte[] payload = [.. "heic"u8.ToArray(), 0, 0, 0, 0, .. "mif1"u8.ToArray(), .. "heic"u8.ToArray(), .. "miaf"u8.ToArray(), .. "MiHB"u8.ToArray()];
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        "ftyp"u8.CopyTo(box.AsSpan(4));
        payload.CopyTo(box, 8);
        return box;
    }
}
