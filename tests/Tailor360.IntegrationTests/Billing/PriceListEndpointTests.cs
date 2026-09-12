using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The price-list routes (#146) end to end: the round trip, publication and what it freezes at the
/// database, the checks against the published tax configuration, a branch priced twice (by the checks
/// and by the database), the catalogue link through both modules' publish commands, the tag every
/// command demands, the two creation races, and deny-by-default over every route.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class PriceListEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();
    private static int _preconditionClientNumber;

    [Fact]
    public async Task AnAdministratorCreatesAListDraftsAVersionAddsItemsAndRulesAndReadsThemBack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-add", "203.0.113.250");
        var branch = await BranchAsync(owner);
        var list = await CreateListAsync(owner, Code("PL_ADD"));
        var version = await DraftAsync(owner, list, "Round trip", [branch]);

        // A branch Identity does not know is refused before anything is written.
        var phantom = await owner.PostAsync($"/api/v1/billing/price-lists/{list}/versions", VersionBody("Phantom", [Guid.CreateVersion7()]), Key());
        phantom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await phantom.Content.ReadAsStringAsync(Token)).ShouldContain("billing.branch-not-found");

        var item = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items", ItemBody(Code("STITCHING")), await VersionKeyAsync(owner, version));
        item.StatusCode.ShouldBe(HttpStatusCode.Created, await item.Content.ReadAsStringAsync(Token));
        using var itemBody = JsonDocument.Parse(await item.Content.ReadAsStringAsync(Token));
        var itemId = itemBody.RootElement.GetProperty("priceListItemId").GetGuid();
        itemBody.RootElement.GetProperty("baseRate").GetDecimal().ShouldBe(450m);

        // The write moved the version, and the response carries the tag the next write must send: usable as is.
        item.Headers.ETag.ShouldNotBeNull();
        var edited = await owner.PutAsync(
            $"/api/v1/billing/price-lists/versions/{version}/items/{itemId}", ItemBody(Code("STITCHING"), rate: 472.5m), [.. Key(), ("If-Match", item.Headers.ETag.ToString())]);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Token));
        edited.Headers.ETag.ShouldNotBeNull();

        var rule = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/discount-rules", RuleBody(Code("FESTIVAL")), await VersionKeyAsync(owner, version));
        rule.StatusCode.ShouldBe(HttpStatusCode.Created, await rule.Content.ReadAsStringAsync(Token));
        using var ruleBody = JsonDocument.Parse(await rule.Content.ReadAsStringAsync(Token));
        var ruleId = ruleBody.RootElement.GetProperty("discountRuleId").GetGuid();

        using var read = JsonDocument.Parse(await (await owner.GetAsync($"/api/v1/billing/price-lists/versions/{version}")).Content.ReadAsStringAsync(Token));
        read.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("baseRate").GetDecimal().ShouldBe(472.5m);
        read.RootElement.GetProperty("discountRules").EnumerateArray().Single().GetProperty("maximum").GetDecimal().ShouldBe(15m);
        read.RootElement.GetProperty("version").GetProperty("roundOff").GetString().ShouldBe("NearestRupee");

        using var lists = JsonDocument.Parse(await (await owner.GetAsync("/api/v1/billing/price-lists")).Content.ReadAsStringAsync(Token));
        lists.RootElement.EnumerateArray().ShouldContain(row => row.GetProperty("priceListId").GetGuid() == list);
        using var versions = JsonDocument.Parse(await (await owner.GetAsync($"/api/v1/billing/price-lists/{list}/versions")).Content.ReadAsStringAsync(Token));
        var summary = versions.RootElement.EnumerateArray().Single();
        summary.GetProperty("priceListVersionId").GetGuid().ShouldBe(version);
        summary.GetProperty("branchIds").EnumerateArray().Single().GetGuid().ShouldBe(branch);

        // A money-bearing flag left out of the body is refused, never defaulted.
        var untagged = await owner.PostAsync(
            $"/api/v1/billing/price-lists/versions/{version}/items",
            new { code = Code("NO_FLAG"), description = "x", kind = "Service", baseRate = 1m, unit = "each", taxCode = "STITCHING_5", reason = (string?)null },
            await VersionKeyAsync(owner, version));
        untagged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await untagged.Content.ReadAsStringAsync(Token)).ShouldContain("\"active\"");
        var unsaid = await owner.PostAsync(
            $"/api/v1/billing/price-lists/{list}/versions",
            new { name = "No flag", effectiveFrom = "2026-04-01", roundOff = "NearestRupee", overrideThresholdPercent = 10m, branchIds = new[] { branch } },
            Key());
        unsaid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unsaid.Content.ReadAsStringAsync(Token)).ShouldContain("\"taxInclusive\"");

        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/discount-rules/{ruleId}/delete", new { reason = "Withdrawn." }, await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items/{itemId}/delete", new { reason = "Withdrawn." }, await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var renamed = await owner.GetAsync($"/api/v1/billing/price-lists/{list}");
        var rename = await owner.PutAsync($"/api/v1/billing/price-lists/{list}", new { name = "Renamed", reason = "Test." }, [.. Key(), ("If-Match", renamed.Headers.ETag!.ToString())]);
        rename.StatusCode.ShouldBe(HttpStatusCode.OK, await rename.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task PublicationNeedsAPublishedTaxCodeAndFreezesTheVersionAtTheDatabase()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-publish", "203.0.113.251");
        var branch = await BranchAsync(owner);
        var list = await CreateListAsync(owner, Code("PL_PUB"));
        var version = await DraftAsync(owner, list, "Publication", [branch]);
        var unknown = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items", ItemBody(Code("UNKNOWN_TAX"), taxCode: Code("NOBODY")), await VersionKeyAsync(owner, version));
        unknown.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(Token));
        var itemId = unknownBody.RootElement.GetProperty("priceListItemId").GetGuid();

        // No tax code by that name is published: refused with the finding, and the report says so too.
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, "PUB");
        var refused = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Trying." }, await VersionKeyAsync(owner, version));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("billing.tax-code-unknown");

        (await owner.PutAsync($"/api/v1/billing/price-lists/versions/{version}/items/{itemId}", ItemBody(Code("UNKNOWN_TAX"), taxCode: taxCode), await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Approved." }, await VersionKeyAsync(owner, version));
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));

        // Frozen by the API and by the database — including moving a row into a draft.
        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items", ItemBody(Code("LATE")), await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.price_list_items SET base_rate = 1 WHERE id = {itemId}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM billing.price_list_version_branches WHERE price_list_version_id = {version}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        var scratch = await DraftAsync(owner, list, "Scratch", [branch]);
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.price_list_items SET price_list_version_id = {scratch} WHERE id = {itemId}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        (await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.price_list_versions SET status = 0, published_at = NULL WHERE id = {version}", Token)))
            .SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // A second list claiming the same branch is refused; the same list's clone supersedes cleanly.
        var rival = await CreateListAsync(owner, Code("PL_RIVAL"));
        var rivalVersion = await DraftAsync(owner, rival, "Rival", [branch]);
        var twice = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{rivalVersion}/publish", new { reason = "Trying." }, await VersionKeyAsync(owner, rivalVersion));
        twice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await twice.Content.ReadAsStringAsync(Token)).ShouldContain("billing.branch-priced-twice");

        var clone = await DraftAsync(owner, list, "Successor", [branch], cloneFrom: version);
        var superseded = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{clone}/publish", new { reason = "Next year." }, await VersionKeyAsync(owner, clone));
        superseded.StatusCode.ShouldBe(HttpStatusCode.OK, await superseded.Content.ReadAsStringAsync(Token));
        using var successor = JsonDocument.Parse(await superseded.Content.ReadAsStringAsync(Token));
        successor.RootElement.GetProperty("supersededVersionId").GetGuid().ShouldBe(version);
        successor.RootElement.GetProperty("published").GetProperty("items").EnumerateArray().Single().GetProperty("code").GetString().ShouldBe(Code("UNKNOWN_TAX"));
    }

    [Fact]
    public async Task TheCatalogueAndThePriceListEachRefuseToStrandTheOther()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await AdministrationHarness.AdministratorAsync(
            fixture, "pl-catalog", "203.0.113.252",
            BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList, CatalogPermissions.Edit, CatalogPermissions.Publish,
            IdentityPermissions.Branches);
        var branch = await BranchAsync(owner);
        var taxCode = await EnsurePublishedTaxCodeAsync(owner, "CAT");

        // A price-list version pricing the branch, holding one item.
        var list = await CreateListAsync(owner, Code("PL_CAT"));
        var version = await DraftAsync(owner, list, "Priced", [branch]);
        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/items", ItemBody(Code("PRICED_STITCHING"), taxCode: taxCode), await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Live." }, await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The catalogue names a code the version does not hold at that branch: refused with the field named.
        var catalogue = await CatalogDraftAsync(owner, "Priced catalogue");
        var category = await AddCategoryAsync(owner, catalogue, Code("PRICED"), branch);
        var service = await AddServiceAsync(owner, catalogue, category, "STITCHING", Code("NOBODY_HOLDS"), branch);
        var refused = await owner.PostAsync($"/api/v1/catalog/versions/{catalogue}/publish", new { reason = "Trying." }, await CatalogKeyAsync(owner, catalogue));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(Token));
        var problem = await refused.Content.ReadAsStringAsync(Token);
        problem.ShouldContain("catalog.price-list-item-unknown");
        problem.ShouldContain($"serviceTypes[{Code("PRICED")}.STITCHING].priceListItemCode");

        // Naming the code the version holds, it publishes.
        (await owner.PutAsync($"/api/v1/catalog/versions/{catalogue}/service-types/{service}", ServiceBody("STITCHING", Code("PRICED_STITCHING"), branch), await CatalogKeyAsync(owner, catalogue)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = await owner.PostAsync($"/api/v1/catalog/versions/{catalogue}/publish", new { reason = "Priced." }, await CatalogKeyAsync(owner, catalogue));
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));

        // From the other side: a successor that drops the item, or the branch, is refused; one that keeps both publishes.
        var dropped = await DraftAsync(owner, list, "Dropped the item", [branch]);
        var lost = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{dropped}/publish", new { reason = "Trying." }, await VersionKeyAsync(owner, dropped));
        lost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var lostProblem = await lost.Content.ReadAsStringAsync(Token);
        lostProblem.ShouldContain("billing.catalogue-reference-lost");
        lostProblem.ShouldContain($"items[{Code("PRICED_STITCHING")}]");

        var moved = await DraftAsync(owner, list, "Moved to another branch", [await BranchAsync(owner)], cloneFrom: version);
        var unpriced = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{moved}/publish", new { reason = "Trying." }, await VersionKeyAsync(owner, moved));
        unpriced.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unpriced.Content.ReadAsStringAsync(Token)).ShouldContain("billing.branch-left-unpriced");

        var kept = await DraftAsync(owner, list, "Kept both", [branch], cloneFrom: version);
        var succeeded = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{kept}/publish", new { reason = "Next year." }, await VersionKeyAsync(owner, kept));
        succeeded.StatusCode.ShouldBe(HttpStatusCode.OK, await succeeded.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task TheDatabaseRefusesTwoPublishedVersionsPricingOneBranchWhateverTheChecksSaw()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-branch", "203.0.113.255");
        var branch = await BranchAsync(owner);
        var first = await CreateListAsync(owner, Code("PL_BR_A"));
        var live = await DraftAsync(owner, first, "Live", [branch]);
        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{live}/publish", new { reason = "Live." }, await VersionKeyAsync(owner, live)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await CreateListAsync(owner, Code("PL_BR_B"));
        var rival = await DraftAsync(owner, second, "Rival", [branch]);

        // Publishing the rival straight at the database, as a request that read the checks before the
        // first publication committed would: the deferred exclusion constraint refuses it at commit.
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var exception = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.price_list_versions SET status = 1, published_at = now(), publish_reason = 'Raced.' WHERE id = {rival}", Token));
        exception.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        exception.ConstraintName.ShouldBe(BillingDbContext.OnePublishedVersionPerBranchConstraint);

        // And the store answers the same race as a conflict rather than a five hundred.
        var store = scope.ServiceProvider.GetRequiredService<IPriceListStore>();
        var draft = await store.FindVersionAsync(rival, SessionTestData.OrganisationId, Token);
        draft.ShouldNotBeNull();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        draft.Publish(clock.UtcNow, null, "Raced.").IsSuccess.ShouldBeTrue();
        var saved = await store.SavePublicationAsync(Token);
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe("billing.branch-publish-conflict");
    }

    [Fact]
    public async Task ADraftOlderThanThePublishedVersionStillSupersedesIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-order", "203.0.113.249");
        var branch = await BranchAsync(owner);
        var list = await CreateListAsync(owner, Code("PL_ORDER"));
        var older = await DraftAsync(owner, list, "Drafted first, published second", [branch]);
        var newer = await DraftAsync(owner, list, "Drafted second, published first", [branch]);

        (await owner.PostAsync($"/api/v1/billing/price-lists/versions/{newer}/publish", new { reason = "First." }, await VersionKeyAsync(owner, newer)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await owner.PostAsync($"/api/v1/billing/price-lists/versions/{older}/publish", new { reason = "Second." }, await VersionKeyAsync(owner, older));
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync(Token));
        using var publication = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Token));
        publication.RootElement.GetProperty("supersededVersionId").GetGuid().ShouldBe(newer);
    }

    [Fact]
    public async Task RefusesToPublishWithOnlyTheDraftingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await AdministrationHarness.AdministratorAsync(
            fixture, "pl-drafter", "203.0.113.248", BillingPermissions.ManagePriceLists, IdentityPermissions.Branches);
        var list = await CreateListAsync(drafter, Code("PL_DRAFTER"));
        var version = await DraftAsync(drafter, list, "Drafted by somebody who may not publish", [await BranchAsync(drafter)]);

        (await drafter.PostAsync($"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "Not mine to do." }, await VersionKeyAsync(drafter, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("describe")]
    [InlineData("add-item")]
    [InlineData("edit-item")]
    [InlineData("delete-item")]
    [InlineData("add-rule")]
    [InlineData("edit-rule")]
    [InlineData("delete-rule")]
    [InlineData("publish")]
    [InlineData("rename")]
    public async Task EveryCommandRequiresTheTagTheAdministratorRead(string operation)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-precondition", $"2001:db8:92::{Interlocked.Increment(ref _preconditionClientNumber):x}");
        var branch = await BranchAsync(owner);
        var list = await CreateListAsync(owner, Code($"PL_PRE_{operation.Replace('-', '_').ToUpperInvariant()}"));
        var version = await DraftAsync(owner, list, $"Preconditions {operation}", [branch]);
        var root = $"/api/v1/billing/price-lists/versions/{version}";
        using var item = JsonDocument.Parse(await (await owner.PostAsync($"{root}/items", ItemBody(Code("PRE_ITEM")), await VersionKeyAsync(owner, version))).Content.ReadAsStringAsync(Token));
        var itemId = item.RootElement.GetProperty("priceListItemId").GetGuid();
        using var rule = JsonDocument.Parse(await (await owner.PostAsync($"{root}/discount-rules", RuleBody(Code("PRE_RULE")), await VersionKeyAsync(owner, version))).Content.ReadAsStringAsync(Token));
        var ruleId = rule.RootElement.GetProperty("discountRuleId").GetGuid();

        // Capture the first screen's tags, then let a second screen move the version and the list.
        var stale = await VersionKeyAsync(owner, version);
        (await owner.PutAsync(root, VersionBody("Moved by a colleague", [branch]), await VersionKeyAsync(owner, version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listRoot = $"/api/v1/billing/price-lists/{list}";
        var staleList = await ListKeyAsync(owner, list);
        (await owner.PutAsync(listRoot, new { name = "Moved by a colleague", reason = (string?)null }, await ListKeyAsync(owner, list))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await SendAsync(owner, operation, root, listRoot, itemId, ruleId, stale, staleList);
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, $"{operation}: {await response.Content.ReadAsStringAsync(Token)}");

        // And with no tag at all, the request is not even considered.
        var untagged = await SendAsync(owner, operation, root, listRoot, itemId, ruleId, Key(), Key());
        untagged.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, operation);
    }

    [Fact]
    public async Task RefusesASecondListThatTookTheSameCode()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPriceListStore>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Two administrators creating a list with one code in the same moment: the handler's read saw
        // neither, and the unique index settles it as a conflict rather than a five hundred.
        foreach (var name in new[] { "First past the post", "Second past the post" })
        {
            var list = PriceList.Create(ids.NewId(), SessionTestData.OrganisationId, Code("PL_RACE"), name, clock.UtcNow, null);
            list.IsSuccess.ShouldBeTrue();
            store.AddList(list.Value);
        }

        var saved = await store.SaveDraftAsync(Token);
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe("billing.code-not-unique");
    }

    [Fact]
    public async Task RefusesEveryRouteToAHolderOfNoBillingPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("pl-deny-o", "203.0.113.253");
        var list = await CreateListAsync(owner, Code("PL_DENY"));
        var version = await DraftAsync(owner, list, "Denied", [await BranchAsync(owner)]);
        var key = await VersionKeyAsync(owner, version);
        using var reader = await AdministrationHarness.AdministratorAsync(fixture, "pl-deny-r", "203.0.113.254", CatalogPermissions.Read);
        var any = Guid.CreateVersion7();

        var attempts = new (string Method, string Path, object? Body)[]
        {
            ("GET", "/api/v1/billing/price-lists", null),
            ("POST", "/api/v1/billing/price-lists", new { code = "X_Y", name = "x" }),
            ("GET", $"/api/v1/billing/price-lists/{list}", null),
            ("PUT", $"/api/v1/billing/price-lists/{list}", new { name = "x" }),
            ("GET", $"/api/v1/billing/price-lists/{list}/versions", null),
            ("POST", $"/api/v1/billing/price-lists/{list}/versions", VersionBody("x", [any])),
            ("GET", $"/api/v1/billing/price-lists/versions/{version}", null),
            ("PUT", $"/api/v1/billing/price-lists/versions/{version}", VersionBody("x", [any])),
            ("GET", $"/api/v1/billing/price-lists/versions/{version}/validation", null),
            ("POST", $"/api/v1/billing/price-lists/versions/{version}/items", ItemBody("X_Y")),
            ("PUT", $"/api/v1/billing/price-lists/versions/{version}/items/{any}", ItemBody("X_Y")),
            ("POST", $"/api/v1/billing/price-lists/versions/{version}/items/{any}/delete", new { reason = "x" }),
            ("POST", $"/api/v1/billing/price-lists/versions/{version}/discount-rules", RuleBody("X_Y")),
            ("PUT", $"/api/v1/billing/price-lists/versions/{version}/discount-rules/{any}", RuleBody("X_Y")),
            ("POST", $"/api/v1/billing/price-lists/versions/{version}/discount-rules/{any}/delete", new { reason = "x" }),
            ("POST", $"/api/v1/billing/price-lists/versions/{version}/publish", new { reason = "x" }),
        };

        foreach (var (method, path, body) in attempts)
        {
            var response = method switch
            {
                "GET" => await reader.GetAsync(path),
                "PUT" => await reader.PutAsync(path, body!, key),
                _ => await reader.PostAsync(path, body!, key),
            };
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key() => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, BillingPermissions.ManagePriceLists, BillingPermissions.PublishPriceList, IdentityPermissions.Branches);

    private static Task<Guid> BranchAsync(AdministrationHarness.AdministratorClient client)
        => BillingHarness.OpenBranchAsync(client);

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/price-lists/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag.ToString())];
    }

    private static async Task<(string Name, string Value)[]> ListKeyAsync(AdministrationHarness.AdministratorClient client, Guid list)
    {
        using var read = await client.GetAsync($"/api/v1/billing/price-lists/{list}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static Task<HttpResponseMessage> SendAsync(
        AdministrationHarness.AdministratorClient client, string operation, string root, string listRoot, Guid itemId, Guid ruleId,
        (string Name, string Value)[] versionHeaders, (string Name, string Value)[] listHeaders)
        => operation switch
        {
            "describe" => client.PutAsync(root, VersionBody("Stale", [Guid.CreateVersion7()]), versionHeaders),
            "add-item" => client.PostAsync($"{root}/items", ItemBody(Code("STALE_ITEM")), versionHeaders),
            "edit-item" => client.PutAsync($"{root}/items/{itemId}", ItemBody(Code("PRE_ITEM"), rate: 1m), versionHeaders),
            "delete-item" => client.PostAsync($"{root}/items/{itemId}/delete", new { reason = "Stale." }, versionHeaders),
            "add-rule" => client.PostAsync($"{root}/discount-rules", RuleBody(Code("STALE_RULE")), versionHeaders),
            "edit-rule" => client.PutAsync($"{root}/discount-rules/{ruleId}", RuleBody(Code("PRE_RULE")), versionHeaders),
            "delete-rule" => client.PostAsync($"{root}/discount-rules/{ruleId}/delete", new { reason = "Stale." }, versionHeaders),
            "rename" => client.PutAsync(listRoot, new { name = "Stale", reason = (string?)null }, listHeaders),
            _ => client.PostAsync($"{root}/publish", new { reason = "Stale." }, versionHeaders),
        };

    private static async Task<Guid> CreateListAsync(AdministrationHarness.AdministratorClient client, string code)
    {
        var response = await client.PostAsync("/api/v1/billing/price-lists", new { code, name = code, reason = (string?)null }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("priceListId").GetGuid();
    }

    private static object VersionBody(string name, Guid[] branches, Guid? cloneFrom = null)
        => new
        {
            name,
            notes = (string?)null,
            effectiveFrom = "2026-04-01",
            taxInclusive = false,
            roundOff = "NearestRupee",
            overrideThresholdPercent = 10m,
            branchIds = branches,
            cloneFromVersionId = cloneFrom,
            reason = (string?)null,
        };

    private static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, Guid list, string name, Guid[] branches, Guid? cloneFrom = null)
    {
        var response = await client.PostAsync($"/api/v1/billing/price-lists/{list}/versions", VersionBody(name, branches, cloneFrom), Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("priceListVersionId").GetGuid();
    }

    private static object ItemBody(string code, decimal rate = 450m, string taxCode = "STITCHING_5")
        => new { code, description = "Blouse stitching, pattern work", kind = "Service", baseRate = rate, unit = "each", taxCode, active = true, reason = (string?)null };

    private static object RuleBody(string code)
        => new { code, description = "Festival-season discount", kind = "Percentage", maximumWithoutApproval = 5m, maximum = 15m, active = true, reason = (string?)null };

    /// <summary>
    /// Publishes a tax configuration version holding one code of this run's own, cloned from whatever is
    /// published so that other runs' codes survive with their keys; returns the code.
    /// </summary>
    private static async Task<string> EnsurePublishedTaxCodeAsync(AdministrationHarness.AdministratorClient client, string stem)
    {
        var code = Code($"TAX_{stem}");
        using var listed = JsonDocument.Parse(
            await (await client.GetAsync("/api/v1/billing/tax-configuration/versions")).Content.ReadAsStringAsync(Token));
        var live = listed.RootElement.EnumerateArray().FirstOrDefault(row => row.GetProperty("status").GetString() == "Published");
        Guid? cloneFrom = live.ValueKind == JsonValueKind.Object ? live.GetProperty("taxConfigurationVersionId").GetGuid() : null;

        var draft = await client.PostAsync(
            "/api/v1/billing/tax-configuration/versions",
            new { name = $"Tax for {RunToken} {stem}", notes = (string?)null, effectiveFrom = "2026-04-01", cloneFromVersionId = cloneFrom },
            Key());
        draft.StatusCode.ShouldBe(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await draft.Content.ReadAsStringAsync(Token));
        var version = body.RootElement.GetProperty("version").GetProperty("taxConfigurationVersionId").GetGuid();

        var added = await client.PostAsync($"/api/v1/billing/tax-configuration/versions/{version}/tax-codes", TaxCodeBody(code), await TaxKeyAsync(client, version));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Token));
        var publish = await client.PostAsync($"/api/v1/billing/tax-configuration/versions/{version}/publish", new { reason = "For the price-list tests." }, await TaxKeyAsync(client, version));
        publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Token));
        return code;
    }

    private static object TaxCodeBody(string code)
        => new
        {
            code,
            description = "Tailoring services",
            classification = "998822",
            kind = "Services",
            active = true,
            rates = new[] { new { kind = "Cgst", ratePercent = 2.5m }, new { kind = "Sgst", ratePercent = 2.5m }, new { kind = "Igst", ratePercent = 5m } },
            reason = (string?)null,
        };

    private static async Task<(string Name, string Value)[]> TaxKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/billing/tax-configuration/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static async Task<Guid> CatalogDraftAsync(AdministrationHarness.AdministratorClient client, string name)
    {
        var response = await client.PostAsync("/api/v1/catalog/versions", new { name }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("catalogVersionId").GetGuid();
    }

    private static async Task<(string Name, string Value)[]> CatalogKeyAsync(AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static async Task<Guid> AddCategoryAsync(AdministrationHarness.AdministratorClient client, Guid version, string code, Guid branch)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories",
            new
            {
                code,
                name = code,
                nameTamil = (string?)null,
                description = "A synthetic category written by a test.",
                parentCategoryId = (Guid?)null,
                displayOrder = 0,
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                featureFlagKey = (string?)null,
                branchIds = new[] { branch },
                reason = "Written by an integration test.",
            },
            await CatalogKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("categoryId").GetGuid();
    }

    private static object ServiceBody(string code, string itemCode, Guid branch)
        => new
        {
            code,
            name = code,
            nameTamil = (string?)null,
            description = (string?)null,
            displayOrder = 0,
            expectedDurationDays = 7,
            intakeWarning = (string?)null,
            measurementTemplateId = (Guid?)null,
            workflowDefinitionId = (Guid?)null,
            designOptionGroupIds = Array.Empty<Guid>(),
            priceListItemCode = itemCode,
            qcChecklistTemplateId = (Guid?)null,
            allowIncomplete = true,
            activeFrom = (DateOnly?)null,
            activeTo = (DateOnly?)null,
            branchIds = new[] { branch },
            reason = "Written by an integration test.",
        };

    private static async Task<Guid> AddServiceAsync(AdministrationHarness.AdministratorClient client, Guid version, Guid category, string code, string itemCode, Guid branch)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{category}/service-types",
            ServiceBody(code, itemCode, branch),
            await CatalogKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("serviceTypeId").GetGuid();
    }
}
