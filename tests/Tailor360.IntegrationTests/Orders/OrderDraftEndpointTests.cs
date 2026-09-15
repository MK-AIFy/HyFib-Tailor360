using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Billing;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Modules.Orders.Domain.Jobs;
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

    [Fact]
    public async Task ACounterAddsSavesDependsOnAndRemovesGarmentSectionsEndToEnd()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("draft-garm-happy-c", "203.0.113.232");
        var customerId = await CustomerAsync(counter);
        var draftId = await StartDraftAsync(counter, customerId);
        var (categoryKey, serviceTypeKey) = await OrderableServiceAsync("garm-happy");

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

        var saved = await counter.PutAsync(
            $"/api/v1/orders/drafts/{draftId}/garments/{garmentId}",
            GarmentBody(
                categoryKey, serviceTypeKey, dueDate: "2026-09-28", instructions: "Customer asked for a looser fit."),
            [Key()[0], ("If-Match", added.Headers.ETag!.ToString())]);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        using var savedBody = JsonDocument.Parse(await saved.Content.ReadAsStringAsync(Token));
        savedBody.RootElement.GetProperty("dueDate").GetString().ShouldBe("2026-09-28");
        savedBody.RootElement.GetProperty("instructions").GetString().ShouldBe("Customer asked for a looser fit.");
        var savedTag = ("If-Match", saved.Headers.ETag!.ToString());

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
        var (categoryKey, serviceTypeKey) = await OrderableServiceAsync("garm-meas");

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
        var (categoryKey, serviceTypeKey) = await OrderableServiceAsync("garm-perm");

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
        var (categoryKey, serviceTypeKey) = await OrderableServiceAsync("garm-stale");

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

    private Task<AdministrationHarness.AdministratorClient> CounterAsync(string prefix, string address)

        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, OrdersPermissions.Intake, CustomersPermissions.Create);

    /// <summary>
    /// Publishes a real category and service type, offered at <see cref="SessionTestData.HomeBranchId"/>
    /// today, and answers the pair a garment section pins against.
    /// </summary>
    /// <remarks>
    /// A real measurement template is created and published too — the catalogue's own publish
    /// validation checks the link (#27) — but no garment in these tests reuses a confirmed measurement
    /// version, so building one is out of scope here: <c>MeasurementIntent.TakeLater</c> exercises the
    /// catalogue pin, which is what this fixture exists for, without it.
    /// </remarks>
    private async Task<(string CategoryKey, string ServiceTypeKey)> OrderableServiceAsync(string stem)
    {
        // Catalog category and service-type codes are upper snake case — capitals, digits and
        // underscores only — so a hyphenated stem is sanitised before it is used to build one.
        var code = $"{stem.Replace('-', '_').ToUpperInvariant()}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        using var owner = await AdministrationHarness.AdministratorAsync(
            fixture, $"draft-cat-{stem}", "203.0.113.230", CatalogPermissions.Edit, CatalogPermissions.Publish);

        var version = await CatalogVersionAsync(owner, code);
        var categoryKey = $"CAT_{code}";
        var serviceTypeKey = "STITCH";
        var category = await CatalogCategoryAsync(owner, version, categoryKey);
        await CatalogServiceAsync(owner, version, category, serviceTypeKey, code);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "Fixture for an order draft integration test." },
            await CatalogVersionKeyAsync(owner, version));
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));

        return (categoryKey, serviceTypeKey);
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

    private async Task<Guid> CatalogServiceAsync(
        AdministrationHarness.AdministratorClient owner, Guid version, Guid categoryId, string code, string stem)
    {
        var measurementTemplateId = await PublishedTemplateAsync(stem);

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
