using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Integration.Infrastructure.Storage;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The rendered documents end to end (#155): posting requests the artefact through the outbox, the worker's
/// pass renders and stores it with its checksum, the download streams exactly those bytes and audits the
/// access, the barcode lookup confirms nothing it does not show, the print route queues and audits, and a
/// cancelled invoice's original document still matches its checksum.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class DocumentArtifactTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task RendersStoresStreamsPrintsAndKeepsTheChecksumThroughACancellation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "doc-render", "203.0.113.180", "DOC", RunToken);
        using var cashier = await CashierAsync(
            fixture, "doc-cashier", "203.0.113.181", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.CancelInvoice, BillingPermissions.PostCreditNote);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);

        // Nothing to stream or print before posting.
        (await cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));
        string number;
        string barcode;
        using (var body = JsonDocument.Parse(await posted.Content.ReadAsStringAsync(Token)))
        {
            number = body.RootElement.GetProperty("invoiceNumber").GetString()!;
            barcode = body.RootElement.GetProperty("barcodePayload").GetString()!;
        }

        // Posted but not yet rendered: the artefact is requested by the outbox consumer, and the route says so.
        await DispatchAsync(fixture);
        await Refused(cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document"), HttpStatusCode.NotFound, "billing.document-not-available");
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/print", new { copies = 1 }, Key()), HttpStatusCode.NotFound, "billing.document-not-available");

        // The worker's pass, as the hosted job runs it.
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1);

        DocumentArtifact artifact;
        using (var scope = fixture.Services.CreateScope())
        {
            artifact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().DocumentArtifacts.AsNoTracking()
                .SingleAsync(candidate => candidate.Kind == DocumentKind.Invoice && candidate.DocumentId == invoiceId, Token);
        }

        artifact.Status.ShouldBe(DocumentArtifactStatus.Completed);
        artifact.DocumentNumber.ShouldBe(number);
        artifact.ObjectKey.ShouldStartWith("documents/");
        artifact.ObjectKey.ShouldNotContain(number);
        artifact.Attempts.ShouldBe(1);

        // What the store holds is what the row says: the size and the SHA-256.
        var storage = fixture.Services.GetRequiredService<InMemoryObjectStorage>();
        await using (var stored = (await storage.OpenReadAsync(artifact.ObjectKey!, Token)).ShouldNotBeNull())
        {
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, Token);
            buffer.Length.ShouldBe(artifact.SizeBytes!.Value);
            Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())).ShouldBe(artifact.Sha256);
            buffer.ToArray()[..5].ShouldBe("%PDF-"u8.ToArray());
        }

        // Streamed: the same bytes, as a PDF named by the number, never cached, and the access audited.
        var download = await cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document");
        download.StatusCode.ShouldBe(HttpStatusCode.OK, await download.Content.ReadAsStringAsync(Token));
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"{number}.pdf");
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var downloaded = await download.Content.ReadAsByteArrayAsync(Token);
        Convert.ToHexStringLower(SHA256.HashData(downloaded)).ShouldBe(artifact.Sha256);

        // Printed: acknowledged with a job and audited.
        var printed = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/print", new { copies = 2 }, Key());
        printed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await printed.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await printed.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("printJobId").GetGuid().ShouldNotBe(Guid.Empty);
        }

        // A credit note is rendered too, and streamed through its invoice.
        var credit = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 90m } }, reason = "Lining charged twice." }, Key());
        credit.StatusCode.ShouldBe(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync(Token));
        Guid noteId;
        string noteNumber;
        using (var body = JsonDocument.Parse(await credit.Content.ReadAsStringAsync(Token)))
        {
            noteId = body.RootElement.GetProperty("noteId").GetGuid();
            noteNumber = body.RootElement.GetProperty("number").GetString()!;
        }

        await DispatchAsync(fixture);
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1);
        var noteDownload = await cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}/notes/{noteId}/document");
        noteDownload.StatusCode.ShouldBe(HttpStatusCode.OK, await noteDownload.Content.ReadAsStringAsync(Token));
        noteDownload.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"{noteNumber}.pdf");
        (await cashier.GetAsync($"/api/v1/billing/invoices/{Guid.CreateVersion7()}/notes/{noteId}/document")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Cancelled: the original document is still the original, byte for byte (INV-INV-06).
        var cancelled = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Issued to the wrong customer." }, Key());
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Token));
        await DispatchAsync(fixture);
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1, "the cancellation's credit note is rendered");
        var again = await cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document");
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        Convert.ToHexStringLower(SHA256.HashData(await again.Content.ReadAsByteArrayAsync(Token))).ShouldBe(artifact.Sha256);

        // The trail: the download twice and the print, against the invoice, by action.
        using (var scope = fixture.Services.CreateScope())
        {
            var actions = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
                .Where(entry => entry.EntityId == invoiceId).Select(entry => entry.Action).ToListAsync(Token);
            actions.Count(action => action == DocumentArtifactHandler.DownloadedAction).ShouldBe(3, "the invoice twice and the note once");
            actions.Count(action => action == DocumentArtifactHandler.PrintedAction).ShouldBe(1);
        }

        // The barcode resolves for the branch that issued it, and for nobody else.
        var resolved = await cashier.GetAsync($"/api/v1/billing/barcodes/{barcode}");
        resolved.StatusCode.ShouldBe(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await resolved.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("invoiceId").GetGuid().ShouldBe(invoiceId);
            body.RootElement.GetProperty("invoiceNumber").GetString().ShouldBe(number);
            body.RootElement.GetProperty("cancelled").GetBoolean().ShouldBeTrue();
        }

        (await cashier.GetAsync($"/api/v1/billing/barcodes/{barcode.ToLowerInvariant()}")).StatusCode.ShouldBe(HttpStatusCode.OK, "a scanner's lower case reads");
        (await cashier.GetAsync($"/api/v1/billing/barcodes/{BarcodePayload.Mint(BarcodePayload.InvoiceNamespace).Value}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cashier.GetAsync($"/api/v1/billing/barcodes/{barcode[..^1]}{(barcode[^1] == 'X' ? 'Y' : 'X')}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "a check character that does not hold");
        (await cashier.GetAsync($"/api/v1/billing/barcodes/{number}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "a display number is not a payload");

        using var elsewhere = await CashierAsync(fixture, "doc-elsewhere", "203.0.113.182", await BillingHarness.OpenBranchAsync(scene.Owner));
        (await elsewhere.GetAsync($"/api/v1/billing/barcodes/{barcode}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another branch's invoice reads as nothing");
        (await elsewhere.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.PostAsync($"/api/v1/billing/invoices/{invoiceId}/print", new { copies = 1 }, Key())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var reader = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "doc-deny", "203.0.113.183", scene.Branch, CatalogPermissions.Read);
        (await reader.GetAsync($"/api/v1/billing/invoices/{invoiceId}/document")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/billing/barcodes/{barcode}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AFailedRenderingIsRetriedOnTheNextPassAndCompletesWithinTheBoundedAttempts()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "doc-drift", "203.0.113.184", "DRIFT", RunToken);
        using var cashier = await CashierAsync(fixture, "doc-drifter", "203.0.113.185", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);

        // Four failures recorded on the row, as four passes against a store that was away would leave it.
        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var artifact = await context.DocumentArtifacts.SingleAsync(candidate => candidate.DocumentId == invoiceId, Token);
            for (var attempt = 0; attempt < DocumentArtifact.MaximumAttempts - 1; attempt++)
            {
                artifact.RecordFailure("billing.document-store-unavailable", DateTimeOffset.UtcNow).ShouldBeFalse();
            }

            await context.SaveChangesAsync(Token);
        }

        // One attempt left, and it succeeds: a retried rendering completes once the store is back.
        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1);
        using (var scope = fixture.Services.CreateScope())
        {
            var artifact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().DocumentArtifacts.AsNoTracking()
                .SingleAsync(candidate => candidate.DocumentId == invoiceId, Token);
            artifact.Status.ShouldBe(DocumentArtifactStatus.Completed);
            artifact.Attempts.ShouldBe(DocumentArtifact.MaximumAttempts);
        }
    }

    [Fact]
    public async Task ARendererThatThrowsCountsAsOneAttemptAndTheNextPassCompletesTheDocument()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "doc-throw", "203.0.113.186", "THROW", RunToken);
        using var cashier = await CashierAsync(fixture, "doc-thrower", "203.0.113.187", scene.Branch, BillingPermissions.PostInvoice);

        // The queue is drained first, so the throwing pass below meets this test's artefact and not a
        // batch of the other classes' — a failure counted on theirs would be a failure they did not cause.
        await RenderPendingAsync();
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DispatchAsync(fixture);

        // A renderer that throws rather than refusing: the pass survives it, the row counts it, and the
        // exception's message never reaches the row — the code does.
        using (var scope = fixture.Services.CreateScope())
        {
            var throwing = ActivatorUtilities.CreateInstance<DocumentArtifactHandler>(scope.ServiceProvider, new ThrowingRenderer());
            await throwing.RenderPendingAsync(cancellationToken: Token);
        }

        var attempted = await ArtifactAsync(invoiceId);
        attempted.Status.ShouldBe(DocumentArtifactStatus.Pending);
        attempted.Attempts.ShouldBe(1);
        attempted.LastError.ShouldBe("billing.document-render-failed");
        attempted.LastError!.ShouldNotContain(ThrowingRenderer.Message);

        (await RenderPendingAsync()).ShouldBeGreaterThanOrEqualTo(1);
        var completed = await ArtifactAsync(invoiceId);
        completed.Status.ShouldBe(DocumentArtifactStatus.Completed);
        completed.Attempts.ShouldBe(2);
        completed.LastError.ShouldBeNull();
    }

    private async Task<DocumentArtifact> ArtifactAsync(Guid documentId)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().DocumentArtifacts.AsNoTracking()
            .SingleAsync(candidate => candidate.DocumentId == documentId, Token);
    }

    /// <summary>
    /// Renders until a pass completes nothing: the queue holds every document the other classes posted and
    /// never rendered, more than one batch of them, and a test's own artefact is wherever the requested
    /// order put it. Answers how many completed in all.
    /// </summary>
    private async Task<int> RenderPendingAsync()
    {
        var completed = 0;
        for (var pass = 0; pass < 40; pass++)
        {
            using var scope = fixture.Services.CreateScope();
            var rendered = await scope.ServiceProvider.GetRequiredService<DocumentArtifactHandler>().RenderPendingAsync(cancellationToken: Token);
            completed += rendered;
            if (rendered == 0)
            {
                break;
            }
        }

        return completed;
    }

    private sealed class ThrowingRenderer : IPdfRenderer
    {
        public const string Message = "The renderer tripped on a row it did not expect.";

        public Task<Result> RenderAsync(string templateKey, IReadOnlyDictionary<string, object?> model, Stream destination, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(Message);
    }
}
