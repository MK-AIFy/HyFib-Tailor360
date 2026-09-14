using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Documents;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Barcodes;
using ZXing.Common;
using ZXing.OneD;

namespace Tailor360.ContractTests.Adapters;

/// <summary>
/// The adapters behind the document ports (#155, ADR-0014): the PDF renderer is deterministic for a model and
/// refuses a template it does not know, the barcode renderer draws Code 128 the scanner reads back, the
/// in-memory store keeps what it was given, and the MinIO adapter — against the compose stack's MinIO when
/// <c>TAILOR360_TEST_S3_ENDPOINT</c> names one — round-trips an object.
/// </summary>
[Trait("Category", "Contract")]
public sealed class DocumentAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RendersTheSameBytesTwiceForOneModelAndDifferentBytesForAnother()
    {
        var renderer = new QuestPdfRenderer();
        var model = SampleModel("INV-MAIN-2627-000001");

        var first = await RenderAsync(renderer, QuestPdfRenderer.InvoiceTemplate, model);
        var second = await RenderAsync(renderer, QuestPdfRenderer.InvoiceTemplate, model);
        var other = await RenderAsync(renderer, QuestPdfRenderer.InvoiceTemplate, SampleModel("INV-MAIN-2627-000002"));

        first.Length.ShouldBeGreaterThan(1000);
        first[..5].ShouldBe("%PDF-"u8.ToArray());
        Convert.ToHexStringLower(SHA256.HashData(first)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(second)), "a rendering is deterministic for its model");
        Convert.ToHexStringLower(SHA256.HashData(other)).ShouldNotBe(Convert.ToHexStringLower(SHA256.HashData(first)));

        var note = await RenderAsync(renderer, QuestPdfRenderer.CreditNoteTemplate, SampleModel("CN-MAIN-2627-000001"));
        note.Length.ShouldBeGreaterThan(1000);

        using var destination = new MemoryStream();
        var refused = await renderer.RenderAsync("billing.estimate", model, destination, Token);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("integration.template-not-known");
    }

    [Fact]
    public async Task RendersTheReceiptOnTheRollDeterministicallyWithItsFiguresAndBarcode()
    {
        var renderer = new QuestPdfRenderer();
        var model = ReceiptModel("RCPT-MAIN-2627-000001");

        var first = await RenderAsync(renderer, QuestPdfRenderer.ReceiptTemplate, model);
        var second = await RenderAsync(renderer, QuestPdfRenderer.ReceiptTemplate, model);
        var other = await RenderAsync(renderer, QuestPdfRenderer.ReceiptTemplate, ReceiptModel("RCPT-MAIN-2627-000002"));

        first.Length.ShouldBeGreaterThan(500);
        first[..5].ShouldBe("%PDF-"u8.ToArray());
        Convert.ToHexStringLower(SHA256.HashData(first)).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(second)), "a rendering is deterministic for its model");
        Convert.ToHexStringLower(SHA256.HashData(other)).ShouldNotBe(Convert.ToHexStringLower(SHA256.HashData(first)));

        // The roll: one page, 80 mm wide, whatever its length; the MediaBox says so in points (80 mm is 226.77 pt).
        var text = System.Text.Encoding.Latin1.GetString(first);
        var box = System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+[0-9.]+\s*\]");
        box.Success.ShouldBeTrue(text[..Math.Min(text.Length, 1500)]);
        decimal.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture).ShouldBe(226.77m, 0.1m);
        text.ShouldContain("/Count 1");
    }

    private static Dictionary<string, object?> ReceiptModel(string number) => new(StringComparer.Ordinal)
    {
        ["kind"] = "Receipt",
        ["number"] = number,
        ["issuedOn"] = "12-09-2026",
        ["financialYear"] = "2627",
        ["renderedAt"] = "2026-09-12T04:30:00.0000000+00:00",
        ["currency"] = "INR",
        ["barcodePayload"] = "R-7K3M9QW2XZ4B",
        ["branchName"] = "Main branch",
        ["orderNumber"] = "O-MAIN-2627-000001",
        ["modeCode"] = "UPI",
        ["modeName"] = "UPI",
        ["reference"] = "UPI-426114-8QX2",
        ["amount"] = 1000m,
        ["allocated"] = 567m,
        ["unappliedAdvance"] = 433m,
        ["orderOutstanding"] = 0m,
        ["allocations"] = new List<object?>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["invoiceNumber"] = "INV-MAIN-2627-000001", ["amount"] = 567m },
        },
    };

    [Fact]
    public async Task DrawsCode128ThatCarriesThePayloadAndRefusesAnotherSymbology()
    {
        var renderer = new Code128BarcodeRenderer();
        var payload = BarcodePayload.Mint(BarcodePayload.InvoiceNamespace).Value;

        var svg = Code128BarcodeRenderer.Svg(payload, 200, 40);
        svg.ShouldStartWith("<svg");
        svg.ShouldContain("<rect");
        svg.ShouldBe(Code128BarcodeRenderer.Svg(payload, 200, 40), "the drawing is deterministic");

        // Read back through the library's Code 128 reader over the drawn modules, quiet zones and all: what
        // was encoded is what a scanner reads, with the check character the payload carries intact.
        var modules = Code128BarcodeRenderer.Modules(payload);
        var row = new BitArray(modules.Length + (2 * Code128BarcodeRenderer.QuietZoneModules));
        for (var index = 0; index < modules.Length; index++)
        {
            if (modules[index])
            {
                row[Code128BarcodeRenderer.QuietZoneModules + index] = true;
            }
        }

        var read = new Code128Reader().decodeRow(0, row, null);
        read.ShouldNotBeNull("the drawn modules decode as Code 128");
        read.Text.ShouldBe(payload);
        BarcodePayload.TryParse(read.Text, BarcodePayload.InvoiceNamespace, out _).ShouldBeTrue();
        svg.Split("<rect").Length.ShouldBeGreaterThan(20);

        var png = await renderer.RenderPngAsync(payload, Code128BarcodeRenderer.Code128, 300, 60, Token);
        png.IsSuccess.ShouldBeTrue(png.IsFailure ? png.Error.Message : string.Empty);
        png.Value[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        (await renderer.RenderPngAsync(payload, "qr", 300, 300, Token)).Error.Code.ShouldBe("integration.symbology-not-supported");
        (await renderer.RenderPngAsync(" ", Code128BarcodeRenderer.Code128, 300, 60, Token)).Error.Code.ShouldBe("integration.barcode-request-not-well-formed");
    }

    [Fact]
    public async Task TheInMemoryStoreKeepsWhatItWasGivenAndAnswersNothingForTheRest()
    {
        var storage = new InMemoryObjectStorage();
        var bytes = "hello"u8.ToArray();

        await storage.PutAsync("documents/one", new MemoryStream(bytes), "application/pdf", Token);

        (await storage.ExistsAsync("documents/one", Token)).ShouldBeTrue();
        (await storage.ExistsAsync("documents/two", Token)).ShouldBeFalse();
        await using var read = (await storage.OpenReadAsync("documents/one", Token)).ShouldNotBeNull();
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, Token);
        buffer.ToArray().ShouldBe(bytes);
        (await storage.OpenReadAsync("documents/two", Token)).ShouldBeNull();
        storage.ContentTypeOf("documents/one").ShouldBe("application/pdf");
        storage.Keys.ShouldContain("documents/one");
    }

    [Fact]
    public void RoutesAKeyToItsModulesBucketAndRefusesTheMinioAdapterWithoutAnEndpointOrCredentials()
    {
        var options = new ObjectStorageOptions();
        options.BucketFor("documents/abc").ShouldBe("tailor360-documents");
        options.BucketFor("exports/abc").ShouldBe("tailor360-exports");
        options.BucketFor("diagram/abc").ShouldBe("tailor360-media");
        options.IsConfigured.ShouldBeFalse();

        Should.Throw<InvalidOperationException>(() => new MinioObjectStorage(Options.Create(new ObjectStorageOptions())));
        Should.Throw<InvalidOperationException>(() => new MinioObjectStorage(Options.Create(new ObjectStorageOptions { Endpoint = "http://minio:9000" })));
    }

    [Fact]
    public async Task TheMinioAdapterRoundTripsAnObjectAgainstTheComposeStack()
    {
        var endpoint = Environment.GetEnvironmentVariable("TAILOR360_TEST_S3_ENDPOINT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), "TAILOR360_TEST_S3_ENDPOINT is not set; the MinIO adapter is exercised only against the compose stack.");

        var storage = new MinioObjectStorage(Options.Create(new ObjectStorageOptions
        {
            Endpoint = endpoint,
            AccessKey = Environment.GetEnvironmentVariable("TAILOR360_TEST_S3_ACCESS_KEY") ?? "tailor360-dev",
            SecretKey = Environment.GetEnvironmentVariable("TAILOR360_TEST_S3_SECRET_KEY") ?? "tailor360-dev-not-a-secret",
        }));
        var key = $"documents/{Guid.CreateVersion7():N}";
        var bytes = new byte[2048];
        Random.Shared.NextBytes(bytes);

        await storage.PutAsync(key, new MemoryStream(bytes), "application/pdf", Token);

        (await storage.ExistsAsync(key, Token)).ShouldBeTrue();
        await using var read = (await storage.OpenReadAsync(key, Token)).ShouldNotBeNull();
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, Token);
        buffer.ToArray().ShouldBe(bytes);
        (await storage.OpenReadAsync($"documents/{Guid.CreateVersion7():N}", Token)).ShouldBeNull();
    }

    private static async Task<byte[]> RenderAsync(QuestPdfRenderer renderer, string template, IReadOnlyDictionary<string, object?> model)
    {
        using var destination = new MemoryStream();
        var rendered = await renderer.RenderAsync(template, model, destination, Token);
        rendered.IsSuccess.ShouldBeTrue(rendered.IsFailure ? rendered.Error.Message : string.Empty);
        return destination.ToArray();
    }

    private static Dictionary<string, object?> SampleModel(string number) => new(StringComparer.Ordinal)
    {
        ["kind"] = "Invoice",
        ["number"] = number,
        ["issuedOn"] = "12-09-2026",
        ["financialYear"] = "2627",
        ["renderedAt"] = "2026-09-12T04:30:00.0000000+00:00",
        ["currency"] = "INR",
        ["barcodePayload"] = "I-7K3M9QW2XZ4B",
        ["orderNumber"] = "O-MAIN-2627-000001",
        ["placeOfSupplyStateCode"] = "33",
        ["scheme"] = "IntraState",
        ["cancelled"] = false,
        ["relatedNumber"] = "INV-MAIN-2627-000001",
        ["reason"] = "Lining charged twice.",
        ["supplier"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["legalName"] = "Example Tailors Private Limited",
            ["tradeName"] = "Example Tailors",
            ["gstin"] = "33AAACH7409R1Z8",
            ["stateCode"] = "33",
            ["branchName"] = "Main branch",
        },
        ["customer"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["customerNumber"] = "C-MAIN-000001",
            ["displayName"] = "Synthetic customer மீனா",
            // Synthetic, and Tamil on purpose: the display name exercises one word of the Tamil face,
            // an address exercises three fields of it, which is what RendersTamilTextAsTamil reads back.
            ["addressLine"] = "12 இரண்டாம் தெரு",
            ["locality"] = "பீளமேடு",
            ["postcode"] = "641004",
        },
        ["lines"] = new List<object?>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["lineNumber"] = "1",
                ["description"] = "Blouse stitching",
                ["itemCode"] = "STITCHING",
                ["classification"] = "998822",
                ["quantity"] = "1",
                ["rate"] = 450m,
                ["surcharges"] = new List<object?> { new Dictionary<string, object?>(StringComparer.Ordinal) { ["description"] = "Lining", ["amount"] = 90m } },
                ["discountRuleCode"] = null,
                ["discountAmount"] = 0m,
                ["taxableValue"] = 540m,
                ["taxes"] = new List<object?>
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "CGST", ["ratePercent"] = "2.5", ["amount"] = 13.5m },
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "SGST", ["ratePercent"] = "2.5", ["amount"] = 13.5m },
                },
                ["taxTotal"] = 27m,
                ["lineTotal"] = 567m,
            },
        },
        ["totals"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["subtotal"] = 540m,
            ["discountTotal"] = 0m,
            ["taxableValue"] = 540m,
            ["centralTax"] = 13.5m,
            ["stateTax"] = 13.5m,
            ["integratedTax"] = 0m,
            ["cess"] = 0m,
            ["roundOff"] = 0m,
            ["grandTotal"] = 567m,
            ["balanceDue"] = 567m,
        },
        ["terms"] = "Goods once tailored are not returned.",
    };

    // What this renderer cannot close, recorded here rather than quietly marked Not applicable.
    // QuestPDF 2026.8.0 writes no tagged-PDF structure: no marked content, no /StructTreeRoot, no
    // table-header role, and no language span around a Tamil name — so A11Y-DP-02, and with it the
    // per-span language that would let a screen reader change voice mid-line, cannot be satisfied by
    // this adapter at all. It also draws every glyph as a Type3 procedure rather than embedding the
    // face, which is why the assertions below read the font descriptors and why any future check of a
    // glyph's identity must too. What the renderer does do is what these tests pin: real extractable
    // text in reading order rather than a picture of a page, a document title and a document-wide /Lang
    // of en-IN, headings printed once above their rows, and every amount on its own label's text line.
    // The blueprint's wording is "tagged PDF where the renderer supports it"; it does not, and
    // E09-F02-10 is where those records are written up as Fail with an owner rather than hidden here.

    /// <summary>The seventeen accountant-agreed cases by name, so a failing run names the case it failed on.</summary>
    public static TheoryData<string> GoldenMasterCaseNames => [.. GoldenMaster.Shared.Cases.Select(@case => @case.Name)];

    [Theory]
    [MemberData(nameof(GoldenMasterCaseNames))]
    public async Task PdfFiguresMatchTheGoldenMaster(string caseName)
    {
        var master = GoldenMaster.Shared;
        var @case = master.Cases.Single(candidate => candidate.Name == caseName);
        var prefix = caseName.Split(':')[0];

        var pdf = await RenderAsync(new QuestPdfRenderer(), QuestPdfRenderer.InvoiceTemplate, master.Model(@case));
        var lines = PdfText.Lines(pdf);

        // The totals block. BillingDocumentTemplate.Line prints an emphasised figure whatever it is and
        // drops an unemphasised zero, so a component the fixture expects to be zero must be absent from
        // the page rather than printed as ₹0.00.
        foreach (var (label, expected, alwaysPrinted) in GoldenMaster.TotalsOn(@case.Expected.Totals))
        {
            var printed = PdfText.AmountBeside(lines, label);
            if (alwaysPrinted || expected != 0m)
            {
                printed.ShouldBe(expected, $"{prefix}: the page prints {GoldenMaster.Say(printed)} beside '{label}', the golden master expects {GoldenMaster.Say(expected)}");
            }
            else
            {
                printed.ShouldBeNull($"{prefix}: '{label}' is zero in the golden master, so the page must not carry that row at all — it carries {GoldenMaster.Say(printed)}");
            }
        }

        // Each line's own three columns, read off the row its line number opens.
        for (var index = 0; index < @case.Expected.Lines.Count; index++)
        {
            var expected = @case.Expected.Lines[index];
            var number = (index + 1).ToString(CultureInfo.InvariantCulture);
            var row = lines.FirstOrDefault(line => line.StartsWith(number + " ", StringComparison.Ordinal) && PdfText.AmountsOn(line).Count > 0);
            row.ShouldNotBeNull($"{prefix}: no table row opens with line number {number}");

            var taxTotal = expected.Taxes.Values.Sum();
            (expected.TaxableValue + taxTotal).ShouldBe(expected.LineTotal, $"{prefix}: line {number} of the golden master does not add up, so there is no figure for the renderer to print");
            PdfText.AmountsOn(row).ShouldBe(
                new[] { expected.TaxableValue, taxTotal, expected.LineTotal },
                $"{prefix}: line {number} prints taxable value, tax and total — the page says '{row}'");
        }

        // Every tax component printed anywhere on the page against every component the fixture expects:
        // same kinds, same rates, same amounts, the same number of them, and not one more.
        GoldenMaster.ComponentsOn(lines).ShouldBe(
            master.ExpectedComponents(@case),
            ignoreOrder: true,
            customMessage: $"{prefix}: the tax components printed on the page are not the golden master's");

        // And nothing else: every amount the page prints is an amount the golden master states.
        var stated = GoldenMaster.EveryStatedAmount(@case);
        foreach (var amount in lines.SelectMany(PdfText.AmountsOn).Select(Math.Abs).Distinct())
        {
            stated.ShouldContain(amount, $"{prefix}: the page prints {GoldenMaster.Say(amount)}, which the golden master never states");
        }
    }

    [Fact]
    public async Task RendersTamilTextAsTamil()
    {
        var model = SampleModel("INV-MAIN-2627-000001");
        var customer = (Dictionary<string, object?>)model["customer"]!;
        var given = string.Concat(customer["displayName"], customer["addressLine"], customer["locality"])
            .Where(character => character is >= '஀' and <= '௿')
            .Distinct()
            .ToArray();

        var pdf = await RenderAsync(new QuestPdfRenderer(), QuestPdfRenderer.InvoiceTemplate, model);
        var text = string.Join(' ', PdfText.Words(pdf));

        // The Tamil face itself, and not merely Tamil-looking output: with DocumentFonts.TamilFamily
        // unregistered this page still carries every Tamil code point, because Skia quietly substitutes
        // whatever Tamil face the host machine has — a substitution that exists on this developer's
        // Windows and on no build container. What cannot be faked is the embedded descriptor, so that is
        // what pins the registration.
        PdfText.FontFamilies(pdf).ShouldBe(
            [DocumentFonts.Family, DocumentFonts.TamilFamily],
            ignoreOrder: true,
            customMessage: "the document embeds the two registered Noto faces and nothing the host substituted");

        // Then by code point and not by glyph: Tamil shaping reorders vowel signs and joins consonants, so
        // the order the text layer hands back is not the order the name was written in — but every code
        // point given must come back, and none may have become U+FFFD.
        given.Length.ShouldBeGreaterThan(10, "the fixture carries a Tamil name, address line and locality");
        foreach (var character in given)
        {
            text.Contains(character).ShouldBeTrue($"U+{(int)character:X4} was given to the renderer and is not in the text layer");
        }

        text.Contains('�').ShouldBeFalse("a replacement character in the text layer means the glyph was not found");
        text.Contains("Taxable value", StringComparison.Ordinal).ShouldBeTrue("the English labels stay English on the same page");
        text.Contains("Grand total", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task RenderedDocumentIsAccessibleAsFarAsTheRendererAllows()
    {
        var pdf = await RenderAsync(new QuestPdfRenderer(), QuestPdfRenderer.InvoiceTemplate, SampleModel("INV-MAIN-2627-000001"));
        var lines = PdfText.Lines(pdf).ToList();
        var words = PdfText.Words(pdf);

        // A11Y-DP-03 and A11Y-DP-04: real text in reading order, not a picture of a page.
        words.Count.ShouldBeGreaterThan(50, "a page rendered as a single image extracts no words at all");
        lines.ShouldContain("Goods once tailored are not returned.", "a sentence comes back as the sentence it was written as");
        lines[^1].ShouldContain("Page 1 of 1", Case.Sensitive, "one page, so the heading counted below is not a repeat");

        var metadata = PdfText.Metadata(pdf);
        metadata["Title"].ShouldBe("Tax invoice INV-MAIN-2627-000001");
        metadata["Lang"].ShouldBe("en-IN", "the document declares its natural language");

        // The assertable half of A11Y-DP-05: the column headings are printed once, above their rows.
        const string Headings = "# Description Qty Rate Taxable Tax Total";
        lines.Count(line => line == Headings).ShouldBe(1, "the line table's headings are printed once");
        lines.IndexOf(Headings).ShouldBeLessThan(
            lines.FindIndex(line => line.StartsWith("1 ", StringComparison.Ordinal) && PdfText.AmountsOn(line).Count > 0),
            "the headings come before the rows they head");

        // The assertable half of A11Y-BI-03 and A11Y-BI-05: no figure is identifiable only by position.
        foreach (var line in lines.Where(line => PdfText.AmountsOn(line).Count > 0))
        {
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(word => !PdfText.IsAmount(word))
                .ShouldBeTrue($"'{line}' prints an amount with nothing on its text line to name it");
        }
    }

    /// <summary>
    /// The accountant's golden master (#147, OD-05) read as a document model. This slice asserts that the
    /// rendered page <em>agrees</em> with the fixture — never that the fixture is right — and it never
    /// writes to it: a figure comes from the file or the test does not assert it.
    /// </summary>
    private sealed class GoldenMaster
    {
        /// <summary>The count the fixture carried when this was written; a case removed must be noticed, not skipped.</summary>
        private const int CasesExpected = 17;

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private static readonly Regex Component = new(@"^(?<kind>[A-Za-z]+) (?<rate>[0-9.]+)% (?<amount>-?₹[0-9,]+\.[0-9]{2})$", RegexOptions.CultureInvariant);

        private static readonly Lazy<GoldenMaster> Fixture = new(Load);

        /// <summary>The fixture, read once.</summary>
        public static GoldenMaster Shared => Fixture.Value;

        public required IReadOnlyList<ItemFixture> Items { get; init; }

        public required IReadOnlyList<TaxCodeFixture> TaxCodes { get; init; }

        public required IReadOnlyList<CaseFixture> Cases { get; init; }

        /// <summary>The template model of one case: every figure from the fixture, everything else from the sample.</summary>
        public Dictionary<string, object?> Model(CaseFixture @case)
        {
            var model = SampleModel("INV-MAIN-2627-000001");
            model["placeOfSupplyStateCode"] = @case.PlaceOfSupplyStateCode;
            model["scheme"] = @case.Expected.Scheme;
            model["lines"] = @case.Expected.Lines.Select((expected, index) =>
            {
                var taxCode = TaxCodeOf(@case.Lines[index]);

                return (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["lineNumber"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                    ["description"] = Items.Single(item => item.Code == @case.Lines[index].ItemCode).Description,
                    ["itemCode"] = @case.Lines[index].ItemCode,
                    ["classification"] = taxCode.Classification,
                    ["quantity"] = @case.Lines[index].Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                    // No rate and no surcharge. The fixture states neither as an expected figure, and an
                    // amount on the page that no expectation covers would make the last assertion a lie.
                    ["rate"] = null,
                    ["surcharges"] = new List<object?>(),
                    ["discountRuleCode"] = @case.Lines[index].Discount?.RuleCode,
                    ["discountAmount"] = expected.Discount,
                    ["taxableValue"] = expected.TaxableValue,
                    ["taxes"] = expected.Taxes.Select(tax => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["kind"] = tax.Key,
                        ["ratePercent"] = RateOf(taxCode, tax.Key),
                        ["amount"] = tax.Value,
                    }).ToList(),
                    ["taxTotal"] = expected.Taxes.Values.Sum(),
                    ["lineTotal"] = expected.LineTotal,
                };
            }).ToList();
            model["totals"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["subtotal"] = @case.Expected.Totals.Subtotal,
                ["discountTotal"] = @case.Expected.Totals.DiscountTotal,
                ["taxableValue"] = @case.Expected.Totals.TaxableValue,
                ["centralTax"] = @case.Expected.Totals.CentralTax,
                ["stateTax"] = @case.Expected.Totals.StateTax,
                ["integratedTax"] = @case.Expected.Totals.IntegratedTax,
                ["cess"] = @case.Expected.Totals.Cess,
                ["roundOff"] = @case.Expected.Totals.RoundOff,
                ["grandTotal"] = @case.Expected.Totals.GrandTotal,
                // As DocumentModels.Invoice writes it: what is due on a document is what it comes to.
                ["balanceDue"] = @case.Expected.Totals.GrandTotal,
            };

            return model;
        }

        /// <summary>Every tax component the fixture expects across a case's lines, written as the page writes one.</summary>
        public IReadOnlyList<string> ExpectedComponents(CaseFixture @case)
            => [.. @case.Expected.Lines.SelectMany((expected, index) =>
                expected.Taxes.Select(tax => $"{tax.Key} {RateOf(TaxCodeOf(@case.Lines[index]), tax.Key)}% {Say(tax.Value)}"))];

        /// <summary>Every amount a case states, so an amount on the page outside this set was never given to the renderer.</summary>
        public static IReadOnlyCollection<decimal> EveryStatedAmount(CaseFixture @case)
            => [.. @case.Expected.Lines
                .SelectMany(line => line.Taxes.Values.Concat([line.TaxableValue, line.Discount, line.LineTotal, line.Taxes.Values.Sum()]))
                .Concat(TotalsOn(@case.Expected.Totals).Select(row => row.Amount))
                .Select(Math.Abs)];

        /// <summary>The totals rows in the order BillingDocumentTemplate prints them, and whether a zero still prints.</summary>
        public static IEnumerable<(string Label, decimal Amount, bool AlwaysPrinted)> TotalsOn(ExpectedTotals totals)
        {
            yield return ("Subtotal", totals.Subtotal, false);
            yield return ("Discount", totals.DiscountTotal, false);
            yield return ("Taxable value", totals.TaxableValue, true);
            yield return ("CGST", totals.CentralTax, false);
            yield return ("SGST", totals.StateTax, false);
            yield return ("IGST", totals.IntegratedTax, false);
            yield return ("Cess", totals.Cess, false);
            yield return ("Round-off", totals.RoundOff, false);
            yield return ("Grand total", totals.GrandTotal, true);
            yield return ("Balance due", totals.GrandTotal, true);
        }

        /// <summary>Every tax component printed on the page, as kind, rate and amount.</summary>
        public static IReadOnlyList<string> ComponentsOn(IEnumerable<string> lines)
            => [.. lines.Select(line => Component.Match(line)).Where(match => match.Success)
                .Select(match => $"{match.Groups["kind"].Value} {match.Groups["rate"].Value}% {Say(PdfText.Amount(match.Groups["amount"].Value))}")];

        /// <summary>An amount to the paisa, so two of them compare and read the same whatever their scale.</summary>
        public static string Say(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "nothing";

        private TaxCodeFixture TaxCodeOf(LineFixture line)
            => TaxCodes.Single(code => code.Code == Items.Single(item => item.Code == line.ItemCode).TaxCode);

        private static string RateOf(TaxCodeFixture taxCode, string kind)
            => taxCode.Rates.First(rate => string.Equals(rate.Key, kind, StringComparison.OrdinalIgnoreCase))
                .Value.ToString("0.##", CultureInfo.InvariantCulture);

        private static GoldenMaster Load()
        {
            var path = Path.Combine(RepositoryFiles.Root, "tests", "fixtures", "billing", "pricing-golden-master.json");
            var master = JsonSerializer.Deserialize<GoldenMaster>(File.ReadAllText(path), Options)
                ?? throw new InvalidOperationException($"The golden master at {path} did not read as one.");

            master.Cases.Count.ShouldBe(CasesExpected, $"{path} is the accountant's fixture and every case in it is asserted here; a case added or removed is a change to review, never one to skip");

            return master;
        }
    }

    private sealed record ItemFixture(string Code, string Description, string TaxCode);

    private sealed record TaxCodeFixture(string Code, string Classification, Dictionary<string, decimal> Rates);

    private sealed record CaseFixture(string Name, string PlaceOfSupplyStateCode, IReadOnlyList<LineFixture> Lines, ExpectedFixture Expected);

    private sealed record LineFixture(string ItemCode, decimal Quantity, DiscountFixture? Discount);

    private sealed record DiscountFixture(string RuleCode);

    private sealed record ExpectedFixture(string Scheme, IReadOnlyList<ExpectedLine> Lines, ExpectedTotals Totals);

    private sealed record ExpectedLine(decimal Discount, decimal TaxableValue, Dictionary<string, decimal> Taxes, decimal LineTotal);

    private sealed record ExpectedTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxableValue, decimal CentralTax, decimal StateTax, decimal IntegratedTax, decimal Cess, decimal RoundOff, decimal GrandTotal);
}
