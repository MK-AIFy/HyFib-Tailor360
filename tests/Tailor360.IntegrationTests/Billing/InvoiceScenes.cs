using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Contracts.Events;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// What an invoice test starts from: a branch with a registration and a published price list, a customer of
/// the branch, an order with garment jobs known to Billing through the outbox, and the order priced under its
/// reference.
/// </summary>
/// <param name="Owner">The administrator who configured the branch; holds <c>admin.branches</c> and the price-list permissions.</param>
/// <param name="Branch">The branch.</param>
/// <param name="BranchCode">The branch's code, which every number drawn at the branch carries.</param>
/// <param name="TaxCode">A published tax code of 5% split in two.</param>
/// <param name="ItemCode">The stitching item, 450.</param>
/// <param name="SurchargeCode">The lining surcharge, 90.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its display number.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="Jobs">The order's garment jobs.</param>
/// <param name="Reference">The reference the order was priced under.</param>
/// <param name="Result">The stored calculation.</param>
internal sealed record InvoiceScene(
    AdministrationHarness.AdministratorClient Owner,
    Guid Branch,
    string BranchCode,
    string TaxCode,
    string ItemCode,
    string SurchargeCode,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid[] Jobs,
    string Reference,
    PricingResult Result);

/// <summary>Builds <see cref="InvoiceScene"/>s and drives the routes and the outbox the invoice tests share.</summary>
internal static class InvoiceScenes
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Builds a scene: one order with two garment jobs, priced whole with the lining on each.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="prefix">A short prefix for the owner's account.</param>
    /// <param name="address">The owner's client address.</param>
    /// <param name="stem">A stem every code of the scene carries.</param>
    /// <param name="runToken">The run's token, so two runs on one database never share a code.</param>
    public static async Task<InvoiceScene> BuildAsync(WebApplicationFixture fixture, string prefix, string address, string stem, string runToken)
    {
        string Code(string part) => $"{part}_{runToken}";

        var owner = await AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList, IdentityPermissions.Branches);
        var (branch, branchCode) = await BillingHarness.OpenBranchWithCodeAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, Code($"TAX_{stem}"), $"Tax for invoices {runToken} {stem}");
        await RegisterAsync(owner, branch);
        var itemCode = Code($"{stem}_STITCH");
        var surchargeCode = Code($"{stem}_LINING");
        var list = await CreateListAsync(owner, Code($"PL_{stem}"));
        var version = await DraftAsync(owner, list, [branch]);
        await AddItemAsync(owner, version, itemCode, 450m, taxCode);
        await AddItemAsync(owner, version, surchargeCode, 90m, taxCode, kind: "Surcharge");
        await PublishAsync(owner, version);

        var customerId = await CustomerHarness.CustomerAsync(fixture, branch);
        var orderNumber = $"O-{runToken}-{stem[..Math.Min(3, stem.Length)]}";
        var (orderId, jobs) = await ConfirmOrderAsync(fixture, branch, customerId, orderNumber, 2);

        var reference = $"order:{orderId:N}:1";
        var result = await PriceAsync(fixture, branch, reference, [.. jobs.Select(job => job.ToString())], itemCode, surchargeCode);

        return new InvoiceScene(owner, branch, branchCode, taxCode, itemCode, surchargeCode, orderId, orderNumber, customerId, jobs, reference, result);
    }

    /// <summary>
    /// Confirms an order with garment jobs at a branch, as Orders announces one: the events into Orders' own
    /// outbox, delivered to Billing by the dispatcher. The jobs are published before the confirmation on
    /// purpose, because the projector must cope with either order.
    /// </summary>
    /// <returns>The order and its jobs.</returns>
    public static async Task<(Guid OrderId, Guid[] Jobs)> ConfirmOrderAsync(WebApplicationFixture fixture, Guid branch, Guid customerId, string orderNumber, int jobCount)
    {
        var orderId = Guid.CreateVersion7();
        var jobs = Enumerable.Range(0, jobCount).Select(_ => Guid.CreateVersion7()).ToArray();
        var now = Now(fixture);

        await PublishAsync(fixture, publisher =>
        {
            for (var index = 0; index < jobs.Length; index++)
            {
                publisher.Publish(new GarmentJobCreated(
                    Guid.CreateVersion7(), now, jobs[index], SessionTestData.OrganisationId, branch, orderId, $"{orderNumber}-0{index + 1}", index + 1,
                    "blouse", "stitching", Guid.CreateVersion7(), new DateOnly(2026, 9, 20)));
            }

            publisher.Publish(new OrderConfirmed(
                Guid.CreateVersion7(), now, orderId, SessionTestData.OrganisationId, branch, customerId, orderNumber, Guid.CreateVersion7(), null,
                new DateOnly(2026, 9, 20), jobs.Length, 1));
        });
        await DispatchAsync(fixture);

        using var scope = fixture.Services.CreateScope();
        var fact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().OrderFacts.AsNoTracking()
            .Include(order => order.Jobs).SingleAsync(order => order.OrderId == orderId, Token);
        fact.CustomerId.ShouldBe(customerId);
        fact.Jobs.Select(job => job.GarmentJobId).Order().ShouldBe(jobs.Order());

        return (orderId, jobs);
    }

    /// <summary>A cashier whose session is at the branch, holding the draft and update permissions and whatever else is named.</summary>
    public static Task<AdministrationHarness.AdministratorClient> CashierAsync(WebApplicationFixture fixture, string prefix, string address, Guid branch, params string[] alsoGrant)
        => AdministrationHarness.AdministratorAtBranchAsync(fixture, prefix, address, branch, BillingPermissions.CreateInvoice, [BillingPermissions.UpdateInvoice, .. alsoGrant]);

    /// <summary>Publishes into Orders' own outbox, committed by Orders' own save, as Orders does it.</summary>
    public static async Task PublishAsync(WebApplicationFixture fixture, Action<IOrdersEventPublisher> publish)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        publish(scope.ServiceProvider.GetRequiredService<IOrdersEventPublisher>());
        await context.SaveChangesAsync(Token);
    }

    /// <summary>Runs dispatcher cycles until nothing is left to deliver, bounded.</summary>
    public static async Task DispatchAsync(WebApplicationFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        for (var cycle = 0; cycle < 20; cycle++)
        {
            if (await dispatcher.RunCycleAsync("invoice-test", Token) == 0)
            {
                return;
            }
        }
    }

    /// <summary>Prices through the contract, as Orders would: no session, so no override permission.</summary>
    public static async Task<PricingResult> PriceAsync(WebApplicationFixture fixture, Guid branch, string reference, string[] lineKeys, string itemCode, string? surcharge = null)
    {
        using var scope = fixture.Services.CreateScope();
        var priced = await scope.ServiceProvider.GetRequiredService<IPricingService>().PriceAsync(
            new PricingRequest(
                SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", reference,
                [.. lineKeys.Select(key => new PricingLineRequest(key, itemCode, 1m, surcharge is null ? [] : [surcharge], null, null))]),
            Token);
        priced.IsSuccess.ShouldBeTrue(priced.IsFailure ? priced.Error.Message : string.Empty);
        return priced.Value;
    }

    /// <summary>The host's clock, read in a scope of its own.</summary>
    public static DateTimeOffset Now(WebApplicationFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
    }

    /// <summary>Asserts a refusal by status and by the code in its body.</summary>
    public static async Task Refused(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(status, body);
        body.ShouldContain(code);
    }

    /// <summary>The invoice identifier a Created answer points at.</summary>
    public static Guid CreatedId(HttpResponseMessage response) => Guid.Parse(response.Headers.Location!.ToString().Split('/')[^1]);

    /// <summary>Drafts an invoice for a scene's order from its stored calculation.</summary>
    public static async Task<(Guid InvoiceId, string Tag)> DraftAsync(AdministrationHarness.AdministratorClient cashier, Guid orderId, string reference)
    {
        var created = await cashier.PostAsync("/api/v1/billing/invoices", new { orderId, calculationReference = reference, garmentJobIds = Array.Empty<Guid>(), reason = (string?)null }, Key());
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        return (CreatedId(created), created.Headers.ETag!.ToString());
    }

    public static (string Name, string Value)[] Key() => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    public static (string Name, string Value)[] Tagged(string tag) => [.. Key(), ("If-Match", tag)];

    // ---- configuration through the routes, as the pricing tests do it ----------------------------

    public static async Task<(string Name, string Value)[]> VersionKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/price-lists/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    public static async Task<Guid> CreateListAsync(AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync("/api/v1/billing/price-lists", new { code, name = code, reason = (string?)null }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("priceListId").GetGuid();
    }

    public static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, Guid list, Guid[] branches)
    {
        var response = await client.PostAsync(
            $"/api/v1/billing/price-lists/{list}/versions",
            new
            {
                name = "Priced",
                notes = (string?)null,
                effectiveFrom = "2026-04-01",
                taxInclusive = false,
                roundOff = "NearestRupee",
                overrideThresholdPercent = 10m,
                branchIds = branches,
                cloneFromVersionId = (Guid?)null,
                reason = (string?)null,
            },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("priceListVersionId").GetGuid();
    }

    public static async Task AddItemAsync(AdministrationHarness.AdministratorClient client, Guid version, string code, decimal rate, string taxCode, string kind = "Service")
    {
        var response = await client.PostAsync(
            $"/api/v1/billing/price-lists/versions/{version}/items",
            new { code, description = "A synthetic item", kind, baseRate = rate, unit = "each", taxCode, active = true, reason = (string?)null },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
    }

    public static async Task PublishAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        var response = await client.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Live." }, await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
    }

    public static async Task RegisterAsync(AdministrationHarness.AdministratorClient client, Guid branch)
    {
        var response = await client.PostAsync(
            "/api/v1/billing/gst-registrations",
            new
            {
                branchId = branch,
                gstin = "33AAACH7409R1Z8",
                stateCode = "33",
                legalName = "Example Tailors Private Limited",
                tradeName = "Example Tailors",
                effectiveFrom = new DateOnly(2026, 4, 1),
                effectiveTo = (DateOnly?)null,
                reason = "Written by an integration test.",
            },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
    }

    /// <summary>Publishes a tax configuration version holding one code of the run's own, cloned from whatever is published; returns the code.</summary>
    public static async Task<string> EnsurePublishedTaxCodeAsync(AdministrationHarness.AdministratorClient client, string code, string versionName)
    {
        using var listed = JsonDocument.Parse(await (await client.GetAsync("/api/v1/billing/tax-configuration/versions")).Content.ReadAsStringAsync(Token));
        var live = listed.RootElement.EnumerateArray().FirstOrDefault(row => row.GetProperty("status").GetString() == "Published");
        Guid? cloneFrom = live.ValueKind == JsonValueKind.Object ? live.GetProperty("taxConfigurationVersionId").GetGuid() : null;

        var draft = await client.PostAsync(
            "/api/v1/billing/tax-configuration/versions",
            new { name = versionName, notes = (string?)null, effectiveFrom = "2026-04-01", cloneFromVersionId = cloneFrom },
            Key());
        draft.StatusCode.ShouldBe(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await draft.Content.ReadAsStringAsync(Token));
        var version = body.RootElement.GetProperty("version").GetProperty("taxConfigurationVersionId").GetGuid();

        var added = await client.PostAsync(
            $"/api/v1/billing/tax-configuration/versions/{version}/tax-codes",
            new
            {
                code,
                description = "Tailoring services",
                classification = "998821",
                kind = "Services",
                active = true,
                rates = new[] { new { kind = "Cgst", ratePercent = 2.5m }, new { kind = "Sgst", ratePercent = 2.5m }, new { kind = "Igst", ratePercent = 5m } },
                reason = (string?)null,
            },
            await TaxKeyAsync(client, version));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        var publish = await client.PostAsync($"/api/v1/billing/tax-configuration/versions/{version}/publish", new { reason = "For the invoice tests." }, await TaxKeyAsync(client, version));
        publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Token));
        return code;
    }

    private static async Task<(string Name, string Value)[]> TaxKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }
}
