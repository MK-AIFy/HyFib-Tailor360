using System.Net;
using System.Text.Json;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The customer timeline over HTTP: what one caller is shown of what happened to one person.
/// </summary>
/// <remarks>
/// <para>
/// These run against a real PostgreSQL because everything under test is a consequence of rows that
/// only exist once a request has been served. The timeline is built from the audit trail, and the audit
/// trail is written by the endpoints — so the arrangement here is deliberately to <em>do</em> things to
/// a customer through the API and then ask the timeline what it saw. A stubbed source would prove that
/// the merge sorts, which the contract tier already proves, and nothing about whether the history is
/// real.
/// </para>
/// <para>
/// The negative cases are the point: a caller without <c>customers.read_consent</c> must not be told
/// that consent was withdrawn, a caller without <c>customers.read_notes</c> must not be shown the free
/// text a colleague typed, and an identifier that names nothing must answer 404 rather than an empty
/// history.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CustomerTimelineEndpointTests(WebApplicationFixture fixture)
{
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000d8");

    private const string BranchCode = "CTML1";

    private static readonly string RunToken = AdministrationHarness.UniqueToken(8);

    [Fact]
    public async Task ShowsWhatHappenedToTheRecordNewestFirst()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-order", "203.0.113.160");

        var customerId = await CreateAsync(reception, "tl-order");
        await CorrectAsync(reception, customerId, "tl-order");

        using var body = await ReadTimelineAsync(reception, customerId);

        var entries = body.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);

        var kinds = entries.EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString())
            .ToArray();

        kinds.ShouldContain("customers.customer.corrected");
        kinds.ShouldContain("customers.customer.registered");

        var instants = entries.EnumerateArray()
            .Select(entry => entry.GetProperty("occurredAt").GetDateTimeOffset())
            .ToArray();

        // Non-increasing, which is the whole of what "newest first" can be asserted as. Deliberately
        // not "the correction is first": two entries sharing an instant are ordered by their
        // identifiers, and a UUIDv7 minted in the same millisecond as another is ordered by its random
        // part rather than by which came first. The two here are separate requests and so are
        // microseconds apart in practice — asserting on that would be asserting on the clock.
        instants.SequenceEqual(instants.OrderByDescending(instant => instant))
            .ShouldBeTrue("the timeline is not in order");

        // The correction is after the registration in time, which is the claim that is actually true.
        var corrected = Entry(body, "customers.customer.corrected").GetProperty("occurredAt").GetDateTimeOffset();
        var registered = Entry(body, "customers.customer.registered").GetProperty("occurredAt").GetDateTimeOffset();

        corrected.ShouldBeGreaterThan(registered);

        // Every entry says which module it came from, so a screen can attribute it and a missing
        // source can be named rather than looking like an absence.
        entries.EnumerateArray().ShouldAllBe(entry => entry.GetProperty("source").GetString() == "customers");

        body.RootElement.GetProperty("unavailableSources").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task WithholdsConsentEntriesFromACallerWithoutReadConsent()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-consent", "203.0.113.161");

        var customerId = await CreateAsync(reception, "tl-consent");
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        (await reception.PostAsync(
                $"/api/v1/customers/{customerId}/consent",
                new { purposeKey = purpose, decision = "Granted", source = "Counter" },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reception holds customers.read_consent, so it sees the entry.
        using var shown = await ReadTimelineAsync(reception, customerId);
        Kinds(shown).ShouldContain("customers.consent.recorded");

        // A caller who may open the record and nothing else does not.
        using var plain = await CustomerHarness.CounterAsync(
            fixture, "tl-plain", "203.0.113.162", BranchId, CustomersPermissions.Read);

        using var hidden = await ReadTimelineAsync(plain, customerId);

        // Withheld whole rather than blanked: the entry's own title says the thing
        // customers.read_consent exists to gate.
        Kinds(hidden).ShouldNotContain("customers.consent.recorded");

        // And the rest of the history is still there — the entry is withheld, not the timeline.
        Kinds(hidden).ShouldContain("customers.customer.registered");
    }

    [Fact]
    public async Task WithholdsTheReasonAnActorGaveFromACallerWithoutReadNotes()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-reason", "203.0.113.163");

        var customerId = await CreateAsync(reception, "tl-reason");
        var reason = "Confirmed the spelling with the customer at the counter.";
        await CorrectAsync(reception, customerId, "tl-reason", reason);

        // Reception does not hold customers.read_notes: the matrix grants it to the Owner and the
        // Branch Manager only. So the counter sees that a reason was given and not what it said.
        using var withheld = await ReadTimelineAsync(reception, customerId);
        var corrected = Entry(withheld, "customers.customer.corrected");

        corrected.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        corrected.GetProperty("reasonPermission").GetString().ShouldBe(CustomersPermissions.ReadNotes);

        // Which is the distinction the field exists for: "a reason you may not read" is not the same
        // answer as "no reason was given", and a screen has to be able to tell them apart.
        using var manager = await CustomerHarness.CounterAsync(
            fixture,
            "tl-notes",
            "203.0.113.164",
            BranchId,
            CustomersPermissions.Read,
            CustomersPermissions.ReadNotes);

        using var shown = await ReadTimelineAsync(manager, customerId);

        Entry(shown, "customers.customer.corrected").GetProperty("reason").GetString().ShouldBe(reason);
    }

    [Fact]
    public async Task CarriesEveryFieldTheApprovedViewDeclaresAndNoOther()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-shape", "203.0.113.165");

        var customerId = await CreateAsync(reception, "tl-shape");

        using var body = await ReadTimelineAsync(reception, customerId);

        Keys(body.RootElement).ShouldBe(["entries", "nextCursor", "unavailableSources"], ignoreOrder: true);

        var declared = new Tailor360.Platform.Security.FieldVisibility.ResponseViewCatalogue(
                [new Tailor360.Platform.Security.FieldVisibility.ApplicationResponseViews()])
            .Require(Tailor360.Platform.Security.FieldVisibility.CustomersResponseViews.Timeline)
            .Fields.Select(field => field.Name)
            .ToArray();

        foreach (var entry in body.RootElement.GetProperty("entries").EnumerateArray())
        {
            Keys(entry).ShouldBe(declared, ignoreOrder: true);
        }
    }

    /// <summary>
    /// The first test in this repository that actually pages a cursor past the first page.
    /// </summary>
    /// <remarks>
    /// The customer search has had a cursor since #26's first slice and no test has ever sent one back,
    /// so "the cursor works" has been an assumption. A timeline is the natural place to stop assuming:
    /// it is the endpoint most likely to have more entries than a page, and the merge makes its cursor
    /// the more delicate of the two.
    /// </remarks>
    [Fact]
    public async Task PagesThroughTheHistoryWithoutRepeatingOrSkippingAnEntry()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-page", "203.0.113.166");

        var customerId = await CreateAsync(reception, "tl-page");

        for (var change = 0; change < 4; change++)
        {
            await CorrectAsync(reception, customerId, $"tl-page-{change}");
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            using var page = await ReadTimelineAsync(reception, customerId, cursor, limit: 2);

            var entries = page.RootElement.GetProperty("entries");
            entries.GetArrayLength().ShouldBeLessThanOrEqualTo(2);

            seen.AddRange(entries.EnumerateArray().Select(entry => entry.GetProperty("entryId").GetGuid()));

            cursor = page.RootElement.GetProperty("nextCursor").ValueKind is JsonValueKind.Null
                ? null
                : page.RootElement.GetProperty("nextCursor").GetString();

            pages++;
        }
        while (cursor is not null && pages < 10);

        cursor.ShouldBeNull("the paging never reached the end");
        pages.ShouldBeGreaterThan(1, "the history was not long enough to page through");

        seen.Count.ShouldBe(5, "one registration and four corrections");
        seen.Distinct().Count().ShouldBe(seen.Count, "an entry was returned on two pages");
    }

    [Fact]
    public async Task AnswersAnIdentifierThatNamesNoCustomerWithNotFound()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-missing", "203.0.113.167");

        var response = await reception.GetAsync($"/api/v1/customers/{Guid.CreateVersion7()}/timeline");

        // Not an empty timeline. A customer nothing has happened to and a customer who does not exist
        // are different answers, and conflating them makes the first look like a defect.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("customers.customer-not-found");
    }

    [Fact]
    public async Task RefusesACallerWhoMayNotOpenACustomerAtAll()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-denied", "203.0.113.168");

        var customerId = await CreateAsync(reception, "tl-denied");

        using var tailor = await CustomerHarness.CounterAsync(
            fixture, "tl-tailor", "203.0.113.169", BranchId);

        var response = await tailor.GetAsync($"/api/v1/customers/{customerId}/timeline");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NeverCachesACustomersHistory()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("tl-cache", "203.0.113.170");

        var customerId = await CreateAsync(reception, "tl-cache");

        var response = await reception.GetAsync($"/api/v1/customers/{customerId}/timeline");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    private static string[] Kinds(JsonDocument body)
        =>
        [
            .. body.RootElement.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("kind").GetString() ?? string.Empty),
        ];

    private static JsonElement Entry(JsonDocument body, string kind)
        => body.RootElement.GetProperty("entries").EnumerateArray()
            .First(entry => entry.GetProperty("kind").GetString() == kind);

    private static string[] Keys(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];

    private Task<AuthenticationClient> ReceptionAsync(string prefix, string address)
        => CustomerHarness.CounterAsync(fixture, prefix, address, BranchId, CustomerHarness.Reception);

    private static async Task<JsonDocument> ReadTimelineAsync(
        AuthenticationClient client,
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

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static (string Name, string Value) Key()
        => ("Idempotency-Key", Guid.CreateVersion7().ToString());

    private static async Task<Guid> CreateAsync(AuthenticationClient client, string prefix)
    {
        var response = await client.PostAsync(
            "/api/v1/customers/",
            new
            {
                displayName = $"Kavitha {prefix} {RunToken}",
                nativeName = (string?)null,
                phone = CustomerHarness.UniquePhone(),
                alternatePhone = (string?)null,
                email = $"{prefix}@example.invalid",
                addressLine = "12 Second Street, Demo Nagar",
                locality = $"Peelamedu {prefix}",
                postcode = "641004",
                language = "ta-IN",
                duplicatesReviewed = true,
            },
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return created.RootElement.GetProperty("customerId").GetGuid();
    }

    private static async Task CorrectAsync(
        AuthenticationClient client,
        Guid customerId,
        string prefix,
        string? reason = null)
    {
        var current = await client.GetAsync($"/api/v1/customers/{customerId}");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(
            await current.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var version = body.RootElement.GetProperty("version").GetString().ShouldNotBeNull();

        var response = await client.PutAsync(
            $"/api/v1/customers/{customerId}",
            new
            {
                displayName = $"Kavitha {prefix} {RunToken}",
                nativeName = (string?)null,
                phone = body.RootElement.GetProperty("phone").GetString(),
                alternatePhone = (string?)null,
                email = body.RootElement.GetProperty("email").GetString(),
                addressLine = body.RootElement.GetProperty("addressLine").GetString(),
                locality = body.RootElement.GetProperty("locality").GetString(),
                postcode = body.RootElement.GetProperty("postcode").GetString(),
                language = "ta-IN",
                reason = reason ?? "Confirmed with the customer at the counter.",
            },
            [Key(), ("If-Match", $"\"{version}\"")]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
