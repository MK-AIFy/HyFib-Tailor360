using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The receipt over HTTP (#169): issued with the payment under a contiguous per-branch number with its
/// R- barcode, twenty at once with no gap and no duplicate and a refused payment burning none; rendered on
/// the roll by the worker, streamed with the stored checksum and audited, printed and audited; resolved from
/// its barcode for the issuing branch alone; immutable at the database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class ReceiptEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task IssuesRendersStreamsPrintsAndResolvesTheReceipt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "rcpt-flow", "203.0.113.211", "RCPT", RunToken);
        using var cashier = await CashierAsync(fixture, "rcpt-cashier", "203.0.113.212", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.PrintReceipt);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The payment answers with its receipt: numbered at the branch, with an R- payload, the figures frozen.
        var reference = $"UPI-{RunToken}-RC01";
        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 1500m, reference }, Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var paymentId = CreatedId(recorded);
        var receipt = JsonDocument.Parse(await recorded.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("receipt");
        var receiptId = receipt.GetProperty("id").GetGuid();
        var number = receipt.GetProperty("receiptNumber").GetString()!;
        number.ShouldStartWith($"RCPT-{scene.BranchCode}-2627-");
        var barcode = receipt.GetProperty("barcodePayload").GetString()!;
        BarcodePayload.TryParse(barcode, BarcodePayload.ReceiptNamespace, out _).ShouldBeTrue(barcode);
        barcode.ShouldNotContain(number);
        receipt.GetProperty("amount").GetDecimal().ShouldBe(1500m);
        receipt.GetProperty("allocated").GetDecimal().ShouldBe(1134m);
        receipt.GetProperty("unappliedAdvance").GetDecimal().ShouldBe(366m);
        receipt.GetProperty("orderOutstanding").GetDecimal().ShouldBe(0m);
        receipt.GetProperty("paymentId").GetGuid().ShouldBe(paymentId);

        // Read back on the payment; not yet rendered.
        var read = await cashier.GetAsync($"/api/v1/billing/payments/{paymentId}");
        JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("receipt").GetProperty("receiptNumber").GetString().ShouldBe(number);
        await Refused(cashier.GetAsync($"/api/v1/billing/receipts/{receiptId}/document"), HttpStatusCode.NotFound, "billing.document-not-available");
        await Refused(cashier.PostAsync($"/api/v1/billing/receipts/{receiptId}/print", new { copies = 1 }, Key()), HttpStatusCode.NotFound, "billing.document-not-available");

        // The recording event asks for the rendering; the worker's pass renders it on the roll.
        await DispatchAsync(fixture);
        await RenderPendingAsync();
        DocumentArtifact artifact;
        using (var scope = fixture.Services.CreateScope())
        {
            artifact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().DocumentArtifacts.AsNoTracking()
                .SingleAsync(candidate => candidate.Kind == DocumentKind.Receipt && candidate.DocumentId == receiptId, Token);
        }

        artifact.Status.ShouldBe(DocumentArtifactStatus.Completed);
        artifact.DocumentNumber.ShouldBe(number);
        artifact.ObjectKey!.ShouldStartWith("documents/");
        artifact.ObjectKey.ShouldNotContain(number);

        // Streamed: the bytes match the stored checksum; the access is audited against the receipt.
        var streamed = await cashier.GetAsync($"/api/v1/billing/receipts/{receiptId}/document");
        streamed.StatusCode.ShouldBe(HttpStatusCode.OK, await streamed.Content.ReadAsStringAsync(Token));
        streamed.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await streamed.Content.ReadAsByteArrayAsync(Token);
        bytes.Length.ShouldBe((int)artifact.SizeBytes!.Value);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(artifact.Sha256);
        bytes[..5].ShouldBe("%PDF-"u8.ToArray());
        PdfPageWidthPoints(bytes).ShouldBe(226.77m, 0.1m, "the roll is 80 mm wide");

        // Printed: one to five copies, acknowledged with the job, audited.
        var printed = await cashier.PostAsync($"/api/v1/billing/receipts/{receiptId}/print", new { copies = 2 }, Key());
        printed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await printed.Content.ReadAsStringAsync(Token));
        await Refused(cashier.PostAsync($"/api/v1/billing/receipts/{receiptId}/print", new { copies = 6 }, Key()), HttpStatusCode.BadRequest, "billing.copies-out-of-range");

        // Resolved from the barcode for this branch; the invoice lookup does not answer an R- payload.
        var resolved = await cashier.GetAsync($"/api/v1/billing/receipts/barcode/{barcode}");
        resolved.StatusCode.ShouldBe(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync(Token));
        JsonDocument.Parse(await resolved.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("id").GetGuid().ShouldBe(receiptId);
        (await cashier.GetAsync($"/api/v1/billing/receipts/barcode/{barcode.ToLowerInvariant()}")).StatusCode.ShouldBe(HttpStatusCode.OK, "lower case is accepted");
        (await cashier.GetAsync($"/api/v1/billing/receipts/barcode/{barcode[..^1]}{(barcode[^1] == 'X' ? 'Y' : 'X')}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "a check character that does not hold");
        (await cashier.GetAsync($"/api/v1/billing/receipts/barcode/{BarcodePayload.Mint(BarcodePayload.ReceiptNamespace).Value}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "a payload nobody issued");
        (await cashier.GetAsync($"/api/v1/billing/receipts/barcode/I{barcode[1..]}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "an I- payload is not a receipt's");
        (await cashier.GetAsync($"/api/v1/billing/barcodes/{barcode}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "the invoice lookup answers I- alone");

        using (var scope = fixture.Services.CreateScope())
        {
            // Immutable at the database.
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            foreach (var sql in new[]
                     {
                         $"UPDATE billing.receipts SET amount_amount = 1 WHERE id = '{receiptId}'",
                         $"UPDATE billing.receipts SET receipt_number = 'RCPT-X' WHERE id = '{receiptId}'",
                         $"DELETE FROM billing.receipts WHERE id = '{receiptId}'",
                     })
            {
                (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
            }

            // The trail: the download and the print against the receipt, with no reference in either summary.
            var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
                .Where(entry => entry.EntityId == receiptId).OrderBy(entry => entry.Sequence).ToListAsync(Token);
            trail.Select(entry => entry.Action).ShouldBe(["billing.receipt.downloaded", "billing.receipt.printed"]);
            trail.ShouldAllBe(entry => !entry.Summary.Contains(reference));
        }
    }

    [Fact]
    public async Task TwentyPaymentsAtOnceAreReceiptedContiguouslyAndARefusedPaymentBurnsNoNumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "rcpt-race", "203.0.113.213", "RCRC", RunToken);
        using var cashier = await CashierAsync(fixture, "rcpt-racer", "203.0.113.214", scene.Branch, BillingPermissions.RecordPayment, BillingPermissions.Session);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }, Key())));
        var numbers = new List<string>();
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
            numbers.Add(JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("receipt").GetProperty("receiptNumber").GetString()!);
        }

        // Contiguous within the branch and the year: twenty distinct numbers with no gap between the lowest and the highest.
        numbers.Distinct().Count().ShouldBe(20);
        var sequences = numbers.Select(number => int.Parse(number.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();
        sequences.ShouldBe(Enumerable.Range(sequences[0], 20).ToList());

        // A payment refused inside its own transaction — the same UPI reference twice — burns no number: the next
        // receipt follows straight on.
        var reference = $"UPI-{RunToken}-RC02";
        (await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 10m, reference }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);
        await Refused(cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "UPI", amount = 10m, reference }, Key()), HttpStatusCode.Conflict, "billing.payment-reference-duplicated");
        var after = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 10m, reference = (string?)null }, Key());
        after.StatusCode.ShouldBe(HttpStatusCode.Created);
        var last = int.Parse(JsonDocument.Parse(await after.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("receipt").GetProperty("receiptNumber").GetString()!.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture);
        last.ShouldBe(sequences[^1] + 2, "one for the UPI payment that was recorded, none for the one refused");
    }

    [Fact]
    public async Task AnotherBranchAndAnotherPermissionAreRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "rcpt-deny", "203.0.113.215", "RCDN", RunToken);
        using var cashier = await CashierAsync(fixture, "rcpt-denied", "203.0.113.216", scene.Branch, BillingPermissions.RecordPayment, BillingPermissions.Session, BillingPermissions.PrintReceipt);
        using var clerk = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rcpt-clerk", "203.0.113.217", scene.Branch, BillingPermissions.RecordPayment);
        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "rcpt-stranger", "203.0.113.218", elsewhere, BillingPermissions.PrintReceipt);
        (await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 0m }, Key())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var recorded = await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 50m, reference = (string?)null }, Key());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Token));
        var receipt = JsonDocument.Parse(await recorded.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("receipt");
        var receiptId = receipt.GetProperty("id").GetGuid();
        var barcode = receipt.GetProperty("barcodePayload").GetString()!;
        await DispatchAsync(fixture);
        await RenderPendingAsync();

        (await clerk.GetAsync($"/api/v1/billing/receipts/{receiptId}/document")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.PostAsync($"/api/v1/billing/receipts/{receiptId}/print", new { copies = 1 }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.GetAsync($"/api/v1/billing/receipts/barcode/{barcode}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/billing/receipts/{receiptId}/document")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/api/v1/billing/receipts/{receiptId}/print", new { copies = 1 }, Key())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/billing/receipts/barcode/{barcode}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cashier.GetAsync($"/api/v1/billing/receipts/{Guid.CreateVersion7()}/document")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cashier.GetAsync($"/api/v1/billing/receipts/{receiptId}/document")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The first page's MediaBox width, read from the PDF's page dictionary as Skia writes it, uncompressed.</summary>
    private static decimal PdfPageWidthPoints(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+[0-9.]+\s*\]");
        match.Success.ShouldBeTrue("the page dictionary carries a MediaBox");
        return decimal.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task RenderPendingAsync()
    {
        for (var pass = 0; pass < 40; pass++)
        {
            using var scope = fixture.Services.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<DocumentArtifactHandler>().RenderPendingAsync(cancellationToken: Token) == 0)
            {
                return;
            }
        }
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }
}
