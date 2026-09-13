using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// What Billing contributes to <c>GET /api/v1/customers/{customerId}/timeline</c> (#309): every posted
/// invoice, its cancellation and its credit and debit notes.
/// </summary>
/// <remarks>
/// Run against a real PostgreSQL and the composed host, in the shape
/// <c>CustomerTimelineEndpointTests</c> already established: do things to an invoice through the API and
/// then ask the timeline what it saw, rather than stub the source — the merge itself is already the
/// contract tier's own test. This slice adds no route, so every case here reads the existing customer
/// timeline endpoint; only the cross-organisation case reaches <see cref="BillingTimelineSource"/>
/// directly, because the test host authenticates every caller into one fixed organisation.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class BillingTimelineSourceTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(8).ToUpperInvariant();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowsThePostedInvoiceItsCancellationAndItsNotesWithTheirActorsAndReferences()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-inv", "203.0.113.180", "TL1", RunToken);
        var (invoiceId, cancelReason) = await PostDebitAndCancelAsync(scene, "tl-actor", "203.0.113.181");

        using var reader = await ReaderAsync(scene.Branch, "tl-reader", "203.0.113.183", CustomersPermissions.ReadNotes);

        using var body = await ReadTimelineAsync(reader, scene.CustomerId);
        var billingEntries = body.RootElement.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("source").GetString() == "billing")
            .ToArray();

        billingEntries.Select(entry => entry.GetProperty("kind").GetString()).ShouldBe(
            [
                "billing.invoice.posted",
                "billing.debit_note.posted",
                "billing.invoice.cancelled",
                "billing.credit_note.posted",
            ],
            ignoreOrder: true);

        billingEntries.ShouldAllBe(entry =>
            entry.GetProperty("referenceType").GetString() == "Invoice"
            && entry.GetProperty("referenceId").GetGuid() == invoiceId
            && entry.GetProperty("expandPermission").GetString() == BillingPermissions.CreateInvoice
            && entry.GetProperty("branchId").GetGuid() == scene.Branch
            && !string.IsNullOrWhiteSpace(entry.GetProperty("actorDisplayName").GetString()));

        var posted = billingEntries.Single(entry => entry.GetProperty("kind").GetString() == "billing.invoice.posted");
        posted.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null, "posting carries no reason to withhold or show");
        posted.GetProperty("reasonPermission").ValueKind.ShouldBe(JsonValueKind.Null);

        var cancelled = billingEntries.Single(entry => entry.GetProperty("kind").GetString() == "billing.invoice.cancelled");
        cancelled.GetProperty("reason").GetString().ShouldBe(cancelReason, "the reader holds customers.read_notes");
        cancelled.GetProperty("reasonPermission").GetString().ShouldBe(CustomersPermissions.ReadNotes);

        body.RootElement.GetProperty("unavailableSources").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task WithholdsTheCancellationReasonFromAReaderWithoutReadNotes()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-reason", "203.0.113.184", "TL2", RunToken);
        await PostDebitAndCancelAsync(scene, "tl-actor2", "203.0.113.196");

        using var reader = await ReaderAsync(scene.Branch, "tl-noreason", "203.0.113.185");

        using var body = await ReadTimelineAsync(reader, scene.CustomerId);
        var cancelled = body.RootElement.GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("kind").GetString() == "billing.invoice.cancelled");

        cancelled.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        cancelled.GetProperty("reasonPermission").GetString().ShouldBe(
            CustomersPermissions.ReadNotes, "a reason exists and is withheld, which is a different answer from none given");
    }

    [Fact]
    public async Task PagesTheFourEntriesWithoutRepeatingOrSkippingOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-page", "203.0.113.186", "TL3", RunToken);
        await PostDebitAndCancelAsync(scene, "tl-actor3", "203.0.113.197");

        using var reader = await ReaderAsync(scene.Branch, "tl-pager", "203.0.113.187");

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            using var page = await ReadTimelineAsync(reader, scene.CustomerId, cursor, limit: 1);
            var entries = page.RootElement.GetProperty("entries");
            entries.GetArrayLength().ShouldBeLessThanOrEqualTo(1);

            seen.AddRange(entries.EnumerateArray().Select(entry => entry.GetProperty("entryId").GetGuid()));
            cursor = page.RootElement.GetProperty("nextCursor").ValueKind is JsonValueKind.Null
                ? null
                : page.RootElement.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        cursor.ShouldBeNull("the paging never reached the end");
        seen.Count.ShouldBe(4, "posted, debit note, cancelled, credit note");
        seen.Distinct().Count().ShouldBe(seen.Count, "an entry was returned on two pages");
    }

    [Fact]
    public async Task ShowsNoInvoiceEntryToAReaderWithoutCreateInvoice()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-noperm", "203.0.113.188", "TL4", RunToken);
        await PostDebitAndCancelAsync(scene, "tl-actor4", "203.0.113.198");

        using var reader = await AdministrationHarness.AdministratorAtBranchAsync(
            fixture, "tl-plain", "203.0.113.189", scene.Branch, CustomersPermissions.Read);

        using var body = await ReadTimelineAsync(reader, scene.CustomerId);

        body.RootElement.GetProperty("entries").EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("source").GetString() == "billing");
        body.RootElement.GetProperty("unavailableSources").GetArrayLength().ShouldBe(
            0, "the permission gate returns an empty page rather than failing the source");
    }

    [Fact]
    public async Task ShowsNoInvoiceEntryToAReaderOfAnotherBranch()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-branch", "203.0.113.190", "TL5", RunToken);
        await PostDebitAndCancelAsync(scene, "tl-actor5", "203.0.113.199");

        var elsewhere = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var reader = await AdministrationHarness.AdministratorAtBranchAsync(
            fixture, "tl-elsewhere", "203.0.113.191", elsewhere, CustomersPermissions.Read, [BillingPermissions.CreateInvoice]);

        using var body = await ReadTimelineAsync(reader, scene.CustomerId);

        body.RootElement.GetProperty("entries").EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("source").GetString() == "billing");
    }

    [Fact]
    public async Task ProducesNoEntryForADraftOrADiscardedInvoice()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-draft", "203.0.113.192", "TL6", RunToken);
        using var cashier = await InvoiceScenes.CashierAsync(fixture, "tl-drafter", "203.0.113.193", scene.Branch);
        var (invoiceId, tag) = await InvoiceScenes.DraftAsync(cashier, scene.OrderId, scene.Reference);
        _ = invoiceId;

        using var reader = await ReaderAsync(scene.Branch, "tl-draftreader", "203.0.113.194");

        using (var stillDraft = await ReadTimelineAsync(reader, scene.CustomerId))
        {
            stillDraft.RootElement.GetProperty("entries").EnumerateArray()
                .ShouldNotContain(entry => entry.GetProperty("source").GetString() == "billing");
        }

        var discarded = await cashier.PostAsync(
            $"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "Customer changed her mind." }, InvoiceScenes.Tagged(tag));
        discarded.StatusCode.ShouldBe(HttpStatusCode.OK, await discarded.Content.ReadAsStringAsync(Token));

        using var afterDiscard = await ReadTimelineAsync(reader, scene.CustomerId);
        afterDiscard.RootElement.GetProperty("entries").EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("source").GetString() == "billing");
    }

    [Fact]
    public async Task ReturnsNothingForACustomerOfAnotherOrganisation()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await InvoiceScenes.BuildAsync(fixture, "tl-org", "203.0.113.195", "TL7", RunToken);
        await PostDebitAndCancelAsync(scene, "tl-actor6", "203.0.113.200");

        // The test host authenticates every HTTP caller into one fixed organisation (SessionTestData),
        // so the cross-organisation case is proved by calling the source directly, as
        // PriceListEndpointTests.RefusesAnotherOrganisationsBranchToAVersionAndToARegistration does for
        // the same reason. Everything else about the request is exactly what the real endpoint would send.
        using var scope = fixture.Services.CreateScope();
        var source = scope.ServiceProvider.GetServices<ITimelineSource>().OfType<BillingTimelineSource>().Single();

        var query = new TimelineQuery(
            scene.CustomerId,
            new OrganisationContext(Guid.CreateVersion7(), null),
            new HashSet<Guid> { scene.Branch },
            new HashSet<string>(StringComparer.Ordinal) { BillingPermissions.CreateInvoice, CustomersPermissions.ReadNotes },
            null,
            25);

        (await source.ReadAsync(query, Token)).ShouldBeEmpty();
    }

    /// <summary>Posts an invoice, adds a debit note, then cancels it — the four kinds in one invoice.</summary>
    private async Task<(Guid InvoiceId, string CancelReason)> PostDebitAndCancelAsync(InvoiceScene scene, string prefix, string address)
    {
        using var cashier = await InvoiceScenes.CashierAsync(
            fixture, prefix, address, scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.CancelInvoice, BillingPermissions.PostCreditNote);

        var (invoiceId, tag) = await InvoiceScenes.DraftAsync(cashier, scene.OrderId, scene.Reference);
        var posted = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/post", new { reason = (string?)null }, InvoiceScenes.Tagged(tag));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(Token));

        var debit = await cashier.PostAsync(
            $"/api/v1/billing/invoices/{invoiceId}/debit-notes",
            new { lines = new[] { new { garmentJobId = scene.Jobs[1], taxableValue = 50m } }, reason = "Express finishing." },
            InvoiceScenes.Key());
        debit.StatusCode.ShouldBe(HttpStatusCode.Created, await debit.Content.ReadAsStringAsync(Token));

        const string cancelReason = "Issued to the wrong customer.";
        var cancelled = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/cancel", new { reason = cancelReason }, InvoiceScenes.Key());
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Token));

        return (invoiceId, cancelReason);
    }

    private Task<AdministrationHarness.AdministratorClient> ReaderAsync(Guid branch, string prefix, string address, params string[] alsoGrant)
        => AdministrationHarness.AdministratorAtBranchAsync(
            fixture, prefix, address, branch, CustomersPermissions.Read, [BillingPermissions.CreateInvoice, .. alsoGrant]);

    private static async Task<JsonDocument> ReadTimelineAsync(
        AdministrationHarness.AdministratorClient client,
        Guid customerId,
        string? cursor = null,
        int? limit = null)
    {
        var route = $"/api/v1/customers/{customerId}/timeline";
        var query = new List<string>();

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (limit is { } size)
        {
            query.Add($"limit={size}");
        }

        if (query.Count > 0)
        {
            route += "?" + string.Join('&', query);
        }

        var response = await client.GetAsync(route);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, route);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }
}
