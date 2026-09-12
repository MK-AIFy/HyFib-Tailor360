using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Contracts.Events;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Orders.Contracts.Events;
using Tailor360.Platform.Abstractions.Barcodes;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// Posting, numbering, financial immutability, cancellation and the notes (#154): a draft posts once under a
/// contiguous per-branch number with its barcode and its outbox event, is frozen at the database, is cancelled
/// by its compensating record with a full credit note, and is corrected by notes at its lines' own rates.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class InvoicePostingTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task PostsADraftOnceNumberedWithItsBarcodeAndItsEventThenFreezesIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-post", "203.0.113.150", "POST", RunToken);
        using var cashier = await CashierAsync(fixture, "inv-poster", "203.0.113.151", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);

        // Posted: the number of the branch and the financial year, the barcode, the frozen figures.
        var key = Key();
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, [.. key, ("If-Match", tag)]);
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));
        string number;
        string barcode;
        using (var body = JsonDocument.Parse(await posted.Content.ReadAsStringAsync(Token)))
        {
            var root = body.RootElement;
            root.GetProperty("status").GetString().ShouldBe("Posted");
            number = root.GetProperty("invoiceNumber").GetString()!;
            number.ShouldBe($"INV-{scene.BranchCode}-2627-000001");
            root.GetProperty("financialYear").GetString().ShouldBe("2627");
            barcode = root.GetProperty("barcodePayload").GetString()!;
            BarcodePayload.TryParse(barcode, BarcodePayload.InvoiceNamespace, out _).ShouldBeTrue(barcode);
            barcode.ShouldNotContain(number);
            root.GetProperty("postedAt").ValueKind.ShouldBe(JsonValueKind.String);
            root.GetProperty("cancelled").GetBoolean().ShouldBeFalse();
            root.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(1134m);
        }

        var postedTag = posted.Headers.ETag!.ToString();
        postedTag.ShouldNotBe(tag);

        // A replay of the same key is the original; a second post is refused, and so is every edit.
        var replayed = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, [.. key, ("If-Match", tag)]);
        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("invoiceNumber").GetString().ShouldBe(number);
        }

        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(postedTag)), HttpStatusCode.Conflict, "billing.invoice-not-editable");
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "No." }, Tagged(postedTag)), HttpStatusCode.Conflict, "billing.invoice-not-editable");
        await Refused(
            cashier.PutAsync($"/api/v1/billing/invoices/{invoiceId}", new { on = "2026-09-12", placeOfSupplyStateCode = "33", lines = Array.Empty<object>(), reason = (string?)null }, Tagged(postedTag)),
            HttpStatusCode.Conflict, "billing.invoice-not-editable");

        // Frozen at the database: no update and no delete gets through, on the invoice or on its rows.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.invoices SET grand_total_amount = 1 WHERE id = '{invoiceId}'",
                     $"UPDATE billing.invoices SET updated_at = now() WHERE id = '{invoiceId}'",
                     $"UPDATE billing.invoices SET status = 0 WHERE id = '{invoiceId}'",
                     $"DELETE FROM billing.invoices WHERE id = '{invoiceId}'",
                     $"UPDATE billing.invoice_lines SET line_total_amount = 1 WHERE invoice_id = '{invoiceId}'",
                     $"DELETE FROM billing.invoice_lines WHERE invoice_id = '{invoiceId}'",
                     $"UPDATE billing.invoice_tax_components SET amount_amount = 0 WHERE invoice_id = '{invoiceId}'",
                     $"DELETE FROM billing.invoice_line_surcharges WHERE invoice_id = '{invoiceId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        // Announced on Billing's own outbox, in the transaction that posted it.
        var outbox = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.AggregateId == invoiceId)
            .Select(message => message.EventType)
            .ToListAsync(Token);
        outbox.ShouldBe([InvoicePosted.Type]);

        // The draft it was, in the listing, now carries its number.
        using (var listed = JsonDocument.Parse(await (await cashier.GetAsync("/api/v1/billing/invoices?status=Posted&limit=50")).Content.ReadAsStringAsync(Token)))
        {
            listed.RootElement.GetProperty("invoices").EnumerateArray().Single(row => row.GetProperty("invoiceId").GetGuid() == invoiceId)
                .GetProperty("invoiceNumber").GetString().ShouldBe(number);
        }
    }

    [Fact]
    public async Task TwentyConcurrentPostsAcrossTwoBranchesAreNumberedContiguouslyPerBranch()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var first = await BuildAsync(fixture, "inv-race-a", "203.0.113.152", "RACEA", RunToken);
        var second = await BuildAsync(fixture, "inv-race-b", "203.0.113.153", "RACEB", RunToken);
        using var cashierA = await CashierAsync(fixture, "inv-racer-a", "203.0.113.154", first.Branch, BillingPermissions.PostInvoice);
        using var cashierB = await CashierAsync(fixture, "inv-racer-b", "203.0.113.155", second.Branch, BillingPermissions.PostInvoice);

        var drafts = new List<(AdministrationHarness.AdministratorClient Cashier, Guid InvoiceId, string Tag, string BranchCode)>();
        foreach (var (scene, cashier) in new[] { (first, cashierA), (second, cashierB) })
        {
            for (var index = 0; index < 10; index++)
            {
                var (orderId, jobs) = await ConfirmOrderAsync(fixture, scene.Branch, scene.CustomerId, $"O-{RunToken}-{scene.BranchCode[^3..]}{index:D2}", 1);
                var reference = $"order:{orderId:N}:1";
                await PriceAsync(fixture, scene.Branch, reference, [jobs[0].ToString()], scene.ItemCode);
                var (invoiceId, tag) = await DraftAsync(cashier, orderId, reference);
                drafts.Add((cashier, invoiceId, tag, scene.BranchCode));
            }
        }

        // All at once. One failure here is a wrong number; twenty successes with a gap or a duplicate are worse.
        var responses = await Task.WhenAll(drafts.Select(draft =>
            draft.Cashier.PostAsync($"/api/v1/billing/invoices/{draft.InvoiceId}/post", new { reason = (string?)null }, Tagged(draft.Tag))));
        var numbers = new List<(string BranchCode, string Number)>();
        foreach (var (response, draft) in responses.Zip(drafts))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            numbers.Add((draft.BranchCode, body.RootElement.GetProperty("invoiceNumber").GetString()!));
        }

        foreach (var branch in numbers.GroupBy(entry => entry.BranchCode))
        {
            var sequences = branch.Select(entry => int.Parse(entry.Number.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
            sequences.ShouldBe(Enumerable.Range(1, 10).ToArray(), $"branch {branch.Key}: contiguous, no duplicate, no gap");
            branch.Select(entry => entry.Number).ShouldAllBe(number => number.StartsWith($"INV-{branch.Key}-2627-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task CancelsAPostedInvoiceByItsRecordAndAFullCreditNoteAndCorrectsAnotherByNotes()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-cancel", "203.0.113.156", "CANCEL", RunToken);
        using var cashier = await CashierAsync(
            fixture, "inv-canceller", "203.0.113.157", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.CancelInvoice, BillingPermissions.PostCreditNote);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);

        // A draft is neither cancelled nor corrected; the reason is required on both.
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Early." }, Key()), HttpStatusCode.Conflict, "billing.invoice-not-posted");
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));
        tag = posted.Headers.ETag!.ToString();
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.reason-required");

        // A credit note per line, at the line's own rates; more than the line carries is refused.
        var creditBody = new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 90m } }, reason = "Lining charged twice." };
        var credit = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", creditBody, Key());
        credit.StatusCode.ShouldBe(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync(Token));
        Guid creditNoteId;
        using (var body = JsonDocument.Parse(await credit.Content.ReadAsStringAsync(Token)))
        {
            creditNoteId = body.RootElement.GetProperty("noteId").GetGuid();
            body.RootElement.GetProperty("kind").GetString().ShouldBe("Credit");
            body.RootElement.GetProperty("number").GetString().ShouldBe($"CN-{scene.BranchCode}-2627-000001");
            var line = body.RootElement.GetProperty("lines").EnumerateArray().Single();
            line.GetProperty("garmentJobId").GetGuid().ShouldBe(scene.Jobs[0]);
            line.GetProperty("taxes").EnumerateArray().Select(tax => tax.GetProperty("amount").GetDecimal()).ShouldBe([2.25m, 2.25m]);
            body.RootElement.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(94.5m);
        }

        await Refused(
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 450.01m } }, reason = "Too much." }, Key()),
            HttpStatusCode.BadRequest, "billing.note-exceeds-line");
        await Refused(
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", new { lines = new[] { new { garmentJobId = Guid.CreateVersion7(), taxableValue = 1m } }, reason = "Stranger." }, Key()),
            HttpStatusCode.BadRequest, "billing.note-line-not-on-invoice");
        await Refused(
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = (decimal?)null } }, reason = "Blank." }, Key()),
            HttpStatusCode.BadRequest, "billing.value-required");
        var debit = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/debit-notes", new { lines = new[] { new { garmentJobId = scene.Jobs[1], taxableValue = 50m } }, reason = "Express finishing." }, Key());
        debit.StatusCode.ShouldBe(HttpStatusCode.Created, await debit.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await debit.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("number").GetString().ShouldBe($"DN-{scene.BranchCode}-2627-000001");
            body.RootElement.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(52.5m);
        }

        // Cancelled: the record appended, a credit note for the whole amount, the number and totals untouched.
        var cancelled = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Issued to the wrong customer." }, Key());
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Token));
        Guid cancellationNoteId;
        using (var body = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync(Token)))
        {
            var root = body.RootElement;
            root.GetProperty("status").GetString().ShouldBe("Posted");
            root.GetProperty("cancelled").GetBoolean().ShouldBeTrue();
            root.GetProperty("invoiceNumber").GetString().ShouldBe($"INV-{scene.BranchCode}-2627-000001");
            root.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(1134m);
            root.GetProperty("lines").GetArrayLength().ShouldBe(2);
            root.GetProperty("cancellation").GetProperty("reason").GetString().ShouldBe("Issued to the wrong customer.");
            cancellationNoteId = root.GetProperty("cancellation").GetProperty("creditNoteId").GetGuid();
            var notes = root.GetProperty("notes").EnumerateArray().ToArray();
            notes.Length.ShouldBe(3);
            var full = notes.Single(note => note.GetProperty("noteId").GetGuid() == cancellationNoteId);
            full.GetProperty("number").GetString().ShouldBe($"CN-{scene.BranchCode}-2627-000002");
            full.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(1134m);
            full.GetProperty("totals").GetProperty("roundOff").GetDecimal().ShouldBe(root.GetProperty("totals").GetProperty("roundOff").GetDecimal());
            full.GetProperty("lines").GetArrayLength().ShouldBe(2);
        }

        // Once: a second cancellation and a further note are refused.
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Again." }, Key()), HttpStatusCode.Conflict, "billing.invoice-already-cancelled");
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", creditBody, Key()), HttpStatusCode.Conflict, "billing.invoice-already-cancelled");

        // The records are append-only at the database, and every event went onto the outbox.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        foreach (var sql in new[]
                 {
                     $"UPDATE billing.invoice_cancellations SET reason = 'x' WHERE invoice_id = '{invoiceId}'",
                     $"DELETE FROM billing.invoice_cancellations WHERE invoice_id = '{invoiceId}'",
                     $"UPDATE billing.adjustment_notes SET reason = 'x' WHERE id = '{creditNoteId}'",
                     $"DELETE FROM billing.adjustment_note_lines WHERE note_id = '{creditNoteId}'",
                     $"UPDATE billing.adjustment_note_taxes SET amount_amount = 0 WHERE note_id = '{creditNoteId}'",
                 })
        {
            (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, sql);
        }

        var events = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.AggregateId == invoiceId || message.AggregateId == creditNoteId || message.AggregateId == cancellationNoteId)
            .Select(message => message.EventType)
            .ToListAsync(Token);
        // The cancellation moves the paid status too (Unpaid to Cancelled), and says so on the outbox.
        events.ShouldBe([InvoicePosted.Type, CreditNotePosted.Type, InvoiceCancelled.Type, CreditNotePosted.Type, InvoicePaidStatusChanged.Type], ignoreOrder: true);

        // Audited under the actions the matrix names, and never with the customer on the row.
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents.AsNoTracking()
            .Where(entry => entry.EntityId == invoiceId).OrderBy(entry => entry.Sequence).Select(entry => entry.Action).ToListAsync(Token);
        trail.ShouldBe(["billing.invoice.drafted", "billing.invoice.posted", "billing.credit_note.posted", "billing.debit_note.posted", "billing.invoice.cancelled"]);
    }

    [Fact]
    public async Task TwoPostsOfOneDraftEndWithOneNumberAndOneRefusalAndNoGap()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-same", "203.0.113.164", "SAME", RunToken);
        using var first = await CashierAsync(fixture, "inv-same-a", "203.0.113.165", scene.Branch, BillingPermissions.PostInvoice);
        using var second = await CashierAsync(fixture, "inv-same-b", "203.0.113.166", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(first, scene.OrderId, scene.Reference);

        // Both hold the same tag and post at once: the row lock serialises them, the loser re-reads a posted
        // invoice and is refused, and the number the loser might have drawn is not burned.
        var responses = await Task.WhenAll(
            first.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag)),
            second.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag)));
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.PreconditionFailed).ShouldBe(1);

        var (nextOrder, nextJobs) = await ConfirmOrderAsync(fixture, scene.Branch, scene.CustomerId, $"O-{RunToken}-SAME2", 1);
        var nextReference = $"order:{nextOrder:N}:1";
        await PriceAsync(fixture, scene.Branch, nextReference, [nextJobs[0].ToString()], scene.ItemCode);
        var (nextInvoice, nextTag) = await DraftAsync(first, nextOrder, nextReference);
        var next = await first.PostAsync($"/api/v1/billing/invoices/{nextInvoice}/post", new { reason = (string?)null }, Tagged(nextTag));
        next.StatusCode.ShouldBe(HttpStatusCode.OK, await next.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await next.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("invoiceNumber").GetString().ShouldBe($"INV-{scene.BranchCode}-2627-000002", "the refused post returned its number");
    }

    [Fact]
    public async Task TwoCreditNotesForOneLineAtOnceRelieveItOnceAndACancelledInvoiceFreesItsJobs()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-twice", "203.0.113.167", "TWICE", RunToken);
        using var cashier = await CashierAsync(
            fixture, "inv-twicer", "203.0.113.168", scene.Branch, BillingPermissions.PostInvoice, BillingPermissions.CancelInvoice, BillingPermissions.PostCreditNote);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The whole line, twice, at once: the second waits on the row lock, re-reads, and is refused.
        var body = new { lines = new[] { new { garmentJobId = scene.Jobs[0], taxableValue = 540m } }, reason = "Whole line." };
        var responses = await Task.WhenAll(
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", body, Key()),
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", body, Key()));
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest).ShouldBe(1);
        (await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync(Token)))).Count(text => text.Contains("billing.note-exceeds-line", StringComparison.Ordinal)).ShouldBe(1);

        // Cancelled twice at once: one record, one refusal.
        var cancels = await Task.WhenAll(
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Wrong customer." }, Key()),
            cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "Wrong customer." }, Key()));
        cancels.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        cancels.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        // The cancelled invoice's garments are free: a new draft for the same order is accepted, and the
        // listing says which invoice is the cancelled one.
        var again = await cashier.PostAsync("/api/v1/billing/invoices", new { orderId = scene.OrderId, calculationReference = scene.Reference, garmentJobIds = Array.Empty<Guid>(), reason = (string?)null }, Key());
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync(Token));
        using (var listed = JsonDocument.Parse(await (await cashier.GetAsync("/api/v1/billing/invoices?limit=50")).Content.ReadAsStringAsync(Token)))
        {
            var rows = listed.RootElement.GetProperty("invoices").EnumerateArray().ToDictionary(row => row.GetProperty("invoiceId").GetGuid(), row => row.GetProperty("cancelled").GetBoolean());
            rows[invoiceId].ShouldBeTrue();
            rows[CreatedId(again)].ShouldBeFalse();
        }
    }

    [Fact]
    public async Task RefusesACancellationBeyondTheConfiguredWindow()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-window", "203.0.113.169", "WINDOW", RunToken);
        using var cashier = await CashierAsync(fixture, "inv-windower", "203.0.113.170", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The handler as the host builds it, with a window of nothing: every cancellation is too late.
        using var scope = fixture.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var handler = new Modules.Billing.Application.Invoicing.InvoiceHandler(
            provider.GetRequiredService<Modules.Billing.Application.Abstractions.IInvoiceStore>(),
            provider.GetRequiredService<Modules.Billing.Application.Abstractions.IPaymentStore>(),
            provider.GetRequiredService<Modules.Billing.Application.Abstractions.IOrderFactStore>(),
            provider.GetRequiredService<Modules.Billing.Application.Abstractions.IGstRegistrationStore>(),
            provider.GetRequiredService<Modules.Billing.Contracts.Pricing.IPricingService>(),
            provider.GetRequiredService<Modules.Customers.Contracts.Customers.ICustomerSnapshotQuery>(),
            provider.GetRequiredService<Modules.Identity.Contracts.Directory.IBranchDirectory>(),
            provider.GetRequiredService<Modules.Billing.Application.Abstractions.IBillingEventPublisher>(),
            Microsoft.Extensions.Options.Options.Create(new Modules.Billing.Application.Invoicing.InvoiceOptions { CancellationWindow = TimeSpan.Zero }),
            provider.GetRequiredService<Tailor360.Platform.Abstractions.Auditing.IAuditWriter>(),
            provider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>(),
            provider.GetRequiredService<Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>());

        var refused = await handler.CancelAsync(
            new Modules.Billing.Application.Invoicing.CancelInvoiceCommand(invoiceId, SessionTestData.OrganisationId, "Too late.", null), Token);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("billing.cancellation-window-closed");
    }

    [Fact]
    public async Task RefusesADriftedDraftAndEveryRouteToAHolderWithoutItsPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-deny", "203.0.113.158", "DENY", RunToken);
        using var drafter = await CashierAsync(fixture, "inv-drafter", "203.0.113.159", scene.Branch);
        var (invoiceId, tag) = await DraftAsync(drafter, scene.OrderId, scene.Reference);

        // Deny by default: the draft permission posts, cancels and corrects nothing.
        (await drafter.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await drafter.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = "No." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await drafter.PostAsync($"/api/v1/billing/invoices/{invoiceId}/credit-notes", new { lines = Array.Empty<object>(), reason = "No." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await drafter.PostAsync($"/api/v1/billing/invoices/{invoiceId}/debit-notes", new { lines = Array.Empty<object>(), reason = "No." }, Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Another branch's poster reaches nothing.
        using var elsewhere = await CashierAsync(fixture, "inv-elsewhere", "203.0.113.160", await BillingHarness.OpenBranchAsync(scene.Owner), BillingPermissions.PostInvoice);
        (await elsewhere.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A stale tag, then a draft whose stored calculation was tampered with: neither posts.
        using var poster = await CashierAsync(fixture, "inv-poster2", "203.0.113.161", scene.Branch, BillingPermissions.PostInvoice);
        await Refused(poster.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged("\"stale\"")), HttpStatusCode.PreconditionFailed, "billing.invoice-changed");
        (await poster.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Key())).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            // The snapshot table is append-only, so the drift is planted on the draft's own row instead: a
            // draft is still editable, and a figure changed under it must not post.
            (await context.Database.ExecuteSqlAsync($"UPDATE billing.invoices SET grand_total_amount = grand_total_amount + 1 WHERE id = {invoiceId}", Token)).ShouldBe(1);
        }

        var read = await poster.GetAsync($"/api/v1/billing/invoices/{invoiceId}");
        await Refused(poster.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(read.Headers.ETag!.ToString())), HttpStatusCode.Conflict, "billing.totals-mismatch");
    }

    [Fact]
    public async Task RefusesToPostADraftTheOrderHasMovedOnFromUntilItIsRePriced()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "inv-moved", "203.0.113.162", "MOVED", RunToken);
        using var cashier = await CashierAsync(fixture, "inv-mover", "203.0.113.163", scene.Branch, BillingPermissions.PostInvoice);
        var (invoiceId, tag) = await DraftAsync(cashier, scene.OrderId, scene.Reference);

        // The order is revised after the draft was made: the draft is not posted as it stands.
        await PublishAsync(fixture, publisher => publisher.Publish(new OrderRevised(
            Guid.CreateVersion7(), Now(fixture), scene.OrderId, SessionTestData.OrganisationId, scene.Branch, scene.CustomerId, scene.OrderNumber,
            Guid.CreateVersion7(), 2, new DateOnly(2026, 9, 25), null, [scene.Jobs[0]])));
        await DispatchAsync(fixture);
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag)), HttpStatusCode.Conflict, "billing.order-revised-since-draft");

        // Re-priced against the order as it now stands, the draft carries the revision and posts.
        var repriced = await cashier.PutAsync(
            $"/api/v1/billing/invoices/{invoiceId}",
            new
            {
                on = "2026-09-12",
                placeOfSupplyStateCode = "33",
                lines = scene.Jobs.Select(job => new { lineKey = job.ToString(), itemCode = scene.ItemCode, quantity = 1m, surchargeItemCodes = new[] { scene.SurchargeCode }, discount = (object?)null, @override = (object?)null }).ToArray(),
                reason = (string?)null,
            },
            Tagged(tag));
        repriced.StatusCode.ShouldBe(HttpStatusCode.OK, await repriced.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await repriced.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("orderRevisionNumber").GetInt32().ShouldBe(2);
        }

        tag = repriced.Headers.ETag!.ToString();

        // A garment cancelled at the counter after the draft was made: the draft still charging for it is not posted.
        await PublishAsync(fixture, publisher => publisher.Publish(new GarmentJobCancelled(
            Guid.CreateVersion7(), Now(fixture), scene.Jobs[1], SessionTestData.OrganisationId, scene.Branch, scene.OrderId, $"{scene.OrderNumber}-02", "WRONG_SIZE")));
        await DispatchAsync(fixture);
        await Refused(cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(tag)), HttpStatusCode.BadRequest, "billing.job-cancelled");

        // Re-priced to the surviving garment, it posts.
        var trimmed = await cashier.PutAsync(
            $"/api/v1/billing/invoices/{invoiceId}",
            new
            {
                on = "2026-09-12",
                placeOfSupplyStateCode = "33",
                lines = new[] { new { lineKey = scene.Jobs[0].ToString(), itemCode = scene.ItemCode, quantity = 1m, surchargeItemCodes = new[] { scene.SurchargeCode }, discount = (object?)null, @override = (object?)null } },
                reason = (string?)null,
            },
            Tagged(tag));
        trimmed.StatusCode.ShouldBe(HttpStatusCode.OK, await trimmed.Content.ReadAsStringAsync(Token));
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, Tagged(trimmed.Headers.ETag!.ToString()));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await posted.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("lines").GetArrayLength().ShouldBe(1);
            body.RootElement.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(567m);
        }
    }
}
