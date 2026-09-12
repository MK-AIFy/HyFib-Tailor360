using System.Security.Cryptography;
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
        var refused = await renderer.RenderAsync("billing.receipt", model, destination, Token);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("integration.template-not-known");
    }

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
            ["addressLine"] = "12 Second Street",
            ["locality"] = "Peelamedu",
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
}
