using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Contracts.Events;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// Invoice drafts end to end (#153): the order's facts arrive through the outbox, the order is priced under
/// a reference through the contract, and a cashier at the order's branch drafts, reads, re-prices and
/// discards — with every refusal the issue names answered by its code.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class InvoiceEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task DraftsFromTheStoredCalculationThenReadsListsRepricesDiscardsAndReplays()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await SceneAsync("inv-draft", "203.0.113.240", "DRAFT");
        using var cashier = await CashierAsync("inv-cashier", "203.0.113.241", scene.Branch, CustomerSnapshot.ContactPermission);

        // Drafted whole from the calculation: two garment jobs, the walkthrough blouse each.
        var key = Key();
        var created = await cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), key);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        var invoiceId = Guid.Parse(created.Headers.Location!.ToString().Split('/')[^1]);
        string customerName;
        using (var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Token)))
        {
            var root = body.RootElement;
            root.GetProperty("invoiceId").GetGuid().ShouldBe(invoiceId);
            root.GetProperty("status").GetString().ShouldBe("Draft");
            root.GetProperty("revision").GetInt32().ShouldBe(1);
            root.GetProperty("orderNumber").GetString().ShouldBe(scene.OrderNumber);
            root.GetProperty("customer").GetProperty("addressLine").GetString().ShouldNotBeNullOrEmpty("the cashier holds the contact permission");
            customerName = root.GetProperty("customer").GetProperty("displayName").GetString()!;
            root.GetProperty("calculation").GetProperty("reference").GetString().ShouldBe(scene.Reference);
            root.GetProperty("calculation").GetProperty("gstin").GetString().ShouldBe("33AAACH7409R1Z8");
            var lines = root.GetProperty("lines").EnumerateArray().ToArray();
            lines.Select(line => line.GetProperty("garmentJobId").GetGuid()).ShouldBe(scene.Jobs);
            lines.Select(line => line.GetProperty("lineNumber").GetInt32()).ShouldBe([1, 2]);
            lines[0].GetProperty("surcharges").EnumerateArray().Single().GetProperty("amount").GetDecimal().ShouldBe(90m);
            lines[0].GetProperty("taxes").EnumerateArray().Select(tax => tax.GetProperty("amount").GetDecimal()).ShouldBe([13.5m, 13.5m]);
            root.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(1134m);
        }

        // A replay of the same key answers the first outcome; a second draft for the same jobs is refused.
        var replayed = await cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), key);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Created);
        using (var body = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("invoiceId").GetGuid().ShouldBe(invoiceId, "a replay is the first outcome, not a second draft");
        }
        var again = await cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key());
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync(Token)).ShouldContain("billing.job-already-invoiced");

        // Read, with the tag a change sends back; listed under the branch, filtered by status.
        var read = await cashier.GetAsync($"/api/v1/billing/invoices/{invoiceId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tag = read.Headers.ETag!.ToString();
        using (var listed = JsonDocument.Parse(await (await cashier.GetAsync("/api/v1/billing/invoices?status=Draft&limit=50")).Content.ReadAsStringAsync(Token)))
        {
            listed.RootElement.GetProperty("invoices").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()).ShouldContain(invoiceId);
        }

        (await cashier.GetAsync("/api/v1/billing/invoices?status=Nonsense")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Re-priced with the same jobs the other way round: each job keeps its row and takes a new number.
        var reordered = await cashier.PutAsync($"/api/v1/billing/invoices/{invoiceId}", RepriceBody(scene, [scene.Jobs[1], scene.Jobs[0]]), Tagged(tag));
        reordered.StatusCode.ShouldBe(HttpStatusCode.OK, await reordered.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await reordered.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("revision").GetInt32().ShouldBe(2);
            body.RootElement.GetProperty("lines").EnumerateArray().Select(line => (line.GetProperty("lineNumber").GetInt32(), line.GetProperty("garmentJobId").GetGuid()))
                .ShouldBe([(1, scene.Jobs[1]), (2, scene.Jobs[0])]);
        }

        tag = reordered.Headers.ETag!.ToString();

        // A re-price refused for its lines leaves nothing behind: the next edit is not blocked by it.
        var stranger = await cashier.PutAsync($"/api/v1/billing/invoices/{invoiceId}", RepriceBody(scene, [Guid.CreateVersion7()]), Tagged(tag));
        stranger.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await stranger.Content.ReadAsStringAsync(Token)).ShouldContain("billing.line-not-a-garment-job");

        // Re-priced to one job under a reference of the draft's own; the stale tag is then refused.
        var repriced = await cashier.PutAsync($"/api/v1/billing/invoices/{invoiceId}", RepriceBody(scene, [scene.Jobs[1]]), Tagged(tag));
        repriced.StatusCode.ShouldBe(HttpStatusCode.OK, await repriced.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await repriced.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("revision").GetInt32().ShouldBe(3);
            body.RootElement.GetProperty("lines").EnumerateArray().Single().GetProperty("garmentJobId").GetGuid().ShouldBe(scene.Jobs[1]);
            body.RootElement.GetProperty("calculation").GetProperty("reference").GetString().ShouldStartWith($"invoice:{invoiceId:N}:3:");
            // One job without its lining: 450 plus 5%, rounded to the rupee, with the round-off on the document.
            body.RootElement.GetProperty("totals").GetProperty("roundOff").GetDecimal().ShouldBe(0.5m);
            body.RootElement.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(473m);
        }

        var freshTag = repriced.Headers.ETag!.ToString();
        freshTag.ShouldNotBe(tag);
        (await cashier.PutAsync($"/api/v1/billing/invoices/{invoiceId}", RepriceBody(scene, [scene.Jobs[0]]), Tagged(tag)))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // The freed job may be drafted by another invoice; the one still on this draft may not.
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
        (await store.AlreadyInvoicedAsync(SessionTestData.OrganisationId, scene.Jobs, Token)).ShouldBe([scene.Jobs[1]]);

        // Discarded with a reason, once; then the jobs are free and a draft may be made again.
        (await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = (string?)null }, Tagged(freshTag)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a discard without a reason is refused");
        var discarded = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "Customer changed their mind." }, Tagged(freshTag));
        discarded.StatusCode.ShouldBe(HttpStatusCode.OK, await discarded.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await discarded.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("status").GetString().ShouldBe("Discarded");
            body.RootElement.GetProperty("discardReason").GetString().ShouldBe("Customer changed their mind.");
        }

        var twice = await cashier.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "Again." }, Tagged(discarded.Headers.ETag!.ToString()));
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await twice.Content.ReadAsStringAsync(Token)).ShouldContain("billing.invoice-not-editable");
        (await store.AlreadyInvoicedAsync(SessionTestData.OrganisationId, scene.Jobs, Token)).ShouldBeEmpty();

        // Every change was audited with the figures, and the trail never carries the customer's details.
        var trail = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuditEvents
            .Where(entry => entry.EntityId == invoiceId)
            .OrderBy(entry => entry.Sequence)
            .Select(entry => new { entry.Action, entry.Summary, entry.Before, entry.After })
            .ToListAsync(Token);
        trail.Select(entry => entry.Action).ShouldBe(
            ["billing.invoice.drafted", "billing.invoice.updated", "billing.invoice.updated", "billing.invoice.discarded"]);
        var written = string.Join('\n', trail.Select(entry => $"{entry.Summary}\n{entry.Before}\n{entry.After}"));
        written.ShouldContain("1134");
        written.ShouldNotContain(customerName);
        written.ShouldNotContain("Trichy");

        // A cashier without the contact permission drafts the document without the address on it.
        using var plain = await CashierAsync("inv-plain", "203.0.113.242", scene.Branch);
        var redrafted = await plain.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key());
        redrafted.StatusCode.ShouldBe(HttpStatusCode.Created, await redrafted.Content.ReadAsStringAsync(Token));
        using (var body = JsonDocument.Parse(await redrafted.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("customer").GetProperty("addressLine").ValueKind.ShouldBe(JsonValueKind.Null);
            body.RootElement.GetProperty("customer").GetProperty("customerNumber").GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task RefusesWhatTheIssueSaysItMustAndAnswersEachByItsCode()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await SceneAsync("inv-refuse", "203.0.113.243", "REFUSE");
        using var cashier = await CashierAsync("inv-refuser", "203.0.113.244", scene.Branch);

        // The order named must be known to Billing, and the calculation stored.
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(Guid.CreateVersion7(), scene.Reference), Key()), HttpStatusCode.NotFound, "billing.order-not-known");
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, $"order:{RunToken}:nothing"), Key()), HttpStatusCode.NotFound, "billing.calculation-not-found");
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", new { orderId = scene.OrderId, calculationReference = "", garmentJobIds = Array.Empty<Guid>(), reason = (string?)null }, Key()), HttpStatusCode.BadRequest, "billing.value-required");

        // The jobs asked for must be the ones the calculation priced.
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference, [scene.Jobs[0]]), Key()), HttpStatusCode.BadRequest, "billing.line-not-a-garment-job");

        // A calculation priced under a line key that is no job of the order.
        var strangerReference = $"order:{scene.OrderId:N}:stranger";
        await PriceAsync(scene.Branch, strangerReference, [Guid.CreateVersion7().ToString()], scene.ItemCode);
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, strangerReference), Key()), HttpStatusCode.BadRequest, "billing.line-not-a-garment-job");

        // A stored figure that no longer reproduces: the snapshot's result was edited under it.
        var tampered = $"order:{scene.OrderId:N}:tampered";
        await StoreTamperedSnapshotAsync(scene, tampered);
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, tampered), Key()), HttpStatusCode.Conflict, "billing.snapshot-mismatch");

        // An order whose garments have arrived ahead of its confirmation is not invoiced yet.
        var early = Guid.CreateVersion7();
        var earlyJob = Guid.CreateVersion7();
        await PublishAsync(publisher => publisher.Publish(new GarmentJobCreated(
            Guid.CreateVersion7(), Now(), earlyJob, SessionTestData.OrganisationId, scene.Branch, early, $"O-{RunToken}-EARLY-01", 1,
            "blouse", "stitching", Guid.CreateVersion7(), new DateOnly(2026, 9, 20))));
        await DispatchAsync();
        var earlyReference = $"order:{early:N}:1";
        await PriceAsync(scene.Branch, earlyReference, [earlyJob.ToString()], scene.ItemCode);
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(early, earlyReference), Key()), HttpStatusCode.Conflict, "billing.order-not-yet-confirmed");

        // The order at another branch, then a calculation priced under another branch's list.
        var otherBranch = await BillingHarness.OpenBranchAsync(scene.Owner);
        using var elsewhere = await CashierAsync("inv-elsewhere", "203.0.113.245", otherBranch);
        await Refused(elsewhere.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key()), HttpStatusCode.Forbidden, "billing.order-at-another-branch");

        // A list of the other branch's own: publishing a second version of the order's list would retire the first.
        await RegisterAsync(scene.Owner, otherBranch);
        var otherList = await CreateListAsync(scene.Owner, Code("PL_REFUSE_OTHER"));
        var otherVersion = await DraftAsync(scene.Owner, otherList, [otherBranch]);
        var otherItem = Code("REFUSE_OTHER_STITCH");
        await AddItemAsync(scene.Owner, otherVersion, otherItem, 999m, scene.TaxCode);
        await PublishAsync(scene.Owner, otherVersion);
        var otherReference = $"order:{scene.OrderId:N}:other-branch";
        await PriceAsync(otherBranch, otherReference, [.. scene.Jobs.Select(job => job.ToString())], otherItem);
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, otherReference), Key()), HttpStatusCode.Conflict, "billing.calculation-for-another-branch");

        // A garment cancelled at the counter is charged on nothing, drafted or re-priced.
        await PublishAsync(publisher => publisher.Publish(new GarmentJobCancelled(
            Guid.CreateVersion7(), Now(), scene.Jobs[1], SessionTestData.OrganisationId, scene.Branch, scene.OrderId, $"{scene.OrderNumber}-02", "WRONG_SIZE")));
        await DispatchAsync();
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key()), HttpStatusCode.BadRequest, "billing.job-cancelled");
        var survivingReference = $"order:{scene.OrderId:N}:surviving";
        await PriceAsync(scene.Branch, survivingReference, [scene.Jobs[0].ToString()], scene.ItemCode);
        var drafted = await cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, survivingReference), Key());
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync(Token));
        var draftedId = Guid.Parse(drafted.Headers.Location!.ToString().Split('/')[^1]);
        await Refused(
            cashier.PutAsync($"/api/v1/billing/invoices/{draftedId}", RepriceBody(scene, [scene.Jobs[0], scene.Jobs[1]]), Tagged(drafted.Headers.ETag!.ToString())),
            HttpStatusCode.BadRequest, "billing.job-cancelled");

        // Then the whole order cancelled.

        await PublishAsync(publisher => publisher.Publish(new OrderCancelled(
            Guid.CreateVersion7(), Now(), scene.OrderId, SessionTestData.OrganisationId, scene.Branch, scene.CustomerId, scene.OrderNumber, "CUSTOMER_REQUEST")));
        await DispatchAsync();
        await Refused(cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key()), HttpStatusCode.Conflict, "billing.order-cancelled");

        using (var scope = fixture.Services.CreateScope())
        {
            var fact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().OrderFacts.AsNoTracking()
                .SingleAsync(order => order.OrderId == scene.OrderId, Token);
            fact.Status.ShouldBe(OrderFactStatus.Cancelled);
            fact.CancellationReasonCode.ShouldBe("CUSTOMER_REQUEST");
            fact.FindJob(scene.Jobs[1]).ShouldNotBeNull().CancellationReasonCode.ShouldBe("WRONG_SIZE");
            fact.FindJob(scene.Jobs[0]).ShouldNotBeNull().IsCancelled.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task AnInvoiceIsReachableOnlyFromItsBranchAndOnlyWithThePermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var scene = await SceneAsync("inv-reach", "203.0.113.246", "REACH");
        using var cashier = await CashierAsync("inv-reacher", "203.0.113.247", scene.Branch);
        var created = await cashier.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key());
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        var invoiceId = Guid.Parse(created.Headers.Location!.ToString().Split('/')[^1]);
        var tag = created.Headers.ETag!.ToString();

        // A cashier of another branch is told nothing: the record reads as absent, and cannot be changed.
        using var elsewhere = await CashierAsync("inv-stranger", "203.0.113.248", await BillingHarness.OpenBranchAsync(scene.Owner));
        (await elsewhere.GetAsync($"/api/v1/billing/invoices/{invoiceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "Not mine." }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var listed = JsonDocument.Parse(await (await elsewhere.GetAsync("/api/v1/billing/invoices?limit=50")).Content.ReadAsStringAsync(Token)))
        {
            listed.RootElement.GetProperty("invoices").EnumerateArray().Select(row => row.GetProperty("invoiceId").GetGuid()).ShouldNotContain(invoiceId);
        }

        // Deny by default: a signed-in holder of an unrelated permission, at the right branch.
        using var reader = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "inv-deny", "203.0.113.249", scene.Branch, CatalogPermissions.Read);
        (await reader.GetAsync("/api/v1/billing/invoices")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/billing/invoices/{invoiceId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync("/api/v1/billing/invoices", DraftBody(scene.OrderId, scene.Reference), Key())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Drafting is not updating: the draft permission alone cannot re-price or discard.
        using var drafter = await AdministrationHarness.AdministratorAtBranchAsync(fixture, "inv-drafter", "203.0.113.250", scene.Branch, BillingPermissions.CreateInvoice);
        (await drafter.PutAsync($"/api/v1/billing/invoices/{invoiceId}", RepriceBody(scene, [scene.Jobs[0]]), Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await drafter.PostAsync($"/api/v1/billing/invoices/{invoiceId}/discard", new { reason = "No." }, Tagged(tag))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- the scene -------------------------------------------------------------------------------

    private sealed record Scene(
        AdministrationHarness.AdministratorClient Owner,
        Guid Branch,
        string TaxCode,
        string ItemCode,
        Guid OrderId,
        string OrderNumber,
        Guid CustomerId,
        Guid[] Jobs,
        string Reference,
        PricingResult Result);

    /// <summary>
    /// A branch with a registration and a published price list, a customer of the branch, an order with two
    /// garment jobs known to Billing through the outbox, and the order priced under its reference.
    /// </summary>
    private async Task<Scene> SceneAsync(string prefix, string address, string stem)
    {
        var owner = await AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList, IdentityPermissions.Branches);
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, stem);
        await RegisterAsync(owner, branch);
        var itemCode = Code($"{stem}_STITCH");
        var list = await CreateListAsync(owner, Code($"PL_{stem}"));
        var version = await DraftAsync(owner, list, [branch]);
        await AddItemAsync(owner, version, itemCode, 450m, taxCode);
        await AddItemAsync(owner, version, Code($"{stem}_LINING"), 90m, taxCode, kind: "Surcharge");
        await PublishAsync(owner, version);

        var customerId = await CustomerHarness.CustomerAsync(fixture, branch);
        var orderId = Guid.CreateVersion7();
        var orderNumber = $"O-{RunToken}-{stem[..Math.Min(3, stem.Length)]}";
        Guid[] jobs = [Guid.CreateVersion7(), Guid.CreateVersion7()];

        // The jobs are published before the confirmation on purpose: the projector must cope with either order.
        await PublishAsync(publisher =>
        {
            for (var index = 0; index < jobs.Length; index++)
            {
                publisher.Publish(new GarmentJobCreated(
                    Guid.CreateVersion7(), Now(), jobs[index], SessionTestData.OrganisationId, branch, orderId, $"{orderNumber}-0{index + 1}", index + 1,
                    "blouse", "stitching", Guid.CreateVersion7(), new DateOnly(2026, 9, 20)));
            }

            publisher.Publish(new OrderConfirmed(
                Guid.CreateVersion7(), Now(), orderId, SessionTestData.OrganisationId, branch, customerId, orderNumber, Guid.CreateVersion7(), null,
                new DateOnly(2026, 9, 20), jobs.Length, 1));
        });
        await DispatchAsync();

        using (var scope = fixture.Services.CreateScope())
        {
            var fact = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().OrderFacts.AsNoTracking()
                .Include(order => order.Jobs).SingleAsync(order => order.OrderId == orderId, Token);
            fact.CustomerId.ShouldBe(customerId);
            fact.Jobs.Select(job => job.GarmentJobId).Order().ShouldBe(jobs.Order());
        }

        var reference = $"order:{orderId:N}:1";
        var result = await PriceAsync(branch, reference, [.. jobs.Select(job => job.ToString())], itemCode, Code($"{stem}_LINING"));

        return new Scene(owner, branch, taxCode, itemCode, orderId, orderNumber, customerId, jobs, reference, result);
    }

    private Task<AdministrationHarness.AdministratorClient> CashierAsync(string prefix, string address, Guid branch, params string[] alsoGrant)
        => AdministrationHarness.AdministratorAtBranchAsync(fixture, prefix, address, branch, BillingPermissions.CreateInvoice, [BillingPermissions.UpdateInvoice, .. alsoGrant]);

    private async Task PublishAsync(Action<IOrdersEventPublisher> publish)
    {
        // As Orders does it: into its own outbox, committed by its own save.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        publish(scope.ServiceProvider.GetRequiredService<IOrdersEventPublisher>());
        await context.SaveChangesAsync(Token);
    }

    private async Task DispatchAsync()
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

    private async Task<PricingResult> PriceAsync(Guid branch, string reference, string[] lineKeys, string itemCode, string? surcharge = null)
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

    /// <summary>A snapshot under the reference whose stored result says a figure the engine would not.</summary>
    private async Task StoreTamperedSnapshotAsync(Scene scene, string reference)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICalculationSnapshotStore>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var request = new PricingRequest(
            SessionTestData.OrganisationId, scene.Branch, new DateOnly(2026, 9, 12), "33", reference,
            [.. scene.Jobs.Select(job => new PricingLineRequest(job.ToString(), scene.ItemCode, 1m, [], null, null))]);
        var result = scene.Result with { Totals = scene.Result.Totals with { GrandTotal = Money.Rupees(1m) } };
        store.Add(CalculationSnapshot.Create(
            ids.NewId(), SessionTestData.OrganisationId, scene.Branch, reference, result.PriceListVersionId, result.TaxConfigurationVersionId,
            result.GstRegistrationId, PricingJson.Write(request), PricingJson.Write(result), result.CalculatedAt, null).Value);
        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
    }

    private static async Task Refused(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(status, body);
        body.ShouldContain(code);
    }

    private DateTimeOffset Now()
    {
        using var scope = fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
    }

    private static object DraftBody(Guid orderId, string reference, Guid[]? jobs = null)
        => new { orderId, calculationReference = reference, garmentJobIds = jobs ?? [], reason = (string?)null };

    private static object RepriceBody(Scene scene, Guid[] jobs)
        => new
        {
            on = "2026-09-12",
            placeOfSupplyStateCode = "33",
            lines = jobs.Select(job => new { lineKey = job.ToString(), itemCode = scene.ItemCode, quantity = 1m, surchargeItemCodes = Array.Empty<string>(), discount = (object?)null, @override = (object?)null }).ToArray(),
            reason = (string?)null,
        };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key() => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static (string Name, string Value)[] Tagged(string tag) => [.. Key(), ("If-Match", tag)];

    private static string Code(string stem) => $"{stem}_{RunToken}";

    // ---- configuration through the routes, as the pricing tests do it ----------------------------

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/price-lists/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Tagged(read.Headers.ETag!.ToString())];
    }

    private static async Task<Guid> CreateListAsync(AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync("/api/v1/billing/price-lists", new { code, name = code, reason = (string?)null }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("priceListId").GetGuid();
    }

    private static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, Guid list, Guid[] branches)
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

    private static async Task AddItemAsync(AdministrationHarness.AdministratorClient client, Guid version, string code, decimal rate, string taxCode, string kind = "Service")
    {
        var response = await client.PostAsync(
            $"/api/v1/billing/price-lists/versions/{version}/items",
            new { code, description = "A synthetic item", kind, baseRate = rate, unit = "each", taxCode, active = true, reason = (string?)null },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task PublishAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        var response = await client.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Live." }, await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task RegisterAsync(AdministrationHarness.AdministratorClient client, Guid branch)
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

    private static async Task<string> EnsurePublishedTaxCodeAsync(AdministrationHarness.AdministratorClient client, string stem)
    {
        var code = Code($"TAX_{stem}");
        using var listed = JsonDocument.Parse(await (await client.GetAsync("/api/v1/billing/tax-configuration/versions")).Content.ReadAsStringAsync(Token));
        var live = listed.RootElement.EnumerateArray().FirstOrDefault(row => row.GetProperty("status").GetString() == "Published");
        Guid? cloneFrom = live.ValueKind == JsonValueKind.Object ? live.GetProperty("taxConfigurationVersionId").GetGuid() : null;

        var draft = await client.PostAsync(
            "/api/v1/billing/tax-configuration/versions",
            new { name = $"Tax for invoices {RunToken} {stem}", notes = (string?)null, effectiveFrom = "2026-04-01", cloneFromVersionId = cloneFrom },
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
        return [.. Tagged(read.Headers.ETag!.ToString())];
    }
}
