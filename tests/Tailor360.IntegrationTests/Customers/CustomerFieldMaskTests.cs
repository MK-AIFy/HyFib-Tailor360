using System.Net;
using System.Text.Json;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The declared response views, asked of the wire: what a customer endpoint actually sends, key by
/// key, to callers holding different things.
/// </summary>
/// <remarks>
/// <para>
/// The unit tier already holds the approved tables equal to the code, and the contract tier holds the
/// payload types equal to the views. Neither of those asks the running application anything. These do:
/// they read the JSON a real signed-in caller receives from a real PostgreSQL and compare its keys
/// with the catalogue, so that a projection which quietly stopped consulting the mask — or a
/// serialiser setting that renamed a field — is caught by the thing it would actually break.
/// </para>
/// <para>
/// They assert over raw JSON rather than a typed body on purpose. Deserialising into a record would
/// answer "was the value there" and silently supply a default for a key that never arrived, which is
/// exactly the distinction the mask exists to make.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CustomerFieldMaskTests(WebApplicationFixture fixture)
{
    /// <summary>
    /// This class's own branch. Not shared: <c>d1</c> already belongs to <c>ModuleOutboxTests</c> and
    /// <c>IdentifierEditingTests</c>, and two classes opening one branch in a suite that runs in
    /// parallel against one database is how a test starts depending on which of them ran first.
    /// </summary>
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000d7");

    private const string BranchCode = "CMSK1";

    private static readonly ResponseViewCatalogue Catalogue = new([new ApplicationResponseViews()]);

    /// <summary>One token per run, so a second run does not resemble the first strongly enough to be refused.</summary>
    private static readonly string RunToken = AdministrationHarness.UniqueToken(8);

    [Fact]
    public async Task TheRecordCarriesEveryFieldTheViewDeclaresAndNoOther()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-shape", "203.0.113.140");

        var customerId = await CreateAsync(reception, "mask-shape");

        using var body = await ReadJsonAsync(reception, $"/api/v1/customers/{customerId}");

        // Every key, against the approved list. Not a subset in either direction: a field the document
        // approves and the response omits is as much a defect as one nobody approved arriving.
        Keys(body.RootElement).ShouldBe(Declared(CustomersResponseViews.Record), ignoreOrder: true);
    }

    [Fact]
    public async Task TheSearchCardCarriesEveryFieldTheViewDeclaresAndNoOther()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-card", "203.0.113.141");

        var name = $"Kavitha mask-card {RunToken}";
        await CreateAsync(reception, "mask-card", displayName: name);

        using var page = await ReadJsonAsync(
            reception, $"/api/v1/customers/?term={Uri.EscapeDataString(name)}");

        var cards = page.RootElement.GetProperty("customers");
        cards.GetArrayLength().ShouldBeGreaterThan(0);

        foreach (var card in cards.EnumerateArray())
        {
            Keys(card).ShouldBe(Declared(CustomersResponseViews.SearchCard), ignoreOrder: true);
        }
    }

    /// <summary>
    /// The behaviour the split exists for, over the wire and from both sides at once.
    /// </summary>
    [Fact]
    public async Task WithholdsEveryContactFieldFromACallerWhoHoldsOnlyCustomersRead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-contact", "203.0.113.142");

        var customerId = await CreateAsync(reception, "mask-contact");

        using var seer = await CustomerHarness.CounterAsync(
            fixture, "mask-blind", "203.0.113.143", BranchId, CustomersPermissions.Read);

        using var shown = await ReadJsonAsync(reception, $"/api/v1/customers/{customerId}");
        using var hidden = await ReadJsonAsync(seer, $"/api/v1/customers/{customerId}");

        var contact = ContactFields();
        contact.Length.ShouldBe(6, "the approved view declares six contact fields");

        foreach (var field in contact)
        {
            shown.RootElement.GetProperty(field).ValueKind.ShouldNotBe(
                JsonValueKind.Null, $"{field} should have been sent to a caller holding read_contact");

            hidden.RootElement.GetProperty(field).ValueKind.ShouldBe(
                JsonValueKind.Null, $"{field} reached a caller holding no read_contact");
        }

        // The record itself is still readable, which is the whole point of splitting the permission.
        hidden.RootElement.GetProperty("displayName").GetString()
            .ShouldBe(shown.RootElement.GetProperty("displayName").GetString());

        // And the key is present rather than absent, because the schema promises it. That is why the
        // discriminator has to exist: without it these two nulls read alike.
        shown.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeTrue();
        hidden.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A withheld number and a number the customer never gave are both null, and the response says
    /// which is which.
    /// </summary>
    [Fact]
    public async Task TellsAWithheldFieldApartFromOneTheCustomerNeverGave()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-absent", "203.0.113.144");

        // No alternate number and no email on this record: the customer gave neither.
        var customerId = await CreateAsync(
            reception, "mask-absent", email: null, alternatePhone: null);

        using var permitted = await ReadJsonAsync(reception, $"/api/v1/customers/{customerId}");

        permitted.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeTrue();
        permitted.RootElement.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
        permitted.RootElement.GetProperty("alternatePhone").ValueKind.ShouldBe(JsonValueKind.Null);

        // Same two nulls, opposite meaning. A client that shows "no email on file" must be able to
        // tell them apart, and contactIncluded is the only thing that lets it.
        using var refused = await CustomerHarness.CounterAsync(
            fixture, "mask-absent-b", "203.0.113.145", BranchId, CustomersPermissions.Read);

        using var withheld = await ReadJsonAsync(refused, $"/api/v1/customers/{customerId}");

        withheld.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeFalse();
        withheld.RootElement.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.RootElement.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The write paths answer with the same projection as the read path.
    /// </summary>
    /// <remarks>
    /// They are the ones that would drift: a correction, a deactivation and a reactivation all return
    /// the record through one shared helper, and a caller who may change a record but not read its
    /// contact details would otherwise be handed the contact details back in the reply to their own
    /// edit — which is the oldest way a masked field leaks.
    /// </remarks>
    [Fact]
    public async Task AnswersACorrectionWithTheSameMaskAsARead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-write", "203.0.113.146");

        var customerId = await CreateAsync(reception, "mask-write");

        using var editor = await CustomerHarness.CounterAsync(
            fixture,
            "mask-editor",
            "203.0.113.147",
            BranchId,
            CustomersPermissions.Read,
            CustomersPermissions.Update);

        using var current = await ReadJsonAsync(editor, $"/api/v1/customers/{customerId}");
        var version = current.RootElement.GetProperty("version").GetString().ShouldNotBeNull();

        var correction = new
        {
            displayName = $"Kavitha mask-write {RunToken} corrected",
            nativeName = (string?)null,

            // The editor cannot read the contact fields, so it cannot resend them; the correction
            // clears what it cannot see, which is a separate product question and not this test's.
            phone = CustomerHarness.UniquePhone(),
            alternatePhone = (string?)null,
            email = (string?)null,
            addressLine = (string?)null,
            locality = (string?)null,
            postcode = (string?)null,
            language = "ta-IN",
            reason = "Confirmed with the customer at the counter.",
        };

        var response = await editor.PutAsync(
            $"/api/v1/customers/{customerId}",
            correction,
            ("Idempotency-Key", Guid.CreateVersion7().ToString()),
            ("If-Match", $"\"{version}\""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var corrected = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Keys(corrected.RootElement).ShouldBe(Declared(CustomersResponseViews.Record), ignoreOrder: true);

        corrected.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeFalse();
        corrected.RootElement.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The status commands answer through the same helper as the correction, and the merge answers
    /// through its own path with the record nested inside the merge receipt.
    /// </summary>
    /// <remarks>
    /// All three demand a permission that is not the record view's own — <c>customers.deactivate</c>
    /// and <c>customers.merge</c> — so all three ask for their mask through <c>MaskForReached</c>.
    /// A mistake there does not fail quietly: an ordinary mask would be empty and the projection
    /// refuses to build a body from one, so the endpoint would answer 500 rather than leak. This
    /// asserts the answer that should come back instead.
    /// </remarks>
    [Fact]
    public async Task AnswersADeactivationWithTheSameMaskAsARead()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);
        using var reception = await ReceptionAsync("mask-status", "203.0.113.148");

        var customerId = await CreateAsync(reception, "mask-status");

        using var closer = await CustomerHarness.CounterAsync(
            fixture,
            "mask-closer",
            "203.0.113.149",
            BranchId,
            CustomersPermissions.Read,
            CustomersPermissions.Deactivate);

        using var current = await ReadJsonAsync(closer, $"/api/v1/customers/{customerId}");
        var version = current.RootElement.GetProperty("version").GetString().ShouldNotBeNull();

        var response = await closer.PostAsync(
            $"/api/v1/customers/{customerId}/deactivate",
            new { reason = "The customer asked for the record to be closed." },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()),
            ("If-Match", $"\"{version}\""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deactivated = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Keys(deactivated.RootElement).ShouldBe(Declared(CustomersResponseViews.Record), ignoreOrder: true);

        deactivated.RootElement.GetProperty("status").GetString().ShouldBe("Deactivated");
        deactivated.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeFalse();
        deactivated.RootElement.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Registration answers with the record it just created, to a caller who may create and read but
    /// not read contact details.
    /// </summary>
    [Fact]
    public async Task AnswersARegistrationWithTheRecordItCreatedAndStillWithholdsContact()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        using var clerk = await CustomerHarness.CounterAsync(
            fixture,
            "mask-clerk",
            "203.0.113.150",
            BranchId,
            CustomersPermissions.Read,
            CustomersPermissions.Create);

        var response = await clerk.PostAsync(
            "/api/v1/customers/",
            new
            {
                displayName = $"Kavitha mask-clerk {RunToken}",
                nativeName = (string?)null,
                phone = CustomerHarness.UniquePhone(),
                alternatePhone = CustomerHarness.UniquePhone(),
                email = "clerk@example.invalid",
                addressLine = "12 Second Street, Demo Nagar",
                locality = "Peelamedu mask-clerk",
                postcode = "641004",
                language = "ta-IN",
                duplicatesReviewed = true,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The body is the record, not an empty shell: the caller was authorised to create it, and the
        // permission that authorised them is what stands in for the view's own.
        Keys(created.RootElement).ShouldBe(Declared(CustomersResponseViews.Record), ignoreOrder: true);
        created.RootElement.GetProperty("customerNumber").GetString().ShouldNotBeNullOrWhiteSpace();

        // And the field gates still apply, to the very values this caller just sent.
        created.RootElement.GetProperty("contactIncluded").GetBoolean().ShouldBeFalse();
        created.RootElement.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        created.RootElement.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static string[] Declared(string viewKey)
        => [.. Catalogue.Require(viewKey).Fields.Select(field => field.Name)];

    private static string[] ContactFields()
        =>
        [
            .. Catalogue.Require(CustomersResponseViews.Record).Fields
                .Where(field => field.Classification.HasFlag(FieldClassification.CustomerContact))
                .Select(field => field.Name),
        ];

    private static string[] Keys(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];

    private Task<AuthenticationClient> ReceptionAsync(string prefix, string address)
        => CustomerHarness.CounterAsync(fixture, prefix, address, BranchId, CustomerHarness.Reception);

    private static async Task<JsonDocument> ReadJsonAsync(AuthenticationClient client, string route)
    {
        var response = await client.GetAsync(route);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, route);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <param name="alternatePhone">
    /// Given by default, so that a test asserting "every contact field came back" is asserting about
    /// six values the customer actually gave. Pass null for the record that gave neither an alternate
    /// number nor an email — which is the case that makes the discriminator necessary.
    /// </param>
    private static async Task<Guid> CreateAsync(
        AuthenticationClient client,
        string prefix,
        string? displayName = null,
        string? email = "counter@example.invalid",
        string? alternatePhone = "")
    {
        var registration = new
        {
            displayName = displayName ?? $"Kavitha {prefix} {RunToken}",
            nativeName = (string?)null,
            phone = CustomerHarness.UniquePhone(),
            alternatePhone = alternatePhone is "" ? CustomerHarness.UniquePhone() : alternatePhone,
            email,
            addressLine = "12 Second Street, Demo Nagar",
            locality = $"Peelamedu {prefix}",
            postcode = "641004",
            language = "ta-IN",
            duplicatesReviewed = true,
        };

        var response = await client.PostAsync(
            "/api/v1/customers/",
            registration,
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return created.RootElement.GetProperty("customerId").GetGuid();
    }
}
