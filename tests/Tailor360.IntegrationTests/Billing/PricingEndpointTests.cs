using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The pricing engine behind real configuration (#147): the preview route against a draft, the refusals
/// for missing configuration and for an override without the permission, and the contract's snapshot —
/// stored once, reproduced after a later publication, frozen at the database, and audited when the
/// permission was exercised.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class PricingEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task PreviewsADraftAndRefusesAnOverrideBeyondTheThresholdWithoutThePermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pr-preview", "203.0.113.230");
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, "PREVIEW");
        await RegisterAsync(owner, branch);
        var list = await CreateListAsync(owner, Code("PL_PREVIEW"));
        var version = await DraftAsync(owner, list, [branch]);
        await AddItemAsync(owner, version, Code("STITCHING"), 450m, taxCode);
        await AddItemAsync(owner, version, Code("LINING"), 90m, taxCode, kind: "Surcharge");

        // Nothing is published, and the draft still previews: the walkthrough blouse less the piping.
        var preview = await owner.PostAsync("/api/v1/billing/pricing/preview", PreviewBody(version, branch, [Line("g1", Code("STITCHING"), [Code("LINING")])]), Key());
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await preview.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("scheme").GetString().ShouldBe("IntraState");
        var line = body.RootElement.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("taxableValue").GetDecimal().ShouldBe(540m);
        line.GetProperty("taxes").EnumerateArray().Select(tax => (tax.GetProperty("kind").GetString(), tax.GetProperty("amount").GetDecimal()))
            .ShouldBe([("CGST", 13.5m), ("SGST", 13.5m)]);
        body.RootElement.GetProperty("totals").GetProperty("grandTotal").GetDecimal().ShouldBe(567m);
        body.RootElement.GetProperty("priceListVersionId").GetGuid().ShouldBe(version);

        // An override beyond the version's threshold: refused to a holder of the administration permission
        // alone, allowed and marked to a holder of billing.override_price.
        var overridden = Line("g1", Code("STITCHING"), [], @override: new { rate = 580m, reason = "Quoted at the sample rate." });
        var refused = await owner.PostAsync("/api/v1/billing/pricing/preview", PreviewBody(version, branch, [overridden]), Key());
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("billing.approval-required");

        using var approver = await AdministrationHarness.AdministratorAsync(
            fixture, "pr-approver", "203.0.113.231", BillingPermissions.ManagePriceLists, BillingPermissions.OverridePrice);
        var allowed = await approver.PostAsync("/api/v1/billing/pricing/preview", PreviewBody(version, branch, [overridden]), Key());
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync(Token));
        using var allowedBody = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync(Token));
        var approvedLine = allowedBody.RootElement.GetProperty("lines").EnumerateArray().Single();
        approvedLine.GetProperty("approvalExercised").GetBoolean().ShouldBeTrue();
        approvedLine.GetProperty("variance").GetDecimal().ShouldBe(130m);

        // Missing configuration is named: a branch with no registration, an item with no such tax code.
        var unregistered = await BillingHarness.OpenBranchAsync(owner);
        var unregisteredVersion = await DraftAsync(owner, list, [unregistered]);
        await AddItemAsync(owner, unregisteredVersion, Code("STITCHING"), 450m, taxCode);
        var missing = await owner.PostAsync("/api/v1/billing/pricing/preview", PreviewBody(unregisteredVersion, unregistered, [Line("g1", Code("STITCHING"), [])]), Key());
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync(Token)).ShouldContain("billing.configuration-missing");
    }

    [Fact]
    public async Task StoresACalculationUnderItsReferenceAndReproducesItAfterALaterPublication()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pr-snapshot", "203.0.113.232");
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, "SNAPSHOT");
        await RegisterAsync(owner, branch);
        var list = await CreateListAsync(owner, Code("PL_SNAP"));
        var version = await DraftAsync(owner, list, [branch]);
        await AddItemAsync(owner, version, Code("SNAP_ITEM"), 341m, taxCode);
        await PublishAsync(owner, version);

        // Priced through the contract, as an order would, under a reference of the run's own.
        var reference = $"order:{RunToken}:1";
        var first = await PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", reference, [new PricingLineRequest("g1", Code("SNAP_ITEM"), 1m, [], null, null)]));
        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        first.Value.PriceListVersionId.ShouldBe(version);
        first.Value.Totals.GrandTotal.Amount.ShouldBe(358m);

        // A successor at a new rate is published; the reference still answers the stored figure, and a
        // fresh reference answers the new one.
        var successor = await DraftAsync(owner, list, [branch], cloneFrom: version);
        using var read = JsonDocument.Parse(await (await owner.GetAsync($"/api/v1/billing/price-lists/versions/{successor}")).Content.ReadAsStringAsync(Token));
        var itemId = read.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("priceListItemId").GetGuid();
        (await owner.PutAsync($"/api/v1/billing/price-lists/versions/{successor}/items/{itemId}", ItemBody(Code("SNAP_ITEM"), 1000m, taxCode), await VersionKeyAsync(owner, successor)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await PublishAsync(owner, successor);

        var again = await PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", reference, [new PricingLineRequest("g1", Code("SNAP_ITEM"), 1m, [], null, null)]));
        again.Value.PriceListVersionId.ShouldBe(version);
        again.Value.Totals.GrandTotal.Amount.ShouldBe(358m);
        again.Value.CalculatedAt.ShouldBe(first.Value.CalculatedAt);
        var fresh = await PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", $"order:{RunToken}:2", [new PricingLineRequest("g1", Code("SNAP_ITEM"), 1m, [], null, null)]));
        fresh.Value.PriceListVersionId.ShouldBe(successor);
        fresh.Value.Totals.GrandTotal.Amount.ShouldBe(1050m);

        using var scope = fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IPricingService>().FindSnapshotAsync(SessionTestData.OrganisationId, reference, Token))
            .ShouldNotBeNull().Totals.GrandTotal.Amount.ShouldBe(358m);

        // Frozen at the database: neither an update nor a delete gets through.
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.calculation_snapshots SET result = '{{}}'::jsonb WHERE reference = {reference}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM billing.calculation_snapshots WHERE reference = {reference}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task StoresAnAuditedOverrideOnlyForAFreshSessionAndAnswersARaceAndAChangedBodyPlainly()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pr-override", "203.0.113.234");
        var branch = await BillingHarness.OpenBranchAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, "OVERRIDE");
        await RegisterAsync(owner, branch);
        var list = await CreateListAsync(owner, Code("PL_OVR"));
        var version = await DraftAsync(owner, list, [branch]);
        await AddItemAsync(owner, version, Code("OVR_ITEM"), 450m, taxCode);
        await PublishAsync(owner, version);

        using var scope = fixture.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var overridden = new PricingLineRequest("g1", Code("OVR_ITEM"), 1m, [], null, new PricingOverrideRequest(580m, "Quoted at the sample rate."));

        // The permission alone is not enough: a session that re-authenticated an hour ago is refused.
        var stale = Service(scope, new StubUser(BillingPermissions.OverridePrice, clock.UtcNow.AddHours(-1)));
        var refused = await stale.PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", $"order:{RunToken}:stale", [overridden]), Token);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("billing.approval-required");

        // Fresh: approved, stored under the reference, and the variance audited.
        var fresh = Service(scope, new StubUser(BillingPermissions.OverridePrice, clock.UtcNow));
        var reference = $"order:{RunToken}:approved";
        var approved = await fresh.PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", reference, [overridden]), Token);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Error.Message : string.Empty);
        approved.Value.Lines.Single().ApprovalExercised.ShouldBeTrue();
        approved.Value.Lines.Single().Variance.Amount.ShouldBe(130m);
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await using (var connection = new NpgsqlConnection(context.Database.GetConnectionString()))
        {
            await connection.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM platform.audit_events WHERE action = @action AND entity_type = @entity AND reason LIKE @reason",
                connection);
            command.Parameters.AddWithValue("action", PricingService.OverriddenAction);
            command.Parameters.AddWithValue("entity", "billing.calculation_snapshot");
            command.Parameters.AddWithValue("reason", "g1: Quoted at the sample rate.%");
            Convert.ToInt32(await command.ExecuteScalarAsync(Token), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(1);
        }

        // The same reference with a different body is a conflict, not the old figure.
        var changed = await fresh.PriceAsync(new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", reference, [overridden with { Quantity = 2m }]), Token);
        changed.IsFailure.ShouldBeTrue();
        changed.Error.Code.ShouldBe("billing.snapshot-conflict");

        // The race: a rival stored a calculation under the reference between the read and the write. The
        // loser is answered the winner's figure rather than a five hundred or its own.
        var raced = $"order:{RunToken}:raced";
        var request = new PricingRequest(SessionTestData.OrganisationId, branch, new DateOnly(2026, 9, 12), "33", raced, [new PricingLineRequest("g1", Code("OVR_ITEM"), 1m, [], null, null)]);
        var rivalResult = (await fresh.PriceAsync(request with { Reference = null }, Token)).Value with { CalculatedAt = clock.UtcNow.AddMinutes(-1) };
        var store = scope.ServiceProvider.GetRequiredService<ICalculationSnapshotStore>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        store.Add(CalculationSnapshot.Create(
            ids.NewId(), SessionTestData.OrganisationId, branch, raced, rivalResult.PriceListVersionId, rivalResult.TaxConfigurationVersionId,
            rivalResult.GstRegistrationId, PricingJson.Write(request), PricingJson.Write(rivalResult), rivalResult.CalculatedAt, null).Value);
        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
        var racing = new RacingSnapshots(store, () =>
        {
            // Nothing to do: the rival's row is already there, and the store's read is what the service sees.
        });
        var loser = Service(scope, new StubUser(null, null), racing);
        var answered = await loser.PriceAsync(request, Token);
        answered.IsSuccess.ShouldBeTrue();
        answered.Value.CalculatedAt.ShouldBe(rivalResult.CalculatedAt);
        racing.Saves.ShouldBe(1, "the loser tried to write and was refused by the unique index");

        // And a race lost to a rival that stored a different body is the conflict, not the rival's figure.
        var otherRaced = $"order:{RunToken}:raced-differently";
        store.Add(CalculationSnapshot.Create(
            ids.NewId(), SessionTestData.OrganisationId, branch, otherRaced, rivalResult.PriceListVersionId, rivalResult.TaxConfigurationVersionId,
            rivalResult.GstRegistrationId, PricingJson.Write(request with { Reference = otherRaced, Lines = [request.Lines[0] with { Quantity = 3m }] }),
            PricingJson.Write(rivalResult), rivalResult.CalculatedAt, null).Value);
        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
        var lostDifferently = await Service(scope, new StubUser(null, null), new RacingSnapshots(store, () => { })).PriceAsync(request with { Reference = otherRaced }, Token);
        lostDifferently.IsFailure.ShouldBeTrue();
        lostDifferently.Error.Code.ShouldBe("billing.snapshot-conflict");
    }

    [Fact]
    public async Task RefusesThePreviewToAHolderOfNoBillingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var reader = await AdministrationHarness.AdministratorAsync(fixture, "pr-deny", "203.0.113.233", CatalogPermissions.Read);
        var response = await reader.PostAsync("/api/v1/billing/pricing/preview", PreviewBody(Guid.CreateVersion7(), Guid.CreateVersion7(), [Line("g1", "X_Y", [])]), Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The service as the host builds it, with a caller of the test's choosing.</summary>
    private static PricingService Service(IServiceScope scope, ICurrentUser caller, ICalculationSnapshotStore? snapshots = null)
        => new(
            scope.ServiceProvider.GetRequiredService<IPriceListStore>(),
            scope.ServiceProvider.GetRequiredService<ITaxConfigurationStore>(),
            scope.ServiceProvider.GetRequiredService<IGstRegistrationStore>(),
            snapshots ?? scope.ServiceProvider.GetRequiredService<ICalculationSnapshotStore>(),
            caller,
            scope.ServiceProvider.GetRequiredService<IOptions<StepUpOptions>>(),
            scope.ServiceProvider.GetRequiredService<IAuditWriter>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            scope.ServiceProvider.GetRequiredService<IIdGenerator>());

    /// <summary>A signed-in caller holding one permission, whose last strong re-authentication was when the test says.</summary>
    private sealed class StubUser(string? permission, DateTimeOffset? reauthenticatedAt) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();

        public string PrincipalId => UserId.ToString("N");

        public string DisplayName => "A synthetic caller";

        public OrganisationContext Context => new(SessionTestData.OrganisationId, null);

        public IReadOnlySet<Guid> AssignedBranches => new HashSet<Guid>();

        public IReadOnlySet<string> Permissions => permission is null ? new HashSet<string>() : new HashSet<string> { permission };

        public bool MfaSatisfied => true;

        public bool IsSignInComplete => true;

        public DateTimeOffset? LastReauthenticatedAt => reauthenticatedAt;

        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey);

        public bool CanActInBranch(Guid branchId) => true;
    }

    /// <summary>A snapshot store whose first read finds nothing, so the service goes on to write into a race it has already lost.</summary>
    private sealed class RacingSnapshots(ICalculationSnapshotStore inner, Action beforeWrite) : ICalculationSnapshotStore
    {
        private bool _firstRead = true;

        public int Saves { get; private set; }

        public async Task<CalculationSnapshot?> FindAsync(Guid organisationId, string reference, CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;
                return null;
            }

            return await inner.FindAsync(organisationId, reference, cancellationToken);
        }

        public void Add(CalculationSnapshot snapshot)
        {
            beforeWrite();
            inner.Add(snapshot);
        }

        public async Task<Tailor360.Platform.Abstractions.Results.Result> SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return await inner.SaveAsync(cancellationToken);
        }
    }

    private static (string Name, string Value)[] Key() => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList, IdentityPermissions.Branches);

    private async Task<Tailor360.Platform.Abstractions.Results.Result<PricingResult>> PriceAsync(PricingRequest request)
    {
        // Through the contract in a scope of its own, as Orders will call it: no session, so no override permission.
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPricingService>().PriceAsync(request, Token);
    }

    private static object PreviewBody(Guid version, Guid branch, object[] lines)
        => new { priceListVersionId = version, taxConfigurationVersionId = (Guid?)null, branchId = branch, on = "2026-09-12", placeOfSupplyStateCode = "33", lines };

    private static object Line(string key, string item, string[] surcharges, object? discount = null, object? @override = null)
        => new { lineKey = key, itemCode = item, quantity = 1m, surchargeItemCodes = surcharges, discount, @override };

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/price-lists/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static async Task<Guid> CreateListAsync(AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync("/api/v1/billing/price-lists", new { code, name = code, reason = (string?)null }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("priceListId").GetGuid();
    }

    private static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, Guid list, Guid[] branches, Guid? cloneFrom = null)
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
                cloneFromVersionId = cloneFrom,
                reason = (string?)null,
            },
            Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("priceListVersionId").GetGuid();
    }

    private static object ItemBody(string code, decimal rate, string taxCode, string kind = "Service")
        => new { code, description = "A synthetic item", kind, baseRate = rate, unit = "each", taxCode, active = true, reason = (string?)null };

    private static async Task AddItemAsync(AdministrationHarness.AdministratorClient client, Guid version, string code, decimal rate, string taxCode, string kind = "Service")
    {
        var response = await client.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items", ItemBody(code, rate, taxCode, kind), await VersionKeyAsync(client, version));
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

    /// <summary>Publishes a tax configuration version holding one code of this run's own, cloned from whatever is published; returns the code.</summary>
    private static async Task<string> EnsurePublishedTaxCodeAsync(AdministrationHarness.AdministratorClient client, string stem)
    {
        var code = Code($"TAX_{stem}");
        using var listed = JsonDocument.Parse(await (await client.GetAsync("/api/v1/billing/tax-configuration/versions")).Content.ReadAsStringAsync(Token));
        var live = listed.RootElement.EnumerateArray().FirstOrDefault(row => row.GetProperty("status").GetString() == "Published");
        Guid? cloneFrom = live.ValueKind == JsonValueKind.Object ? live.GetProperty("taxConfigurationVersionId").GetGuid() : null;

        var draft = await client.PostAsync(
            "/api/v1/billing/tax-configuration/versions",
            new { name = $"Tax for pricing {RunToken} {stem}", notes = (string?)null, effectiveFrom = "2026-04-01", cloneFromVersionId = cloneFrom },
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
        var publish = await client.PostAsync($"/api/v1/billing/tax-configuration/versions/{version}/publish", new { reason = "For the pricing tests." }, await TaxKeyAsync(client, version));
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
