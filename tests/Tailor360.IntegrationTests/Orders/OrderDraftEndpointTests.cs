using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Billing;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// The order draft lifecycle's own HTTP surface (#199): starting a draft, reading it, re-pointing its
/// customer or its order-level schedule, and its garment sections — the authorisation matrix, a stale tag,
/// an unknown customer, an expired draft, a retried create, the per-section lock, and a reused measurement
/// bound to the customer and the service's template.
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

    [Fact]
    public async Task ACounterAddsSavesDependsOnAndRemovesGarmentSectionsEndToEnd()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-happy-c", "203.0.113.232");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-happy");

        var added = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey, dueDate: "2026-09-25", instructions: "Keep the shoulder loose."),
            Key());
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        added.Headers.ETag.ShouldNotBeNull();
        added.Headers.Location.ShouldNotBeNull();

        using var addedBody = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        var garmentId = addedBody.RootElement.GetProperty("orderDraftGarmentId").GetGuid();
        addedBody.RootElement.GetProperty("categoryKey").GetString().ShouldBe(categoryKey);
        addedBody.RootElement.GetProperty("serviceTypeKey").GetString().ShouldBe(serviceTypeKey);
        // Never trusted from the request: the catalogue version is the one the service is published
        // under, and a template id neither the request nor the test named.
        addedBody.RootElement.GetProperty("catalogVersionId").GetGuid().ShouldNotBe(Guid.Empty);
        addedBody.RootElement.GetProperty("measurementTemplateId").GetGuid().ShouldNotBe(Guid.Empty);
        addedBody.RootElement.GetProperty("position").GetInt32().ShouldBe(1);

        // A second garment, to make the dependency and the removal's cascade genuine.
        var secondAdded = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey),
            Key());
        secondAdded.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var secondBody = JsonDocument.Parse(await secondAdded.Content.ReadAsStringAsync(Token));
        var secondGarmentId = secondBody.RootElement.GetProperty("orderDraftGarmentId").GetGuid();
        var secondTag = ("If-Match", secondAdded.Headers.ETag!.ToString());

        // Reopening the draft is enough to edit any section of it: each carries its own version in the
        // body — the same value its add answered as ETag — so the first edit after a reopen has a
        // precondition to send without a round trip per section, or a deliberate 409 to discover it.
        using var reopened = await counter.GetAsync($"/api/v1/orders/drafts/{draftId}");
        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);
        var draftTagBeforeTheSave = reopened.Headers.ETag!.ToString();
        using var reopenedBody = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync(Token));
        var reopenedSections = reopenedBody.RootElement.GetProperty("garments").EnumerateArray().ToArray();
        reopenedSections.Length.ShouldBe(2);
        reopenedSections[0].GetProperty("version").GetString().ShouldBe(added.Headers.ETag!.Tag.Trim('"'));
        reopenedSections[1].GetProperty("version").GetString().ShouldBe(secondAdded.Headers.ETag!.Tag.Trim('"'));
        var firstVersionFromTheRead = reopenedSections[0].GetProperty("version").GetString();

        var saved = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}",
            GarmentBody(
                categoryKey, serviceTypeKey, dueDate: "2026-09-28", instructions: "Customer asked for a looser fit."),
            [Key()[0], ("If-Match", $"\"{firstVersionFromTheRead}\"")]);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        using var savedBody = JsonDocument.Parse(await saved.Content.ReadAsStringAsync(Token));
        savedBody.RootElement.GetProperty("dueDate").GetString().ShouldBe("2026-09-28");
        savedBody.RootElement.GetProperty("instructions").GetString().ShouldBe("Customer asked for a looser fit.");
        savedBody.RootElement.GetProperty("version").GetString().ShouldBe(saved.Headers.ETag!.Tag.Trim('"'));
        var savedTag = ("If-Match", saved.Headers.ETag!.ToString());

        // The section's row moved; the draft's did not. A counter holding the draft's tag for an
        // order-level edit is not thrown off by a colleague finishing a section.
        using var afterTheSave = await counter.GetAsync($"/api/v1/orders/drafts/{draftId}");
        afterTheSave.Headers.ETag!.ToString().ShouldBe(draftTagBeforeTheSave);
        using var afterTheSaveBody = JsonDocument.Parse(await afterTheSave.Content.ReadAsStringAsync(Token));
        afterTheSaveBody.RootElement.GetProperty("garments").EnumerateArray().First()
            .GetProperty("version").GetString().ShouldBe(saved.Headers.ETag!.Tag.Trim('"'));

        var declared = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{secondGarmentId}/dependencies",
            new
            {
                prerequisiteGarmentId = garmentId,
                kind = "DeliverTogether",
                reason = "The sari blouse goes home with the sari.",
            },
            [Key()[0], secondTag]);
        declared.StatusCode.ShouldBe(HttpStatusCode.OK, await declared.Content.ReadAsStringAsync(Token));
        using var declaredBody = JsonDocument.Parse(await declared.Content.ReadAsStringAsync(Token));
        var dependencies = declaredBody.RootElement.GetProperty("dependencies").EnumerateArray().ToArray();
        dependencies.Length.ShouldBe(1);
        dependencies[0].GetProperty("prerequisiteOrderDraftGarmentId").GetGuid().ShouldBe(garmentId);
        dependencies[0].GetProperty("kind").GetString().ShouldBe("DeliverTogether");
        // Declaring touches the dependent section, moving its own tag even though the dependency table
        // itself carries none.
        declared.Headers.ETag.ShouldNotBeNull();
        declared.Headers.ETag!.ToString().ShouldNotBe(secondTag.Item2);
        var declaredTag = ("If-Match", declared.Headers.ETag!.ToString());

        var withdrawn = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{secondGarmentId}/dependencies/delete",
            new { prerequisiteGarmentId = garmentId, kind = "DeliverTogether" },
            [Key()[0], declaredTag]);
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK, await withdrawn.Content.ReadAsStringAsync(Token));
        using var withdrawnBody = JsonDocument.Parse(await withdrawn.Content.ReadAsStringAsync(Token));
        withdrawnBody.RootElement.GetProperty("dependencies").EnumerateArray().ShouldBeEmpty();
        var withdrawnTag = ("If-Match", withdrawn.Headers.ETag!.ToString());

        // Re-declare it so removal has a dependency to cascade over.
        var redeclared = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{secondGarmentId}/dependencies",
            new { prerequisiteGarmentId = garmentId, kind = "DeliverTogether", reason = (string?)null },
            [Key()[0], withdrawnTag]);
        redeclared.StatusCode.ShouldBe(HttpStatusCode.OK);

        var removed = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}/delete",
            new { },
            [Key()[0], savedTag]);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync(Token));
        using var removedBody = JsonDocument.Parse(await removed.Content.ReadAsStringAsync(Token));
        var remaining = removedBody.RootElement.GetProperty("garments").EnumerateArray().ToArray();
        remaining.Length.ShouldBe(1);
        remaining[0].GetProperty("orderDraftGarmentId").GetGuid().ShouldBe(secondGarmentId);
        // The cascade: the removed section's own dependency went with it, along with the sibling's
        // dependency naming it.
        remaining[0].GetProperty("dependencies").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task AddingAGarmentForAnUnorderableServiceIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-cat-c", "203.0.113.233");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);

        var refused = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody("NO-SUCH-CATEGORY", "NO-SUCH-SERVICE"),
            Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.service-not-orderable-here");
    }

    [Fact]
    public async Task AddingAGarmentThatReusesAnUnknownMeasurementVersionIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-meas-c", "203.0.113.234");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-meas");

        var refused = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(
                categoryKey,
                serviceTypeKey,
                measurementIntent: "ReuseVersion",
                measurementVersionId: Guid.CreateVersion7()),
            Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.measurement-version-not-found");
    }

    [Fact]
    public async Task AGarmentRouteRefusesACallerWithNoPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-perm-owner", "203.0.113.235");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-perm");

        using var stranger = await AdministrationHarness.AdministratorAsync(
            fixture, "draft-garm-perm-none", "203.0.113.236", grantPermission: null);

        (await stranger.PostAsync(
                $"/api/v1/orders/drafts/{draftId}/garments", GarmentBody(categoryKey, serviceTypeKey), Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AStaleIfMatchOnAGarmentIsRefusedWithTheCurrentVersion()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-stale-c", "203.0.113.237");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-stale");

        var added = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments", GarmentBody(categoryKey, serviceTypeKey), Key());
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var addedBody = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        var garmentId = addedBody.RootElement.GetProperty("orderDraftGarmentId").GetGuid();
        var staleTag = ("If-Match", added.Headers.ETag!.ToString());

        // A first save moves the garment's own tag — the draft's tag is untouched by this, which is
        // exactly the per-section lock the precondition proves here.
        (await counter.PutAsync(
                $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}",
                GarmentBody(categoryKey, serviceTypeKey, dueDate: "2026-09-25"),
                [Key()[0], staleTag]))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}",
            GarmentBody(categoryKey, serviceTypeKey, dueDate: "2026-09-30"),
            [Key()[0], staleTag]);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(stale)).ShouldBe("orders.concurrent-change");
        stale.Headers.ETag.ShouldNotBeNull();
        using var staleBody = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(Token));
        staleBody.RootElement.GetProperty("currentVersion").GetString()
            .ShouldBe(stale.Headers.ETag!.Tag.Trim('"'));
    }

    [Fact]
    public async Task AGarmentNotOnTheDraftIsRefusedAsNotFound()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-missing-c", "203.0.113.238");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);

        var refused = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{Guid.CreateVersion7()}",
            GarmentBody("whatever", "whatever"),
            [Key()[0], ("If-Match", "\"1\"")]);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.garment-not-on-draft");
    }

    [Fact]
    public async Task AGarmentReusesTheCustomersOwnMeasurementOfTheServicesTemplate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-reuse-c", "203.0.113.240");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, templateId) = await OrderableServiceAsync("garm-reuse");
        var measurementVersionId = await ConfirmedMeasurementAsync(customerId, templateId);

        var added = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey, measurementIntent: "ReuseVersion", measurementVersionId),
            Key());
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("measurementIntent").GetString().ShouldBe("ReuseVersion");
        body.RootElement.GetProperty("measurementVersionId").GetGuid().ShouldBe(measurementVersionId);
        // The template the measurement answers is the one the service pins — the binding this proves.
        body.RootElement.GetProperty("measurementTemplateId").GetGuid().ShouldBe(templateId);
    }

    [Fact]
    public async Task AGarmentCannotReuseAnotherCustomersMeasurement()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-theirs-c", "203.0.113.241");
        var customerId = await CustomerAsync(counter);
        var somebodyElse = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, templateId) = await OrderableServiceAsync("garm-theirs");
        var theirMeasurement = await ConfirmedMeasurementAsync(somebodyElse, templateId);

        // Right template, wrong person. Answered as not found rather than as "somebody else's": a caller
        // holding orders.intake is not handed a way to tell which measurement identifiers exist.
        var refused = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey, measurementIntent: "ReuseVersion", theirMeasurement),
            Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.measurement-version-not-found");

        using var unchanged = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/orders/drafts/{draftId}")).Content.ReadAsStringAsync(Token));
        unchanged.RootElement.GetProperty("garments").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task AGarmentCannotReuseAMeasurementOfAnotherTemplate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-tmpl-c", "203.0.113.242");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-tmpl");
        var anotherTemplate = await PublishedTemplateAsync("garm-tmpl-other");
        var wrongSheet = await ConfirmedMeasurementAsync(customerId, anotherTemplate);

        // Right person, wrong template: the customer's own measurement, but of something else. A field
        // error, because the measurement is theirs and the counter may well know it — the mistake is
        // which one was picked.
        var refused = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey, measurementIntent: "ReuseVersion", wrongSheet),
            Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.measurement-not-for-service");
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("errors").TryGetProperty("measurementVersionId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task ADraftIsNotRepointedWhileASectionReusesTheOldCustomersMeasurement()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-repoint-c", "203.0.113.243");
        var customerId = await CustomerAsync(counter);
        var reallyFor = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, templateId) = await OrderableServiceAsync("garm-repoint");
        var measurementVersionId = await ConfirmedMeasurementAsync(customerId, templateId);

        var added = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments",
            GarmentBody(categoryKey, serviceTypeKey, measurementIntent: "ReuseVersion", measurementVersionId),
            Key());
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        using var addedBody = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        var garmentId = addedBody.RootElement.GetProperty("orderDraftGarmentId").GetGuid();

        // Re-pointing would carry the first customer's measurement onto the second customer's order. The
        // sections are locked one by one and this route holds only the draft's tag, so it refuses and
        // names the remedy rather than rewriting a section somebody else may have open.
        var refused = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/customer",
            new { customerId = reallyFor },
            await DraftKeyAsync(counter, draftId));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(refused)).ShouldBe("orders.reused-measurements-not-for-customer");

        using (var unchanged = JsonDocument.Parse(
                   await (await counter.GetAsync($"/api/v1/orders/drafts/{draftId}")).Content.ReadAsStringAsync(Token)))
        {
            unchanged.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        }

        // Once the section no longer reuses it, the correction goes through.
        var released = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}",
            GarmentBody(categoryKey, serviceTypeKey, measurementIntent: "TakeLater"),
            [Key()[0], ("If-Match", added.Headers.ETag!.ToString())]);
        released.StatusCode.ShouldBe(HttpStatusCode.OK, await released.Content.ReadAsStringAsync(Token));

        var repointed = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/customer",
            new { customerId = reallyFor },
            await DraftKeyAsync(counter, draftId));
        repointed.StatusCode.ShouldBe(HttpStatusCode.OK, await repointed.Content.ReadAsStringAsync(Token));
        using var repointedBody = JsonDocument.Parse(await repointed.Content.ReadAsStringAsync(Token));
        repointedBody.RootElement.GetProperty("customerId").GetGuid().ShouldBe(reallyFor);
    }

    /// <summary>
    /// The per-section lock at the row level, which no sequence of HTTP requests can show: each request
    /// reloads the draft, so the collision only exists between two callers that both read before either
    /// wrote. Two units of work stand in for the two counters.
    /// </summary>
    [Fact]
    public async Task TwoCountersSavingDifferentSectionsOfOneDraftBothSucceed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-race-c", "203.0.113.244");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey, _) = await OrderableServiceAsync("garm-race");
        var firstGarment = await AddedGarmentAsync(counter, draftId, categoryKey, serviceTypeKey);
        var secondGarment = await AddedGarmentAsync(counter, draftId, categoryKey, serviceTypeKey);
        var organisationId = SessionTestData.OrganisationId;

        using (var one = fixture.Services.CreateScope())
        using (var other = fixture.Services.CreateScope())
        {
            var oneStore = one.ServiceProvider.GetRequiredService<IOrderDraftStore>();
            var otherStore = other.ServiceProvider.GetRequiredService<IOrderDraftStore>();
            var now = one.ServiceProvider.GetRequiredService<IClock>().UtcNow;

            // Both read before either writes — the only way the draft's own row could be the thing in the
            // way, which it was while a section's save also touched the draft.
            var oneDraft = (await oneStore.FindAsync(draftId, organisationId, Token))!;
            var otherDraft = (await otherStore.FindAsync(draftId, organisationId, Token))!;

            oneDraft.SaveGarment(firstGarment, Reworded(oneDraft, firstGarment, "First counter."), now, null)
                .IsSuccess.ShouldBeTrue();
            otherDraft.SaveGarment(secondGarment, Reworded(otherDraft, secondGarment, "Second counter."), now, null)
                .IsSuccess.ShouldBeTrue();

            (await oneStore.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
            var second = await otherStore.SaveAsync(Token);
            second.IsSuccess.ShouldBeTrue(second.IsFailure ? second.Error.Code : null);
        }

        // Neither write was lost.
        using var read = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/orders/drafts/{draftId}")).Content.ReadAsStringAsync(Token));
        var sections = read.RootElement.GetProperty("garments").EnumerateArray()
            .ToDictionary(section => section.GetProperty("orderDraftGarmentId").GetGuid());
        sections[firstGarment].GetProperty("instructions").GetString().ShouldBe("First counter.");
        sections[secondGarment].GetProperty("instructions").GetString().ShouldBe("Second counter.");

        // The same section from two counters is still the race the lock refuses.
        using (var one = fixture.Services.CreateScope())
        using (var other = fixture.Services.CreateScope())
        {
            var oneStore = one.ServiceProvider.GetRequiredService<IOrderDraftStore>();
            var otherStore = other.ServiceProvider.GetRequiredService<IOrderDraftStore>();
            var now = one.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            var oneDraft = (await oneStore.FindAsync(draftId, organisationId, Token))!;
            var otherDraft = (await otherStore.FindAsync(draftId, organisationId, Token))!;

            oneDraft.SaveGarment(firstGarment, Reworded(oneDraft, firstGarment, "Won."), now, null)
                .IsSuccess.ShouldBeTrue();
            otherDraft.SaveGarment(firstGarment, Reworded(otherDraft, firstGarment, "Lost."), now, null)
                .IsSuccess.ShouldBeTrue();

            (await oneStore.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
            var lost = await otherStore.SaveAsync(Token);
            lost.IsFailure.ShouldBeTrue();
            lost.Error.Code.ShouldBe("orders.concurrent-change");
        }
    }

    private static async Task<Guid> AddedGarmentAsync(
        AdministrationHarness.AdministratorClient counter, Guid draftId, string categoryKey, string serviceTypeKey)
    {
        var added = await counter.PostAsync(
            $"/api/v1/orders/drafts/{draftId}/garments", GarmentBody(categoryKey, serviceTypeKey), Key());
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("orderDraftGarmentId").GetGuid();
    }

    /// <summary>The section's content as loaded, with only its instructions changed.</summary>
    private static OrderDraftGarmentContent Reworded(OrderDraft draft, Guid garmentId, string instructions)
    {
        var garment = draft.FindGarment(garmentId)!;

        return OrderDraftGarmentContent.Create(
            garment.CategoryKey,
            garment.ServiceTypeKey,
            garment.CatalogVersionId,
            garment.DesignSelectionDraftId,
            garment.MeasurementIntent,
            garment.MeasurementVersionId,
            garment.MeasurementTemplateId,
            garment.DueDate,
            instructions,
            garment.ReferenceMediaIds).Value;
    }

    /// <summary>
    /// A confirmed measurement of one customer against one template, through the capture handler rather
    /// than over HTTP — a fixture for the reuse tests, not the thing they test, following
    /// <see cref="PublishedTemplateAsync"/>'s own precedent. The consent is not decoration: INV-MSR-05
    /// refuses a confirmation without it.
    /// </summary>
    private async Task<Guid> ConfirmedMeasurementAsync(Guid customerId, Guid templateId)
    {
        await CustomerHarness.SeededPurposesAsync(fixture);
        await CustomerHarness.ConsentAsync(
            fixture,
            customerId,
            ConsentPurposeKeys.MeasurementStorage,
            ConsentDecision.Granted,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using var scope = fixture.Services.CreateScope();
        var capture = scope.ServiceProvider.GetRequiredService<MeasurementCaptureHandler>();
        var organisationId = SessionTestData.OrganisationId;

        var started = await capture.StartAsync(
            new StartMeasurementDraftCommand(
                customerId, templateId, null, organisationId, SessionTestData.HomeBranchId, null),
            Token);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error.Message : null);
        var draftId = started.Value.Draft.Id;

        // The one field the template carries, at a value a tape would actually read.
        var saved = await capture.SaveSectionAsync(
            new SaveMeasurementSectionCommand(
                draftId,
                "Bodice",
                [new CapturedValue("chest_bust", 36m, DisplayUnit.Inch, null, false)],
                organisationId,
                null,
                null),
            Token);
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Error.Message : null);

        var confirmed = await capture.ConfirmAsync(
            new ConfirmMeasurementsCommand(draftId, null, null, organisationId, null, null), Token);
        confirmed.IsSuccess.ShouldBeTrue(confirmed.IsFailure ? confirmed.Error.Message : null);

        return confirmed.Value.Id;
    }

    private static object GarmentBody(
        string? categoryKey,
        string? serviceTypeKey,
        string measurementIntent = "TakeLater",
        Guid? measurementVersionId = null,
        string? dueDate = null,
        string? instructions = null)
        => new
        {
            categoryKey,
            serviceTypeKey,
            designSelectionDraftId = (Guid?)null,
            measurementIntent,
            measurementVersionId,
            dueDate,
            instructions,
            referenceMediaIds = Array.Empty<Guid>(),
        };

    [Fact]
    public async Task ADeactivatedCustomerCannotBeAttachedToNewWork()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await AdministrationHarness.AdministratorAsync(
            fixture, "draft-deact-c", "203.0.113.239",
            OrdersPermissions.Intake, CustomersPermissions.Create, CustomersPermissions.Deactivate);

        var live = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, live);

        var (withdrawnId, withdrawnTag) = await RegisterCustomerAsync(counter);
        var deactivated = await counter.PostAsync(
            $"/api/v1/customers/{withdrawnId}/deactivate",
            new { reason = "Duplicate record; the counter folded it away." },
            [Key()[0], ("If-Match", withdrawnTag)]);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync(Token));

        // CustomerStatus.Deactivated is "not offered when somebody is starting something new" — on both
        // paths that start something new: a fresh draft, and re-pointing an existing one.
        var started = await counter.PostAsync(
            "/api/v1/orders/drafts",
            new { customerId = withdrawnId, dueDate = (string?)null, notes = (string?)null },
            Key());
        started.StatusCode.ShouldBe(HttpStatusCode.Conflict, await started.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(started)).ShouldBe("orders.customer-not-active");

        var repointed = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/customer",
            new { customerId = withdrawnId },
            await DraftKeyAsync(counter, draftId));
        repointed.StatusCode.ShouldBe(HttpStatusCode.Conflict, await repointed.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(repointed)).ShouldBe("orders.customer-not-active");

        // Nothing moved: the draft still names the live customer.
        using var unchanged = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/orders/drafts/{draftId}")).Content.ReadAsStringAsync(Token));
        unchanged.RootElement.GetProperty("customerId").GetGuid().ShouldBe(live);
    }

    private Task<AdministrationHarness.AdministratorClient> CounterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, OrdersPermissions.Intake, CustomersPermissions.Create);

    /// <summary>
    /// Publishes a real category and service type, offered at <see cref="SessionTestData.HomeBranchId"/>
    /// today, and answers the pair a garment section pins against.
    /// </summary>
    /// <remarks>
    /// A real measurement template is created and published too — the catalogue's own publish
    /// validation checks the link (#27) — and is answered alongside the keys, because the reuse tests
    /// need a confirmed measurement against exactly the template the service pins.
    /// </remarks>
    private async Task<(string CategoryKey, string ServiceTypeKey, Guid MeasurementTemplateId)> OrderableServiceAsync(
        string stem)
    {
        // Catalog category and service-type codes are upper snake case — capitals, digits and
        // underscores only — so a hyphenated stem is sanitised before it is used to build one.
        var code = $"{stem.Replace('-', '_').ToUpperInvariant()}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        using var owner = await AdministrationHarness.AdministratorAsync(
            fixture, $"draft-cat-{stem}", "203.0.113.230", CatalogPermissions.Edit, CatalogPermissions.Publish);

        var version = await CatalogVersionAsync(owner, code);
        var categoryKey = $"CAT_{code}";
        var serviceTypeKey = "STITCH";
        var measurementTemplateId = await PublishedTemplateAsync(stem);
        var category = await CatalogCategoryAsync(owner, version, categoryKey);
        await CatalogServiceAsync(owner, version, category, serviceTypeKey, measurementTemplateId);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "Fixture for an order draft integration test." },
            await CatalogVersionKeyAsync(owner, version));
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));

        return (categoryKey, serviceTypeKey, measurementTemplateId);
    }

    private static async Task<Guid> CatalogVersionAsync(AdministrationHarness.AdministratorClient owner, string name)
    {
        var response = await owner.PostAsync("/api/v1/catalog/versions", new { name }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("catalogVersionId").GetGuid();
    }

    private static async Task<Guid> CatalogCategoryAsync(
        AdministrationHarness.AdministratorClient owner, Guid version, string code)
    {
        var response = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories",
            new
            {
                code,
                name = code,
                nameTamil = (string?)null,
                description = "A synthetic category written by an order draft integration test.",
                displayOrder = 0,
                parentId = (Guid?)null,
                branchIds = new[] { SessionTestData.HomeBranchId },
                reason = (string?)null,
            },
            await CatalogVersionKeyAsync(owner, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("categoryId").GetGuid();
    }

    private static async Task<Guid> CatalogServiceAsync(
        AdministrationHarness.AdministratorClient owner,
        Guid version,
        Guid categoryId,
        string code,
        Guid measurementTemplateId)
    {
        var response = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/service-types",
            new
            {
                code,
                name = "Stitching",
                nameTamil = (string?)null,
                description = "A synthetic service type written by an order draft integration test.",
                displayOrder = 0,
                expectedDurationDays = 7,
                intakeWarning = (string?)null,
                measurementTemplateId,
                workflowDefinitionId = Guid.CreateVersion7(),
                designOptionGroupIds = Array.Empty<Guid>(),
                priceListItemCode = "PL-SYNTHETIC",
                qcChecklistTemplateId = Guid.CreateVersion7(),
                allowIncomplete = false,
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                branchIds = new[] { SessionTestData.HomeBranchId },
                reason = (string?)null,
            },
            await CatalogVersionKeyAsync(owner, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("serviceTypeId").GetGuid();
    }

    private static async Task<(string Name, string Value)[]> CatalogVersionKeyAsync(
        AdministrationHarness.AdministratorClient owner, Guid version)
    {
        using var read = await owner.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    /// <summary>
    /// Creates a measurement template with one published version, through the handler rather than over
    /// HTTP — a fixture for this file's tests, not the thing they test, following
    /// <c>CatalogEndpointTests.PublishedTemplateAsync</c>'s own precedent and reasoning.
    /// </summary>
    private async Task<Guid> PublishedTemplateAsync(string stem)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var organisationId = SessionTestData.OrganisationId;
        var code = $"MT_{stem.Replace('-', '_').ToUpperInvariant()}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        var template = await handler.CreateAsync(
            new CreateMeasurementTemplateCommand(organisationId, code, code, null, null), Token);
        template.IsSuccess.ShouldBeTrue();
        var templateId = template.Value.Template.Id;

        var draft = await handler.StartDraftAsync(
            new StartTemplateDraftCommand(
                templateId, organisationId, "Version 1", null, DisplayUnit.Inch, null, null),
            Token);
        draft.IsSuccess.ShouldBeTrue();
        var versionId = draft.Value.Version!.Id;

        var field = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                templateId,
                versionId,
                organisationId,
                null,
                new TemplateFieldDefinition(
                    "chest_bust",
                    "Chest (bust)",
                    null,
                    "Bodice",
                    0,
                    CanonicalUnit.Millimetre,
                    FieldPrecision.Eighths,
                    new ValidationBands(550m, 1500m, 710m, 1270m),
                    true,
                    "Body measurement.",
                    "blouse_front_v1",
                    null,
                    "Body measurement.",
                    null,
                    []),
                null,
                null),
            Token);
        field.IsSuccess.ShouldBeTrue();

        var command = new TemplateLifecycleCommand(templateId, versionId, organisationId, "Fixture.", null, null);
        (await handler.SubmitAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.PublishAsync(command, Token)).IsSuccess.ShouldBeTrue();

        return templateId;
    }

    private static async Task<Guid> CustomerAsync(AdministrationHarness.AdministratorClient counter)
        => (await RegisterCustomerAsync(counter)).Id;

    private static async Task<(Guid Id, string ETag)> RegisterCustomerAsync(
        AdministrationHarness.AdministratorClient counter)
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
        response.Headers.ETag.ShouldNotBeNull();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return (body.RootElement.GetProperty("customerId").GetGuid(), response.Headers.ETag!.ToString());
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
