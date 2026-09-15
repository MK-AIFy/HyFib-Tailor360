using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Billing;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// The order draft lifecycle's own HTTP surface (#199): starting a draft, reading it, and re-pointing its
/// customer or its order-level schedule — the authorisation matrix, a stale tag, an unknown customer, an
/// expired draft, and a retried create.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class OrderDraftEndpointTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACounterStartsReadsAndEditsADraftEndToEnd()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-happy-c", "203.0.113.220");
        var customerId = await CustomerAsync(counter);

        var started = await counter.PostAsync(
            "/api/v1/orders/drafts",
            new { customerId, dueDate = "2026-09-25", notes = "Wants the earlier delivery date." },
            Key());
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(Token));
        started.Headers.ETag.ShouldNotBeNull();
        started.Headers.Location.ShouldNotBeNull();

        using var startedBody = JsonDocument.Parse(await started.Content.ReadAsStringAsync(Token));
        var draftId = startedBody.RootElement.GetProperty("orderDraftId").GetGuid();
        startedBody.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        startedBody.RootElement.GetProperty("dueDate").GetString().ShouldBe("2026-09-25");
        startedBody.RootElement.GetProperty("isOpen").GetBoolean().ShouldBeTrue();
        // An identifier only: no name, no telephone number ever leaves the boundary onto this payload.
        startedBody.RootElement.TryGetProperty("customerName", out _).ShouldBeFalse();

        var read = await counter.GetAsync($"/api/v1/orders/drafts/{draftId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();

        var otherCustomerId = await CustomerAsync(counter);
        var reCustomered = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/customer",
            new { customerId = otherCustomerId },
            await DraftKeyAsync(counter, draftId));
        reCustomered.StatusCode.ShouldBe(HttpStatusCode.OK, await reCustomered.Content.ReadAsStringAsync(Token));
        using var reCustomeredBody = JsonDocument.Parse(await reCustomered.Content.ReadAsStringAsync(Token));
        reCustomeredBody.RootElement.GetProperty("customerId").GetGuid().ShouldBe(otherCustomerId);

        var rescheduled = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/schedule",
            new { dueDate = (string?)null, notes = "Pushed back a week; fabric is delayed." },
            await DraftKeyAsync(counter, draftId));
        rescheduled.StatusCode.ShouldBe(HttpStatusCode.OK, await rescheduled.Content.ReadAsStringAsync(Token));
        using var rescheduledBody = JsonDocument.Parse(await rescheduled.Content.ReadAsStringAsync(Token));
        // The whole value replaces: sending only notes clears the due date rather than leaving it standing.
        rescheduledBody.RootElement.GetProperty("dueDate").ValueKind.ShouldBe(JsonValueKind.Null);
        rescheduledBody.RootElement.GetProperty("notes").GetString()
            .ShouldBe("Pushed back a week; fabric is delayed.");
    }

    [Fact]
    public async Task ACallerWithNoPermissionIsRefusedOnEveryDraftRoute()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-perm-owner", "203.0.113.221");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);

        using var stranger = await AdministrationHarness.AdministratorAsync(
            fixture, "draft-perm-none", "203.0.113.222", grantPermission: null);

        (await stranger.PostAsync("/api/v1/orders/drafts", new { customerId, dueDate = (string?)null, notes = (string?)null }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/orders/drafts/{draftId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PutAsync($"/api/v1/orders/drafts/{draftId}/customer", new { customerId }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ADraftAtAnotherBranchReadsAsNotFound()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-scope-home", "203.0.113.223");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);

        using var owner = await AdministrationHarness.AdministratorAsync(
            fixture, "draft-scope-owner", "203.0.113.224", IdentityPermissions.Branches);
        var otherBranchId = await BillingHarness.OpenBranchAsync(owner);

        using var elsewhere = await AdministrationHarness.AdministratorAtBranchAsync(
            fixture, "draft-scope-other", "203.0.113.225", otherBranchId, OrdersPermissions.Intake);

        // The identifier-editing oracle the authorisation design closes: ScopedToResource answers before
        // the handler is ever reached, so another branch's draft and an unknown identifier both come back
        // as the platform's own 404 rather than the handler's orders.draft-not-found — a caller can never
        // tell "not yours" from "not there" (docs/prd/state-transitions.md section 2.1's "server-side
        // draft shared with every user in the branch" is the other half of this).
        var foreignRead = await elsewhere.GetAsync($"/api/v1/orders/drafts/{draftId}");
        foreignRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(foreignRead)).ShouldBe("security.resource-not-found");

        var unknownRead = await elsewhere.GetAsync($"/api/v1/orders/drafts/{Guid.CreateVersion7()}");
        unknownRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknownRead)).ShouldBe("security.resource-not-found");
    }

    [Fact]
    public async Task AStaleIfMatchIsRefusedAndNothingIsLost()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-stale-c", "203.0.113.226");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var staleKey = await DraftKeyAsync(counter, draftId);

        // A first edit moves the tag.
        (await counter.PutAsync(
                $"/api/v1/orders/drafts/{draftId}/schedule",
                new { dueDate = "2026-09-30", notes = (string?)null },
                await DraftKeyAsync(counter, draftId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var otherCustomerId = await CustomerAsync(counter);
        var stale = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/customer", new { customerId = otherCustomerId }, staleKey);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(stale)).ShouldBe("orders.concurrent-change");

        // The loser gets the current version on the response as well as in the body, so it can offer a
        // merge without a second manual GET — the same shape CustomerEndpoints' merge route answers with.
        stale.Headers.ETag.ShouldNotBeNull();
        using var staleBody = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(Token));
        staleBody.RootElement.GetProperty("currentVersion").GetString()
            .ShouldBe(stale.Headers.ETag!.Tag.Trim('"'));

        // Nothing was lost: the first edit's schedule stands, and the stale customer write never landed.
        using var unchanged = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/orders/drafts/{draftId}")).Content.ReadAsStringAsync(Token));
        unchanged.RootElement.GetProperty("dueDate").GetString().ShouldBe("2026-09-30");
        unchanged.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customerId);
    }

    [Fact]
    public async Task StartingADraftForAnUnknownCustomerIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-cust-c", "203.0.113.227");

        var refused = await counter.PostAsync(
            "/api/v1/orders/drafts",
            new { customerId = Guid.CreateVersion7(), dueDate = (string?)null, notes = (string?)null },
            Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.customer-not-found");
    }

    [Fact]
    public async Task AnExpiredDraftRefusesAnEdit()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-exp-c", "203.0.113.228");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await context.Database.ExecuteSqlAsync(
                $"""
                 UPDATE orders.order_drafts
                    SET started_at = now() - interval '80 hours', expires_at = now() - interval '8 hours'
                  WHERE id = {draftId}
                 """,
                Token);
        }

        // The direct SQL above moves the row's own xmin, so the tag has to be re-read afterwards — a stale
        // tag would report a concurrency conflict before the expiry check is ever reached, which is not
        // what this test is about (CatalogDesignSelectionEndpointTests follows the identical shape).
        var expiredKey = await DraftKeyAsync(counter, draftId);
        var expired = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/schedule",
            new { dueDate = "2026-10-01", notes = (string?)null },
            expiredKey);
        expired.StatusCode.ShouldBe(HttpStatusCode.Conflict, await expired.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(expired)).ShouldBe("orders.draft-expired");
    }

    [Fact]
    public async Task ARetriedStartWithTheSameIdempotencyKeyAndBodyReplaysTheFirstOutcome()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-idem-c", "203.0.113.229");
        var customerId = await CustomerAsync(counter);
        var key = Key();
        var body = new { customerId, dueDate = (string?)null, notes = "Original." };

        var first = await counter.PostAsync("/api/v1/orders/drafts", body, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Token));
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync(Token));
        var draftId = firstBody.RootElement.GetProperty("orderDraftId").GetGuid();

        // The same key on a genuine retry — a lost response after the first attempt already committed —
        // carries the identical body, and replays the first outcome rather than making a second draft.
        var retried = await counter.PostAsync("/api/v1/orders/drafts", body, key);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created, await retried.Content.ReadAsStringAsync(Token));
        using var retriedBody = JsonDocument.Parse(await retried.Content.ReadAsStringAsync(Token));
        retriedBody.RootElement.GetProperty("orderDraftId").GetGuid().ShouldBe(draftId);

        // The same key with a body that has since changed is a defined conflict, not a silent replay of
        // whichever outcome happened to be cached (CLAUDE.md section 4 rule 4).
        var mismatched = await counter.PostAsync(
            "/api/v1/orders/drafts", new { customerId, dueDate = (string?)null, notes = "Different text." }, key);
        mismatched.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private Task<AdministrationHarness.AdministratorClient> CounterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, OrdersPermissions.Intake, CustomersPermissions.Create);

    private static async Task<Guid> CustomerAsync(AdministrationHarness.AdministratorClient counter)
    {
        var response = await counter.PostAsync(
            "/api/v1/customers/",
            new
            {
                displayName = $"Kavitha {AdministrationHarness.UniqueToken(8)}",
                nativeName = (string?)null,
                phone = CustomerHarness.UniquePhone(),
                alternatePhone = (string?)null,
                email = (string?)null,
                addressLine = (string?)null,
                locality = (string?)null,
                postcode = (string?)null,
                language = "en-IN",
                duplicatesReviewed = false,
            },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("customerId").GetGuid();
    }

    private static async Task<Guid> StartDraftAsync(AdministrationHarness.AdministratorClient counter, Guid customerId)
    {
        var response = await counter.PostAsync(
            "/api/v1/orders/drafts", new { customerId, dueDate = (string?)null, notes = (string?)null }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("orderDraftId").GetGuid();
    }

    private static async Task<(string Name, string Value)[]> DraftKeyAsync(
        AdministrationHarness.AdministratorClient counter, Guid draftId)
    {
        using var read = await counter.GetAsync($"/api/v1/orders/drafts/{draftId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
