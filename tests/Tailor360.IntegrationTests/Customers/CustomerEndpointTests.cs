using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The customer record over HTTP: creating one, finding it from another branch, correcting it,
/// withdrawing it, and the refusals that hold the rules in place.
/// </summary>
/// <remarks>
/// These run against a real PostgreSQL because most of what is under test lives in the database — the
/// trigram indexes the counter search reads through, the unique customer number, the check constraints
/// on the telephone columns, and the <c>xmin</c> concurrency token every correction is made against. A
/// substitute would answer every one of them from a dictionary and prove nothing.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CustomerEndpointTests(WebApplicationFixture fixture)
{
    private static readonly Guid FirstBranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000c1");
    private static readonly Guid SecondBranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000c2");

    private const string FirstBranchCode = "CUST1";
    private const string SecondBranchCode = "CUST2";

    [Fact]
    public async Task RegisteringACustomerAllocatesANumberFromTheBranchAndNeverCachesTheAnswer()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-register", "203.0.113.120", FirstBranchId);

        var created = await CreateAsync(counter, Registration("cust-register"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var customer = await ReadCustomerAsync(created);
        customer.CustomerNumber.ShouldStartWith($"C-{FirstBranchCode}-");
        customer.OwningBranchId.ShouldBe(FirstBranchId);
        customer.VisibilityBranchIds.ShouldBe([FirstBranchId]);
        customer.Status.ShouldBe("Active");
        customer.Version.ShouldNotBeNullOrWhiteSpace();

        created.Headers.ETag.ShouldNotBeNull();
        created.Headers.CacheControl?.NoStore.ShouldBeTrue();
        created.Headers.Location.ShouldNotBeNull();
    }

    /// <summary>
    /// The field-level minimisation of <c>data-classification.md</c> section 5.2, asked of the wire
    /// rather than of the projection: one caller holds <c>customers.read_contact</c> and one does not,
    /// and they read the same record.
    /// </summary>
    [Fact]
    public async Task ACallerWithoutReadContactIsNotGivenTheContactFields()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-contact", "203.0.113.121", FirstBranchId);

        var customer = await ReadCustomerAsync(await CreateAsync(counter, Registration("cust-contact")));

        var withContact = await counter.GetAsync($"/api/v1/customers/{customer.CustomerId}");
        var visible = await ReadCustomerAsync(withContact);

        visible.Phone.ShouldNotBeNull();
        visible.Email.ShouldNotBeNull();
        visible.AddressLine.ShouldNotBeNull();

        using var tailor = await CustomerHarness.CounterAsync(
            fixture, "cust-tailor", "203.0.113.122", FirstBranchId, CustomersPermissions.Read);

        var withoutContact = await tailor.GetAsync($"/api/v1/customers/{customer.CustomerId}");
        withoutContact.StatusCode.ShouldBe(HttpStatusCode.OK);

        var minimised = await ReadCustomerAsync(withoutContact);

        // The record itself is readable — a Tailor has to be able to see whose garment this is — and
        // every field that is contact detail comes back empty.
        minimised.DisplayName.ShouldBe(visible.DisplayName);
        minimised.Phone.ShouldBeNull();
        minimised.AlternatePhone.ShouldBeNull();
        minimised.Email.ShouldBeNull();
        minimised.AddressLine.ShouldBeNull();
        minimised.Locality.ShouldBeNull();
        minimised.Postcode.ShouldBeNull();
    }

    /// <summary>
    /// The duplicate refusal: a second attempt on the same telephone number is refused, carries the
    /// candidates that caused the refusal, and is accepted once the caller says they have read them.
    /// </summary>
    [Fact]
    public async Task ASecondRecordOnTheSameNumberIsRefusedUntilTheCandidatesHaveBeenRead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-dupe", "203.0.113.123", FirstBranchId);

        var phone = CustomerHarness.UniquePhone();
        (await CreateAsync(counter, Registration("cust-dupe", phone: phone)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await CreateAsync(counter, Registration("cust-dupe-again", phone: phone));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.duplicates-not-reviewed");

        using var problem = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var candidates = problem.RootElement.GetProperty("candidates");

        candidates.GetArrayLength().ShouldBeGreaterThan(0);

        var first = candidates[0];
        first.GetProperty("confidence").GetString().ShouldBe("High");
        first.GetProperty("reasons").GetArrayLength().ShouldBeGreaterThan(0);

        // A card, not a record: what crosses to the person deciding is a masked number, never the one
        // they typed handed back to them.
        var masked = first.GetProperty("customer").GetProperty("maskedPhone").GetString().ShouldNotBeNull();
        masked.ShouldNotBe(phone);
        masked.ShouldEndWith(phone[^4..]);

        var accepted = await CreateAsync(
            counter, Registration("cust-dupe-again", phone: phone) with { DuplicatesReviewed = true });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// Scenario 2 of <c>branch-scenarios.md</c>, end to end: a customer of the first branch walks into
    /// the second, Reception there finds a masked card, opens the record, and the record is then part
    /// of that branch's ordinary results.
    /// </summary>
    [Fact]
    public async Task ACustomerOfAnotherBranchIsFoundAsAMaskedCardAndBecomesVisibleWhenOpened()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        await CustomerHarness.BranchAsync(fixture, SecondBranchId, SecondBranchCode);

        using var first = await CounterAsync("cust-branch-a", "203.0.113.124", FirstBranchId);

        var phone = CustomerHarness.UniquePhone();
        var customer = await ReadCustomerAsync(
            await CreateAsync(first, Registration("cust-branch-a", phone: phone)));

        using var second = await CounterAsync("cust-branch-b", "203.0.113.125", SecondBranchId);

        var card = (await SearchAsync(second, phone[^6..])).Customers.ShouldHaveSingleItem();

        card.CustomerId.ShouldBe(customer.CustomerId);
        card.OwningBranchId.ShouldBe(FirstBranchId);
        card.VisibleToCaller.ShouldBeFalse();
        card.MaskedPhone.ShouldNotBe(phone);
        card.MaskedPhone.ShouldEndWith(phone[^4..]);

        var opened = await OpenAsync(second, customer.CustomerId);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK);

        var visibility = await ReadCustomerAsync(opened);
        visibility.VisibilityBranchIds.ShouldContain(FirstBranchId);
        visibility.VisibilityBranchIds.ShouldContain(SecondBranchId);

        (await SearchAsync(second, phone[^6..])).Customers.ShouldHaveSingleItem()
            .VisibleToCaller.ShouldBeTrue();

        // Opening a record the branch already sees changes nothing, so it does not write a second entry
        // into the trail. A cross-branch read is worth recording once, not once per screen.
        (await OpenAsync(second, customer.CustomerId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AuditEntriesAsync(customer.CustomerId))
            .Count(entry => entry.Action == "customers.customer.opened-at-branch")
            .ShouldBe(1);
    }

    /// <summary>A corrected name stays findable, because the old one is kept as an alias.</summary>
    [Fact]
    public async Task CorrectingANameKeepsTheOldOneFindable()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-alias", "203.0.113.126", FirstBranchId);

        var token = AdministrationHarness.UniqueToken(6).ToLowerInvariant();
        var maidenName = $"Kavitha {token}";
        var marriedName = $"Kavitha {token} Sundaram";

        var customer = await ReadCustomerAsync(
            await CreateAsync(counter, Registration("cust-alias", displayName: maidenName)));

        var corrected = await CorrectAsync(
            counter,
            customer,
            Correction(customer, displayName: marriedName),
            customer.Version);

        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await ReadCustomerAsync(corrected);
        updated.DisplayName.ShouldBe(marriedName);
        updated.Aliases.ShouldContain(alias => alias.Value == maidenName);
        updated.Version.ShouldNotBe(customer.Version);

        // Both names reach the record: the new one because it is on the record, the old one because it
        // is held as an alias — which is the whole reason an alias is kept.
        (await SearchAsync(counter, maidenName)).Customers
            .ShouldContain(found => found.CustomerId == customer.CustomerId);
        (await SearchAsync(counter, marriedName)).Customers
            .ShouldContain(found => found.CustomerId == customer.CustomerId);
    }

    [Fact]
    public async Task ACorrectionWithNoIfMatchIsRefusedAndOneMadeAgainstAnOldVersionConflicts()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-ifmatch", "203.0.113.127", FirstBranchId);

        var customer = await ReadCustomerAsync(await CreateAsync(counter, Registration("cust-ifmatch")));

        var without = await CorrectAsync(
            counter, customer, Correction(customer, locality: "Gandhipuram"), version: null);

        without.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        var applied = await CorrectAsync(
            counter, customer, Correction(customer, locality: "Gandhipuram"), customer.Version);

        applied.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The same version again, which is now the version before last.
        var stale = await CorrectAsync(
            counter, customer, Correction(customer, locality: "Saibaba Colony"), customer.Version);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(stale)).ShouldBe("customers.version-conflict");

        // The refusal carries the version to retry against, so a client does not need a second round
        // trip to learn what it was looking at was old.
        stale.Headers.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task EveryChangeToTheRecordDemandsAWrittenReason()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-reason", "203.0.113.128", FirstBranchId);

        var customer = await ReadCustomerAsync(await CreateAsync(counter, Registration("cust-reason")));

        var blank = await CorrectAsync(
            counter, customer, Correction(customer) with { Reason = "  " }, customer.Version);

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(blank)).ShouldBe("customers.reason-required");

        var tooShort = await CommandAsync(
            counter, customer.CustomerId, "deactivate", new { reason = "x" }, customer.Version);

        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(tooShort)).ShouldBe("customers.reason-out-of-bounds");
    }

    [Fact]
    public async Task AWithdrawnRecordLeavesOrdinarySearchAndComesBackWhenItIsRestored()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-status", "203.0.113.129", FirstBranchId);

        var phone = CustomerHarness.UniquePhone();
        var customer = await ReadCustomerAsync(
            await CreateAsync(counter, Registration("cust-status", phone: phone)));

        var withdrawn = await CommandAsync(
            counter,
            customer.CustomerId,
            "deactivate",
            new { reason = "Moved out of the city and asked us not to contact her." },
            customer.Version);

        withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterWithdrawal = await ReadCustomerAsync(withdrawn);
        afterWithdrawal.Status.ShouldBe("Deactivated");

        (await SearchAsync(counter, phone[^6..])).Customers.ShouldBeEmpty();
        (await SearchAsync(counter, phone[^6..], includeDeactivated: true)).Customers
            .ShouldContain(found => found.CustomerId == customer.CustomerId);

        // Still readable by identifier: an order placed last year still names the person who placed it.
        (await counter.GetAsync($"/api/v1/customers/{customer.CustomerId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = await CommandAsync(
            counter,
            customer.CustomerId,
            "reactivate",
            new { reason = "Moved back and came in for a blouse; she asked us to use the old record." },
            afterWithdrawal.Version);

        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SearchAsync(counter, phone[^6..])).Customers
            .ShouldContain(found => found.CustomerId == customer.CustomerId);
    }

    [Fact]
    public async Task ASearchTooShortToMeanAnythingReturnsNothingRatherThanEverything()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-short", "203.0.113.130", FirstBranchId);

        (await CreateAsync(counter, Registration("cust-short"))).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await SearchAsync(counter, "ka")).Customers.ShouldBeEmpty();
        (await SearchAsync(counter, string.Empty)).Customers.ShouldBeEmpty();
    }

    /// <summary>
    /// Deny by default, asked of every verb: a signed-in caller holding none of the customer
    /// permissions is refused everywhere, including on the read that would otherwise be the cheapest
    /// way in.
    /// </summary>
    [Fact]
    public async Task ACallerHoldingNoneOfThePermissionsIsRefusedEverywhere()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-owner", "203.0.113.131", FirstBranchId);

        var customer = await ReadCustomerAsync(await CreateAsync(counter, Registration("cust-owner")));

        using var stranger = await CustomerHarness.CounterAsync(
            fixture, "cust-nobody", "203.0.113.132", FirstBranchId);

        (await stranger.GetAsync("/api/v1/customers/?term=kavitha"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/customers/{customer.CustomerId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CreateAsync(stranger, Registration("cust-nobody")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CorrectAsync(stranger, customer, Correction(customer), customer.Version))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CommandAsync(
                stranger,
                customer.CustomerId,
                "deactivate",
                new { reason = "A request that must never reach the handler." },
                customer.Version))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await OpenAsync(stranger, customer.CustomerId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Every state change reaches the trail, and the trail names fields rather than values.</summary>
    [Fact]
    public async Task TheTrailRecordsWhatChangedAndNeverWhatItChangedTo()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);
        using var counter = await CounterAsync("cust-audit", "203.0.113.133", FirstBranchId);

        var phone = CustomerHarness.UniquePhone();
        var name = $"Meena {AdministrationHarness.UniqueToken(6)}";
        var customer = await ReadCustomerAsync(await CreateAsync(
            counter, Registration("cust-audit", displayName: name, phone: phone)));

        (await CorrectAsync(
                counter, customer, Correction(customer, locality: "Race Course"), customer.Version))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await AuditEntriesAsync(customer.CustomerId);

        entries.Select(entry => entry.Action).ShouldBe(
            ["customers.customer.registered", "customers.customer.corrected"], ignoreOrder: true);

        var written = string.Join(
            '\n', entries.Select(entry => $"{entry.Summary}\n{entry.Before}\n{entry.After}"));

        // The name of the field that changed, and nothing that would let a reader of the trail rebuild
        // the person from it (data-classification.md section 5.2).
        written.ShouldContain("locality");
        written.ShouldNotContain(name);
        written.ShouldNotContain(phone.Replace("+", string.Empty, StringComparison.Ordinal));
        written.ShouldNotContain("Race Course");
    }

    // ---- helpers -----------------------------------------------------------------------------

    private Task<AuthenticationClient> CounterAsync(string prefix, string address, Guid branchId)
        => CustomerHarness.CounterAsync(fixture, prefix, address, branchId, CustomerHarness.Reception);

    private static Task<HttpResponseMessage> CreateAsync(
        AuthenticationClient client,
        RegistrationBody registration)
        => client.PostAsync("/api/v1/customers/", registration, Key());

    private static Task<HttpResponseMessage> CorrectAsync(
        AuthenticationClient client,
        CustomerBody customer,
        CorrectionBody correction,
        string? version)
        => client.PutAsync(
            $"/api/v1/customers/{customer.CustomerId}",
            correction,
            version is null ? [Key()] : [Key(), ("If-Match", $"\"{version}\"")]);

    private static Task<HttpResponseMessage> CommandAsync(
        AuthenticationClient client,
        Guid customerId,
        string segment,
        object body,
        string version)
        => client.PostAsync(
            $"/api/v1/customers/{customerId}/{segment}",
            body,
            Key(),
            ("If-Match", $"\"{version}\""));

    private static Task<HttpResponseMessage> OpenAsync(AuthenticationClient client, Guid customerId)
        => client.PostAsync($"/api/v1/customers/{customerId}/open", new { }, Key());

    private static async Task<CustomerBody> ReadCustomerAsync(HttpResponseMessage response)
        => (await AuthenticationClient.ReadAsync<CustomerBody>(response)).ShouldNotBeNull();

    private static async Task<PageBody> SearchAsync(
        AuthenticationClient client,
        string term,
        bool includeDeactivated = false)
    {
        var response = await client.GetAsync(
            $"/api/v1/customers/?term={Uri.EscapeDataString(term)}"
            + $"&includeDeactivated={(includeDeactivated ? "true" : "false")}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        return (await AuthenticationClient.ReadAsync<PageBody>(response)).ShouldNotBeNull();
    }

    private async Task<IReadOnlyList<AuditRow>> AuditEntriesAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return
        [
            .. await context.AuditEvents
                .Where(entry => entry.EntityId == customerId)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new AuditRow(entry.Action, entry.Summary, entry.Before, entry.After))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    /// <summary>A fresh idempotency key, which every command on this surface requires.</summary>
    private static (string Name, string Value) Key()
        => ("Idempotency-Key", Guid.CreateVersion7().ToString());

    private static RegistrationBody Registration(
        string prefix,
        string? displayName = null,
        string? phone = null)
        => new(
            displayName ?? $"Kavitha {prefix}",
            null,
            phone ?? CustomerHarness.UniquePhone(),
            null,
            $"{prefix}@example.invalid",
            "12 Second Street, Demo Nagar",
            "Peelamedu",
            "641004",
            "ta-IN",
            false);

    private static CorrectionBody Correction(
        CustomerBody customer,
        string? displayName = null,
        string? locality = null)
        => new(
            displayName ?? customer.DisplayName,
            customer.NativeName,
            customer.Phone,
            customer.AlternatePhone,
            customer.Email,
            customer.AddressLine,
            locality ?? customer.Locality,
            customer.Postcode,
            customer.Language,
            "Confirmed with the customer at the counter.");

    private sealed record RegistrationBody(
        string? DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string? Language,
        bool DuplicatesReviewed);

    private sealed record CorrectionBody(
        string? DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string? Language,
        string? Reason);

    private sealed record CustomerBody(
        Guid CustomerId,
        string CustomerNumber,
        string DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string Language,
        string Status,
        Guid OwningBranchId,
        IReadOnlyList<Guid> VisibilityBranchIds,
        IReadOnlyList<AliasBody> Aliases,
        string Version);

    private sealed record AliasBody(string Kind, string Value);

    private sealed record CardBody(
        Guid CustomerId,
        string CustomerNumber,
        string DisplayName,
        string MaskedPhone,
        Guid OwningBranchId,
        bool VisibleToCaller,
        string Status);

    private sealed record PageBody(IReadOnlyList<CardBody> Customers, string? NextCursor);

    private sealed record AuditRow(string Action, string Summary, string? Before, string? After);
}
