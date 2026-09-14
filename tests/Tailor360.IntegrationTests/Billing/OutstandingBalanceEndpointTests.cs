using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Security.Permissions;
using static Tailor360.IntegrationTests.Billing.InvoiceScenes;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The branch's outstanding balances over HTTP (#421): the server-side aggregate replacing the client's
/// own fan-out over <c>ListInvoices</c> and <c>GetOrderBalance</c> — deny by default, branch isolation, a
/// clamped limit, an unrecognised cursor answered as the first page, and the scan bound's own exception
/// (a page can be empty and still carry a cursor).
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OutstandingBalanceEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    /// <summary>Mirrors <c>OutstandingBalanceQuery.MaximumSourcePagesPerRequest</c>, a private engineering bound.</summary>
    private const int MaximumSourcePagesPerRequest = 10;

    [Fact]
    public async Task AnswersExactlyThePartPaidInvoiceWithGetOrderBalancesOwnFigure()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await SeedPaymentModesAsync();
        var scene = await BuildAsync(fixture, "out-flow", "203.0.113.210", "OUT", RunToken);
        using var cashier = await CashierAsync(fixture, "out-cashier", "203.0.113.211", scene.Branch,
            BillingPermissions.PostInvoice, BillingPermissions.RecordPayment, BillingPermissions.Session);

        var opened = await cashier.PostAsync("/api/v1/billing/cashier-sessions", new { openingFloat = 500m }, Key());
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Token));

        // One invoice per garment, 567 each: the first is settled in full, the second only part-paid.
        var (settledId, settledTag) = await DraftOneAsync(scene, cashier, 0);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{settledId}/post", new { reason = (string?)null }, Tagged(settledTag)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 567m, reference = (string?)null }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var (partId, partTag) = await DraftOneAsync(scene, cashier, 1);
        (await cashier.PostAsync($"/api/v1/billing/invoices/{partId}/post", new { reason = (string?)null }, Tagged(partTag)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cashier.PostAsync("/api/v1/billing/payments", new { orderId = scene.OrderId, modeCode = "CASH", amount = 100m, reference = (string?)null }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var balance = await BalanceAsync(cashier, scene.OrderId);
        var expectedOutstanding = balance.GetProperty("invoices").EnumerateArray()
            .Single(row => row.GetProperty("invoiceId").GetGuid() == partId).GetProperty("outstanding").GetDecimal();
        expectedOutstanding.ShouldBe(467m);

        var page = await OutstandingAsync(cashier);
        var rows = page.GetProperty("rows").EnumerateArray().ToArray();
        rows.Select(row => row.GetProperty("invoiceId").GetGuid()).ShouldBe([partId]);
        rows[0].GetProperty("outstanding").GetDecimal().ShouldBe(expectedOutstanding);
        rows[0].GetProperty("orderId").GetGuid().ShouldBe(scene.OrderId);
        rows[0].GetProperty("orderNumber").GetString().ShouldBe(scene.OrderNumber);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task RefusesACallerHoldingNoBillingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "out-deny", "203.0.113.212", "DENY", RunToken);
        using var reader = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "out-deny-r", "203.0.113.213", scene.Branch, CatalogPermissions.Read);

        (await reader.GetAsync("/api/v1/billing/outstanding-balances")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NeverShowsAnotherBranchsInvoiceAndIgnoresAnUnrecognisedCursor()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "out-branch", "203.0.113.214", "BR", RunToken);
        using var cashier = await CashierAsync(fixture, "out-branch-c", "203.0.113.215", scene.Branch);

        var secondBranch = await BillingHarness.OpenBranchAsync(scene.Owner);
        var secondCustomer = await CustomerHarness.CustomerAsync(fixture, secondBranch);
        var elsewhereId = await SeedOutstandingInvoiceAsync(secondBranch, secondCustomer, $"O-{RunToken}-ELSEWHERE", 250m);

        var here = await SeedOutstandingInvoiceAsync(scene.Branch, scene.CustomerId, $"O-{RunToken}-HERE", 300m);

        var page = await OutstandingAsync(cashier);
        var ids = page.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()).ToArray();
        ids.ShouldContain(here);
        ids.ShouldNotContain(elsewhereId);

        // A cursor this route never issued is ignored, and the first page is returned — InvoiceStore's own
        // behaviour, the same ListInvoices already has (billing.md issue #421 acceptance criteria).
        var withBogusCursor = await OutstandingAsync(cashier, "?cursor=not-a-real-cursor");
        withBogusCursor.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid())
            .ShouldBe(page.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()));
    }

    [Fact]
    public async Task ClampsAnOutOfRangeLimitRatherThanRefusingIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "out-clamp", "203.0.113.216", "CLAMP", RunToken);
        using var cashier = await CashierAsync(fixture, "out-clamp-c", "203.0.113.217", scene.Branch);
        var invoiceId = await SeedOutstandingInvoiceAsync(scene.Branch, scene.CustomerId, $"O-{RunToken}-CLAMP", 300m);

        // Honoured or refused would answer 400; clamped answers 200 with the row all the same.
        var over = await OutstandingAsync(cashier, "?limit=500");
        over.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()).ShouldContain(invoiceId);

        var under = await OutstandingAsync(cashier, "?limit=0");
        under.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()).ShouldContain(invoiceId);
    }

    [Fact]
    public async Task AtTheScanBoundAnEmptyPageStillCarriesANonNullCursorRatherThanTheEmptyState()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await BuildAsync(fixture, "out-bound", "203.0.113.218", "BOUND", RunToken);
        using var cashier = await CashierAsync(fixture, "out-bound-c", "203.0.113.219", scene.Branch);

        // One more posted invoice than ten source pages of fifty can scan, every one of them settled
        // (cancelled, so nothing is outstanding): the read must stop at the bound with an empty answer
        // and a cursor still in hand, not report the branch as fully scanned.
        await SeedCancelledPostedInvoicesAsync(scene.Branch, scene.CustomerId, (MaximumSourcePagesPerRequest * InvoiceListQuery.MaximumLimit) + 1, "BOUND");

        var page = await OutstandingAsync(cashier);
        page.GetProperty("rows").EnumerateArray().ShouldBeEmpty();
        page.GetProperty("nextCursor").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static async Task<JsonElement> OutstandingAsync(AdministrationHarness.AdministratorClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/v1/billing/outstanding-balances{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
    }

    private static async Task<JsonElement> BalanceAsync(AdministrationHarness.AdministratorClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/v1/billing/orders/{orderId}/balance");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
    }

    /// <summary>One garment of the scene priced and drafted on its own, so the order posts 567 at a time.</summary>
    private async Task<(Guid InvoiceId, string Tag)> DraftOneAsync(InvoiceScene scene, AdministrationHarness.AdministratorClient cashier, int job)
    {
        var reference = $"order:{scene.OrderId:N}:job{job + 1}";
        await PriceAsync(scene.Branch, reference, [scene.Jobs[job].ToString()], scene.ItemCode, scene.SurchargeCode);
        return await DraftAsync(cashier, scene.OrderId, reference);
    }

    private async Task SeedPaymentModesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBillingReferenceDataSeeder>().SeedPaymentModesAsync(SessionTestData.OrganisationId, Token);
    }

    /// <summary>A posted invoice at the branch with nothing allocated or refunded against it: fully outstanding.</summary>
    private async Task<Guid> SeedOutstandingInvoiceAsync(Guid branchId, Guid customerId, string orderNumber, decimal amount)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
        // The last 8 hex characters, not the first: a version-7 GUID's leading bits are a
        // millisecond timestamp, so two calls within the same run collide on a truncated prefix far
        // more often than the birthday bound on a random suffix would suggest. The tail is random.
        var suffix = Guid.CreateVersion7().ToString("N")[24..];

        // The row's own trigger (billing.posted_invoices_are_immutable) insists an invoice is
        // *inserted* as a draft and reaches Posted only by an update, exactly as PostInTransactionAsync
        // does it — so posting has to be its own save, after the draft's insert has committed.
        var invoice = Drafted(branchId, customerId, orderNumber, amount, suffix);
        store.Add(invoice);
        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

        invoice.Post($"INV-{suffix}", $"I-{suffix}", "2627", new DateOnly(2026, 9, 12), Now(), null).IsSuccess.ShouldBeTrue();
        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

        return invoice.Id;
    }

    /// <summary>Posted invoices, each cancelled the moment it posts (INV-PAY-06: nothing outstanding on any of them).</summary>
    private async Task SeedCancelledPostedInvoicesAsync(Guid branchId, Guid customerId, int count, string prefix)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
        var now = Now();

        // RunToken in the suffix, not just the order number: the document-number and barcode unique
        // indexes are scoped by organisation only, and this run's organisation is the fixed constant
        // every test in this file shares, so two runs against a database that outlives one dotnet test
        // process (a developer's own compose Postgres, reused rather than dropped) must not collide.
        var shortToken = RunToken[..Math.Min(4, RunToken.Length)];
        var shortPrefix = prefix[..Math.Min(3, prefix.Length)];

        var drafts = new List<(Invoice Invoice, string Suffix)>();
        for (var index = 0; index < count; index++)
        {
            var suffix = $"{shortToken}{shortPrefix}{index:0000}";
            var invoice = Drafted(branchId, customerId, $"O-{RunToken}-{prefix}-{index:0000}", 100m, suffix);
            store.Add(invoice);
            drafts.Add((invoice, suffix));
        }

        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

        foreach (var (invoice, suffix) in drafts)
        {
            invoice.Post($"INV-{suffix}", $"I-{suffix}", "2627", new DateOnly(2026, 9, 12), now, null).IsSuccess.ShouldBeTrue();
            invoice.Cancel(Guid.CreateVersion7(), Guid.CreateVersion7(), $"CN-{suffix}", "Synthetic, for the scan-bound test.", new DateOnly(2026, 9, 12), now, null)
                .IsSuccess.ShouldBeTrue();
        }

        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>A minimal draft invoice, one line, no surcharge and no tax split — enough to carry a grand total.</summary>
    private Invoice Drafted(Guid branchId, Guid customerId, string orderNumber, decimal amount, string numberSuffix)
    {
        var jobId = Guid.CreateVersion7();
        var customer = new InvoiceCustomer($"C-{numberSuffix}", "Synthetic Customer", null, null, null);
        var calculation = new InvoiceCalculation(
            $"order:{numberSuffix}:1", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "33AAACH7409R1Z8", "33", "33", "IntraState", false, "Example Tailors Private Limited", "Example Tailors");
        var line = new InvoicedLine(
            jobId, jobId.ToString(), "BOUND_ITEM", "Stitching", 1m, amount, amount, Money.Rupees(amount),
            [], null, null, null, Money.Zero, Money.Rupees(amount), Money.Rupees(amount), "STITCHING_5", "998822", "Services",
            [], Money.Zero, Money.Rupees(amount), Money.Zero);
        var totals = new InvoiceTotals(Money.Rupees(amount), Money.Zero, Money.Rupees(amount), Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Rupees(amount));

        return Invoice.CreateDraft(
                Guid.CreateVersion7(), SessionTestData.OrganisationId, branchId, customerId, Guid.CreateVersion7(), orderNumber,
                customer, calculation, [line], totals, Now(), null)
            .Value;
    }

    private Task<PricingResult> PriceAsync(Guid branch, string reference, string[] lineKeys, string itemCode, string? surcharge)
        => InvoiceScenes.PriceAsync(fixture, branch, reference, lineKeys, itemCode, surcharge);

    private DateTimeOffset Now() => InvoiceScenes.Now(fixture);
}
