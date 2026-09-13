using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Billing;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The design picker's own routes (#30, issue #140): what a service type offers, the drafts Reception
/// builds against it, the migration a republish leaves standing, and the read #32a will pin at
/// confirmation — the authorisation matrix, a stale tag, an expired draft, a consumed draft and the
/// snapshot's immutability.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CatalogDesignSelectionEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();
    private static readonly string[] CapOnly = ["CAP"];
    private static readonly string[] ZipOnly = ["ZIP"];
    private static readonly string[] OptionStyleB = ["STYLE_B"];
    private static readonly string[] PipingOnly = ["PIPING"];
    private static readonly string[] BoundOnly = ["BOUND"];

    [Fact]
    public async Task APickerReadADraftAndACheckAgreeAndTheQuerySnapshotIsDeterministic()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-happy-o", "203.0.113.240");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-happy");

        using var counter = await CounterAsync("sel-happy-c", "203.0.113.241");

        // The picker: only the offered groups and their active options, at this branch, today.
        using var picker = JsonDocument.Parse(
            await (await counter.GetAsync(
                    $"/api/v1/catalog/current/service-types/{fixtureData.ServiceTypeId}/design"))
                .Content.ReadAsStringAsync(Token));
        var groups = picker.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        groups.Select(group => group.GetProperty("code").GetString())
            .ShouldBe(["sleeve_style", "closure", "lining"]);
        groups[0].GetProperty("options").EnumerateArray()
            .Select(option => option.GetProperty("code").GetString())
            .ShouldBe(["FULL", "CAP"]);

        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);

        var saved = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{draftId}",
            SaveBody([("sleeve_style", ["FULL"]), ("closure", ["HOOK"])], "Keep the shoulder loose."),
            await DraftKeyAsync(counter, draftId));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));

        using var check = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftId}/check"))
                .Content.ReadAsStringAsync(Token));
        check.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeTrue();
        check.RootElement.GetProperty("violations").GetArrayLength().ShouldBe(0);
        check.RootElement.GetProperty("notes").EnumerateArray().Single()
            .GetProperty("text").GetString().ShouldBe("Press the seam flat.");

        // The published contract, called twice with nothing in between: the same answer, and the same
        // JSON both times — what "the snapshot is a copy" has to mean at the boundary Orders reads it
        // through.
        using var scope = fixture.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IDesignSelectionQuery>();
        var first = await query.GetAsync(draftId, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
        var second = await query.GetAsync(draftId, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
        first.IsSuccess.ShouldBeTrue();
        first.Value.IsConfirmable.ShouldBeTrue();
        first.Value.Snapshot.Selections.Select(selection => (selection.GroupCode, selection.OptionCode))
            .ShouldBe([("sleeve_style", "FULL"), ("closure", "HOOK")]);
        first.Value.Snapshot.Instructions.ShouldBe("Keep the shoulder loose.");
        JsonSerializer.Serialize(first.Value.Snapshot).ShouldBe(JsonSerializer.Serialize(second.Value.Snapshot));
    }

    [Fact]
    public async Task ABlockingExclusionRefusesConfirmationAndANoteNeverBlocks()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-block-o", "203.0.113.242");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-block");

        using var counter = await CounterAsync("sel-block-c", "203.0.113.243");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);

        // DR excludes: sleeve_style = CAP excludes closure = ZIP. Both chosen, so confirmation is refused
        // — but the Always note still fires, because a note never blocks.
        var saved = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{draftId}",
            SaveBody([("sleeve_style", ["CAP"]), ("closure", ["ZIP"])], null),
            await DraftKeyAsync(counter, draftId));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var check = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftId}/check"))
                .Content.ReadAsStringAsync(Token));
        check.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeFalse();
        var violation = check.RootElement.GetProperty("violations").EnumerateArray().Single();
        violation.GetProperty("code").GetString().ShouldBe("design.excluded");
        violation.GetProperty("blocks").GetBoolean().ShouldBeTrue();
        check.RootElement.GetProperty("notes").EnumerateArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARepublishLeavesTheDraftPinnedAndMigrationAppliesExactlyWhatTheReadNamed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-mig-o", "203.0.113.244");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-mig");

        using var counter = await CounterAsync("sel-mig-c", "203.0.113.245");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftId}",
                SaveBody([("sleeve_style", ["FULL"]), ("closure", ["HOOK"])], null),
                await DraftKeyAsync(counter, draftId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The republish: clone, retire the option this very draft chose for sleeve_style (never the
        // Excludes rule's own CAP or ZIP — retiring either would make the rule engine's own publish
        // checks refuse the version for an unrelated reason, a defect this fixture is not about), make
        // the optional lining group required, and add a note rule reading a group this service offers.
        var clonedVersion = await CloneAsync(owner, fixtureData.VersionId, "Republished");
        using var clonedRead = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{clonedVersion}")).Content.ReadAsStringAsync(Token));
        var clonedGroups = clonedRead.RootElement.GetProperty("designGroups").EnumerateArray().ToArray();
        var clonedSleeve = clonedGroups.Single(group => group.GetProperty("code").GetString() == "sleeve_style");
        var clonedFull = clonedSleeve.GetProperty("options").EnumerateArray()
            .Single(option => option.GetProperty("code").GetString() == "FULL");
        var clonedLining = clonedGroups.Single(group => group.GetProperty("code").GetString() == "lining");
        var clonedCategory = clonedRead.RootElement.GetProperty("categories").EnumerateArray().Single()
            .GetProperty("categoryId").GetGuid();

        (await owner.PutAsync(
                $"/api/v1/catalog/versions/{clonedVersion}/design-options/{clonedFull.GetProperty("designOptionId").GetGuid()}",
                OptionBody("FULL", active: false),
                await VersionKeyAsync(owner, clonedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PutAsync(
                $"/api/v1/catalog/versions/{clonedVersion}/design-groups/{clonedLining.GetProperty("designOptionGroupId").GetGuid()}",
                GroupBody("lining", required: true),
                await VersionKeyAsync(owner, clonedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var addedRule = await owner.PostAsync(
            $"/api/v1/catalog/versions/{clonedVersion}/categories/{clonedCategory}/design-rules",
            NoteRuleBody("sleeve_style", "Check the shoulder seam."),
            await VersionKeyAsync(owner, clonedVersion));
        using var addedRuleBody = JsonDocument.Parse(await addedRule.Content.ReadAsStringAsync(Token));
        var addedRuleIdentifier = addedRuleBody.RootElement.GetProperty("identifier").GetString();
        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{clonedVersion}/publish",
                new { reason = "Republished mid-draft." },
                await VersionKeyAsync(owner, clonedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The pin holds: reading the draft still answers from v1, with a prompt naming exactly the
        // three things that moved.
        using var beforeMigration = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftId}")).Content.ReadAsStringAsync(Token));
        beforeMigration.RootElement.GetProperty("catalogVersionId").GetGuid().ShouldBe(fixtureData.VersionId);
        var prompt = beforeMigration.RootElement.GetProperty("migrationPrompt");
        prompt.ValueKind.ShouldNotBe(JsonValueKind.Null);
        var changes = prompt.GetProperty("changes").EnumerateArray().ToArray();
        changes.Length.ShouldBe(3, string.Join("; ", changes.Select(change => change.GetProperty("message").GetString())));
        changes.Select(change => change.GetProperty("kind").GetString()).ShouldBe(
            [
                "design.option-retired",
                "design.group-newly-required",
                "design.rule-added",
            ],
            ignoreOrder: true);
        changes.Single(change => change.GetProperty("kind").GetString() == "design.option-retired")
            .GetProperty("optionCode").GetString().ShouldBe("FULL");
        changes.Single(change => change.GetProperty("kind").GetString() == "design.group-newly-required")
            .GetProperty("groupCode").GetString().ShouldBe("lining");
        changes.Single(change => change.GetProperty("kind").GetString() == "design.rule-added")
            .GetProperty("ruleIdentifier").GetString().ShouldBe(addedRuleIdentifier);

        // Migrating re-pins it, drops the retired selection, and re-validates against the version just
        // migrated to.
        var migrated = await counter.PostAsync(
            $"/api/v1/catalog/design-drafts/{draftId}/migrate",
            new { },
            await DraftKeyAsync(counter, draftId));
        migrated.StatusCode.ShouldBe(HttpStatusCode.OK, await migrated.Content.ReadAsStringAsync(Token));
        using var outcome = JsonDocument.Parse(await migrated.Content.ReadAsStringAsync(Token));
        outcome.RootElement.GetProperty("draft").GetProperty("catalogVersionId").GetGuid().ShouldBe(clonedVersion);
        outcome.RootElement.GetProperty("appliedChanges").GetArrayLength().ShouldBe(3);
        var draftSelections = outcome.RootElement.GetProperty("draft").GetProperty("selections").EnumerateArray()
            .Select(selection => selection.GetProperty("groupCode").GetString()).ToArray();
        draftSelections.ShouldBe(["closure"], "sleeve_style's only chosen option was retired and could not survive");

        var evaluation = outcome.RootElement.GetProperty("evaluation");
        evaluation.GetProperty("confirmable").GetBoolean().ShouldBeFalse();
        var unset = evaluation.GetProperty("violations").EnumerateArray()
            .Where(violation => violation.GetProperty("code").GetString() == "design.required-group-unset")
            .Select(violation => violation.GetProperty("groupCode").GetString())
            .ToArray();
        unset.ShouldBe(["sleeve_style", "lining"], ignoreOrder: true);

        // Migrating again is a harmless no-op: the draft is now current.
        var again = await counter.PostAsync(
            $"/api/v1/catalog/design-drafts/{draftId}/migrate", new { }, await DraftKeyAsync(counter, draftId));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var againBody = JsonDocument.Parse(await again.Content.ReadAsStringAsync(Token));
        againBody.RootElement.GetProperty("appliedChanges").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ASnapshotBuiltBeforeARepublishIsByteForByteIdenticalToOneBuiltFromThePinnedVersionAfterwards()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-snap-o", "203.0.113.246");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-snap");

        using var counter = await CounterAsync("sel-snap-c", "203.0.113.247");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftId}",
                SaveBody([("sleeve_style", ["FULL"]), ("closure", ["HOOK"])], "As agreed."),
                await DraftKeyAsync(counter, draftId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scopeBefore = fixture.Services.CreateScope();
        var before = await scopeBefore.ServiceProvider.GetRequiredService<IDesignSelectionQuery>()
            .GetAsync(draftId, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
        before.IsSuccess.ShouldBeTrue();

        // An unrelated republish: a clone that retires an option this draft never chose, published.
        var clonedVersion = await CloneAsync(owner, fixtureData.VersionId, "Unrelated republish");
        using var clonedRead = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{clonedVersion}")).Content.ReadAsStringAsync(Token));
        var clonedLining = clonedRead.RootElement.GetProperty("designGroups").EnumerateArray()
            .Single(group => group.GetProperty("code").GetString() == "lining");
        var clonedNone = clonedLining.GetProperty("options").EnumerateArray()
            .Single(option => option.GetProperty("code").GetString() == "NONE");
        (await owner.PutAsync(
                $"/api/v1/catalog/versions/{clonedVersion}/design-options/{clonedNone.GetProperty("designOptionId").GetGuid()}",
                OptionBody("NONE", active: false),
                await VersionKeyAsync(owner, clonedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{clonedVersion}/publish",
                new { reason = "Unrelated republish." },
                await VersionKeyAsync(owner, clonedVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scopeAfter = fixture.Services.CreateScope();
        var after = await scopeAfter.ServiceProvider.GetRequiredService<IDesignSelectionQuery>()
            .GetAsync(draftId, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
        after.IsSuccess.ShouldBeTrue();

        JsonSerializer.Serialize(before.Value.Snapshot).ShouldBe(JsonSerializer.Serialize(after.Value.Snapshot));
        after.Value.Snapshot.CatalogVersionId.ShouldBe(fixtureData.VersionId, "the pin holds until migration is asked for");
    }

    [Fact]
    public async Task AStaleTagIsRefusedOnASaveAndOnAMigrate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-stale-o", "203.0.113.248");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-stale");

        using var counter = await CounterAsync("sel-stale-c", "203.0.113.249");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);
        var staleKey = await DraftKeyAsync(counter, draftId);

        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftId}",
                SaveBody([("sleeve_style", ["FULL"])], null),
                await DraftKeyAsync(counter, draftId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{draftId}", SaveBody([("closure", ["HOOK"])], null), staleKey);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(stale)).ShouldBe("catalog.design-draft-changed");

        var staleMigrate = await counter.PostAsync(
            $"/api/v1/catalog/design-drafts/{draftId}/migrate", new { }, staleKey);
        staleMigrate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(staleMigrate)).ShouldBe("catalog.design-draft-changed");
    }

    [Fact]
    public async Task AnExpiredDraftRefusesASaveAndAConsumedDraftDoesToo()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-exp-o", "203.0.113.250");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-exp");

        using var counter = await CounterAsync("sel-exp-c", "203.0.113.251");
        var expiredDraftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await context.Database.ExecuteSqlAsync(
                $"""
                 UPDATE catalog.design_selection_drafts
                    SET started_at = now() - interval '2 hours', expires_at = now() - interval '1 hour'
                  WHERE id = {expiredDraftId}
                 """,
                Token);
        }

        // Nothing has consumed this draft yet — Consume itself would refuse it too, and the
        // confirmation-facing read (Orders, at #32a) is expected to already say so rather than let a
        // caller press on toward confirmation with a draft that has simply outlived its lifetime.
        using (var scope = fixture.Services.CreateScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IDesignSelectionQuery>();
            var read = await query.GetAsync(
                expiredDraftId, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
            read.IsSuccess.ShouldBeTrue();
            read.Value.IsOpen.ShouldBeFalse("an expired draft may never be consumed, open or not");
        }

        // The direct SQL above moves the row's own xmin, so the tag has to be re-read afterwards — a
        // stale tag would report "changed" before the expiry check is ever reached, which is not what
        // this test is about.
        var expiredKey = await DraftKeyAsync(counter, expiredDraftId);
        var expired = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{expiredDraftId}", SaveBody([("sleeve_style", ["FULL"])], null), expiredKey);
        expired.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(expired)).ShouldBe("catalog.design-draft-expired");

        var consumedDraftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);

        using (var scope = fixture.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<DesignSelectionDraftHandler>();
            (await handler.ConsumeAsync(consumedDraftId, SessionTestData.OrganisationId, Token)).IsSuccess
                .ShouldBeTrue();
        }

        // Consuming saves through EF too, moving xmin the same way; the tag is read afterwards for the
        // same reason as above.
        var consumedKey = await DraftKeyAsync(counter, consumedDraftId);
        var consumed = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{consumedDraftId}", SaveBody([("sleeve_style", ["FULL"])], null), consumedKey);
        consumed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(consumed)).ShouldBe("catalog.design-draft-already-consumed");
    }

    [Fact]
    public async Task AServiceScopedEvaluationAutoSelectsNarrowsRequiredGroupsAndRefusesOutOfScopeSaves()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-scope-o", "203.0.113.257");
        var fixtureData = await BuildMultiServiceCatalogueAsync(owner, "sel-scope");

        using var counter = await CounterAsync("sel-scope-c", "203.0.113.258");

        // Service A never links "lining" at all — it belongs only to service B — so a draft of service A
        // must never be blocked by lining being required and unset.
        var draftA = await StartDraftAsync(counter, fixtureData.ServiceAId);

        // Only cut = STYLE_B is chosen; trim is never touched. DR-01 requires trim = PIPING whenever
        // cut = STYLE_B, and trim has exactly one admissible option, so the rule settles it on the
        // customer's behalf.
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftA}",
                SaveBody([("cut", ["STYLE_B"])], null),
                await DraftKeyAsync(counter, draftA)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var checkA = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftA}/check"))
                .Content.ReadAsStringAsync(Token));
        checkA.RootElement.GetProperty("confirmable").GetBoolean()
            .ShouldBeTrue("lining belongs only to the sibling service and must never gate this one");
        checkA.RootElement.GetProperty("violations").EnumerateArray()
            .ShouldNotContain(violation => violation.GetProperty("groupCode").GetString() == "lining");

        using (var scope = fixture.Services.CreateScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IDesignSelectionQuery>();
            var readA = await query.GetAsync(
                draftA, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
            readA.IsSuccess.ShouldBeTrue();
            readA.Value.IsConfirmable.ShouldBeTrue();
            readA.Value.Snapshot.Selections.Select(selection => (selection.GroupCode, selection.OptionCode))
                .ShouldBe([("cut", "STYLE_B"), ("trim", "PIPING")], ignoreOrder: true,
                    "the rule's own auto-selection must reach the frozen snapshot, price-list item included");
        }

        // "lining" is a real group of this category — just never linked to service A. A crafted request
        // naming it must be refused, never silently accepted and later snapshotted.
        var leaky = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{draftA}",
            SaveBody([("cut", ["STYLE_A"]), ("lining", ["FULL"])], null),
            await DraftKeyAsync(counter, draftA));
        leaky.StatusCode.ShouldBe(HttpStatusCode.NotFound, await leaky.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(leaky)).ShouldBe("catalog.design-group-not-found");

        using var unchanged = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftA}")).Content.ReadAsStringAsync(Token));
        unchanged.RootElement.GetProperty("selections").EnumerateArray()
            .Select(selection => selection.GetProperty("groupCode").GetString())
            .ShouldBe(["cut"], "the refused save must leave the draft exactly as it was");

        // The scoping must be real, not a blanket skip of the required-group check: service B still
        // offers and requires lining, and a draft of its own with lining unset is still non-confirmable.
        var draftB = await StartDraftAsync(counter, fixtureData.ServiceBId);
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftB}",
                SaveBody([("cut", ["STYLE_A"])], null),
                await DraftKeyAsync(counter, draftB)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var checkB = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftB}/check"))
                .Content.ReadAsStringAsync(Token));
        checkB.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeFalse();
        checkB.RootElement.GetProperty("violations").EnumerateArray()
            .Select(violation => violation.GetProperty("groupCode").GetString())
            .ShouldContain("lining");
    }

    [Fact]
    public async Task AnAutoSelectionChainedThroughAnUnofferedGroupNeverReachesTheSnapshot()
    {
        // Codex review, PR #221: dropping only an auto-selection that directly names an unoffered group
        // missed a chain — cut requires trim (unoffered by this service), and trim in turn requires
        // edging (offered). Before the fix, edging's auto-selection survived the filter purely because
        // edging itself is an offered group, even though the only reason it fired at all was a rule two
        // hops downstream of a group this service's picker never showed.
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-chain-o", "203.0.113.261");
        var fixtureData = await BuildChainedRuleCatalogueAsync(owner, "sel-chain");

        using var counter = await CounterAsync("sel-chain-c", "203.0.113.262");

        // Service X never links "trim" — only cut and edging — so the trim->edging leg of the chain must
        // never settle anything for it, even though edging itself is offered here.
        var draftX = await StartDraftAsync(counter, fixtureData.ServiceXId);
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftX}",
                SaveBody([("cut", ["STYLE_B"])], null),
                await DraftKeyAsync(counter, draftX)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var checkX = JsonDocument.Parse(
            await (await counter.GetAsync($"/api/v1/catalog/design-drafts/{draftX}/check"))
                .Content.ReadAsStringAsync(Token));
        checkX.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeTrue();
        checkX.RootElement.GetProperty("autoSelections").EnumerateArray()
            .ShouldBeEmpty("neither trim nor edging is reachable from what this service's picker showed");

        using (var scope = fixture.Services.CreateScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IDesignSelectionQuery>();
            var readX = await query.GetAsync(
                draftX, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
            readX.IsSuccess.ShouldBeTrue();
            readX.Value.Snapshot.Selections.Select(selection => selection.GroupCode)
                .ShouldBe(["cut"], "edging must never freeze onto the snapshot on the strength of a rule chain "
                    + "that runs through a group this service never offered");
        }

        // The chain is real, not disabled outright: service Y offers cut, trim and edging together, and
        // the same two rules settle both hops on its own draft.
        var draftY = await StartDraftAsync(counter, fixtureData.ServiceYId);
        (await counter.PutAsync(
                $"/api/v1/catalog/design-drafts/{draftY}",
                SaveBody([("cut", ["STYLE_B"])], null),
                await DraftKeyAsync(counter, draftY)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = fixture.Services.CreateScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IDesignSelectionQuery>();
            var readY = await query.GetAsync(
                draftY, SessionTestData.OrganisationId, hasReferenceImage: false, Token);
            readY.IsSuccess.ShouldBeTrue();
            readY.Value.Snapshot.Selections.Select(selection => (selection.GroupCode, selection.OptionCode))
                .ShouldBe([("cut", "STYLE_B"), ("trim", "PIPING"), ("edging", "BOUND")], ignoreOrder: true,
                    "both hops fire when every group the chain touches is one this service actually offers");
        }
    }

    [Fact]
    public async Task SavingInstructionsOverTheColumnLimitIsRefusedAsACleanValidationErrorRatherThanA500()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-long-o", "203.0.113.259");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-long");

        using var counter = await CounterAsync("sel-long-c", "203.0.113.260");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);

        var tooLong = new string('a', 2001);
        var response = await counter.PutAsync(
            $"/api/v1/catalog/design-drafts/{draftId}",
            SaveBody([("sleeve_style", ["FULL"])], tooLong),
            await DraftKeyAsync(counter, draftId));

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Token));
        (await CodeOfAsync(response)).ShouldBe("catalog.value-too-long");
    }

    [Fact]
    public async Task EveryDraftRouteIsRefusedFromAnotherBranchAndToACallerWithoutThePermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("sel-deny-o", "203.0.113.252");
        var fixtureData = await BuildCatalogueAsync(owner, "sel-deny");

        using var counter = await CounterAsync("sel-deny-c", "203.0.113.253");
        var draftId = await StartDraftAsync(counter, fixtureData.ServiceTypeId);
        var key = await DraftKeyAsync(counter, draftId);

        var branchesOwner = await AdministrationHarness.AdministratorAsync(
            fixture, "sel-deny-branches", "203.0.113.254", IdentityPermissions.Branches);
        var elsewhere = await BillingHarness.OpenBranchAsync(branchesOwner);
        branchesOwner.Dispose();

        using var stranger = await AdministrationHarness.AdministratorAtBranchAsync(
            fixture, "sel-deny-stranger", "203.0.113.255", elsewhere,
            CatalogPermissions.DesignSelect, CatalogPermissions.Read);

        (await stranger.GetAsync($"/api/v1/catalog/design-drafts/{draftId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound, "another branch's draft reads as not found");
        (await stranger.PutAsync($"/api/v1/catalog/design-drafts/{draftId}", SaveBody([("sleeve_style", ["FULL"])], null), key))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/catalog/design-drafts/{draftId}/check")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/api/v1/catalog/design-drafts/{draftId}/migrate", new { }, key)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        using var reader = await CounterWithOnlyReadAsync("sel-deny-reader", "203.0.113.256");

        (await reader.GetAsync($"/api/v1/catalog/current/service-types/{fixtureData.ServiceTypeId}/design"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync(
                "/api/v1/catalog/design-drafts", new { serviceTypeId = fixtureData.ServiceTypeId }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/catalog/design-drafts/{draftId}")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PutAsync($"/api/v1/catalog/design-drafts/{draftId}", SaveBody([("sleeve_style", ["FULL"])], null), key))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/catalog/design-drafts/{draftId}/check")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync($"/api/v1/catalog/design-drafts/{draftId}/migrate", new { }, key)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static Guid HomeBranch => SessionTestData.HomeBranchId;

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Edit, CatalogPermissions.Publish);

    private Task<AdministrationHarness.AdministratorClient> CounterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.DesignSelect, CatalogPermissions.Read);

    private Task<AdministrationHarness.AdministratorClient> CounterWithOnlyReadAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(fixture, prefix, address, CatalogPermissions.Read);

    /// <summary>
    /// Builds and publishes a small catalogue: sleeve_style (required: FULL, CAP), closure (required:
    /// HOOK, ZIP), lining (optional: NONE, FULL); an excludes rule between sleeve_style = CAP and
    /// closure = ZIP; an always-note; one service type offering all three groups.
    /// </summary>
    private async Task<FixtureCatalogue> BuildCatalogueAsync(
        AdministrationHarness.AdministratorClient owner, string prefix)
    {
        var measurementTemplateId = await PublishedTemplateAsync(prefix);
        var version = await DraftAsync(owner, $"{prefix} catalogue");
        var category = await AddCategoryAsync(
            owner, version, Code($"{prefix.Replace('-', '_').ToUpperInvariant()}_CAT"));
        var sleeve = await AddGroupAsync(owner, version, category, "sleeve_style");
        var closure = await AddGroupAsync(owner, version, category, "closure", displayOrder: 1);
        var lining = await AddGroupAsync(owner, version, category, "lining", required: false, displayOrder: 2);
        await AddOptionAsync(owner, version, sleeve, "FULL");
        await AddOptionAsync(owner, version, sleeve, "CAP", displayOrder: 1);
        await AddOptionAsync(owner, version, closure, "HOOK");
        await AddOptionAsync(owner, version, closure, "ZIP", displayOrder: 1);
        await AddOptionAsync(owner, version, lining, "NONE");
        await AddOptionAsync(owner, version, lining, "FULL", displayOrder: 1);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/design-rules",
                new
                {
                    type = "Excludes",
                    antecedent = new { groupCode = "sleeve_style", form = "Equals", optionCodes = CapOnly },
                    consequent = new { groupCode = "closure", form = "Equals", optionCodes = ZipOnly },
                    note = (string?)null,
                    why = "A cap sleeve is never paired with a full zip closure.",
                    reason = (string?)null,
                },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/design-rules",
                NoteRuleAlwaysBody("Press the seam flat."),
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var service = await AddServiceAsync(
            owner, version, category, "STITCHING", [sleeve, closure, lining], measurementTemplateId);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved for the picker tests." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return new FixtureCatalogue(version, category, service);
    }

    private static async Task<Guid> StartDraftAsync(
        AdministrationHarness.AdministratorClient counter, Guid serviceTypeId)
    {
        var response = await counter.PostAsync(
            "/api/v1/catalog/design-drafts", new { serviceTypeId }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designSelectionDraftId").GetGuid();
    }

    private static object SaveBody((string GroupCode, string[] OptionCodes)[] selections, string? instructions)
        => new
        {
            selections = selections.Select(selection => new { groupCode = selection.GroupCode, optionCodes = selection.OptionCodes }),
            instructions,
        };

    private static async Task<(string Name, string Value)[]> DraftKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid draftId)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/design-drafts/{draftId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag!.ToString())];
    }

    private static async Task<Guid> DraftAsync(AdministrationHarness.AdministratorClient client, string name)
    {
        var response = await client.PostAsync("/api/v1/catalog/versions", new { name }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("catalogVersionId").GetGuid();
    }

    private static async Task<Guid> CloneAsync(
        AdministrationHarness.AdministratorClient client, Guid source, string name)
    {
        var response = await client.PostAsync(
            "/api/v1/catalog/versions", new { name, cloneFromVersionId = source }, Key());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("version").GetProperty("catalogVersionId").GetGuid();
    }

    private static async Task<Guid> AddCategoryAsync(
        AdministrationHarness.AdministratorClient client, Guid version, string code)
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
                branchIds = new[] { HomeBranch },
                reason = "Written by an integration test.",
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("categoryId").GetGuid();
    }

    private static object GroupBody(string code, bool required = true, int displayOrder = 0)
        => new
        {
            code,
            name = code.Replace('_', ' '),
            nameTamil = (string?)null,
            selectionMode = "SingleChoice",
            required,
            displayOrder,
            activeFrom = (DateOnly?)null,
            activeTo = (DateOnly?)null,
            branchIds = new[] { HomeBranch },
            reason = "Written by an integration test.",
        };

    private static async Task<Guid> AddGroupAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string code,
        bool required = true,
        int displayOrder = 0)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/design-groups",
            GroupBody(code, required, displayOrder),
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designOptionGroupId").GetGuid();
    }

    private static object OptionBody(string code, int displayOrder = 0, bool active = true)
        => new
        {
            code,
            name = code.Replace('_', ' '),
            nameTamil = (string?)null,
            helpText = "What the choice means for the finished garment.",
            illustrationKey = (string?)null,
            illustrationAlt = "The shape in words.",
            priceListItemCode = (string?)null,
            timeImpactDays = 0,
            displayOrder,
            active,
            reason = (string?)null,
        };

    private static async Task<Guid> AddOptionAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid groupId,
        string code,
        int displayOrder = 0)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-groups/{groupId}/options",
            OptionBody(code, displayOrder),
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designOptionId").GetGuid();
    }

    private static object NoteRuleAlwaysBody(string note)
        => new
        {
            type = "Note",
            antecedent = new { groupCode = (string?)null, form = "Always", optionCodes = Array.Empty<string>() },
            consequent = (object?)null,
            note,
            why = (string?)null,
            reason = (string?)null,
        };

    private static object NoteRuleBody(string groupCode, string note)
        => new
        {
            type = "Note",
            antecedent = new { groupCode, form = "AnySelection", optionCodes = Array.Empty<string>() },
            consequent = (object?)null,
            note,
            why = (string?)null,
            reason = (string?)null,
        };

    /// <summary>
    /// A service type body complete in every one of the five links, so the published service is genuinely
    /// orderable (<see cref="ICatalogAvailabilityQuery.IsOrderableAsync"/>) and the picker and draft routes
    /// under test — gated on exactly that predicate — have something to answer about.
    /// </summary>
    /// <remarks>
    /// The measurement template must be real and published: Customers' own
    /// <c>MeasurementTemplateCatalogValidator</c> refuses publication otherwise. The workflow definition
    /// and QC checklist links have no registered validator yet and are accepted as bare identifiers. The
    /// price-list item code is real-shaped but unbacked; <c>PriceListCatalogValidator</c> only warns on a
    /// branch no price-list version prices at all, which every fixture branch here is — the Billing suite
    /// prices only the branches it opens for itself.
    /// </remarks>
    private static object ServiceBody(string code, IReadOnlyList<Guid> groupIds, Guid measurementTemplateId)
        => new
        {
            code,
            name = "Stitching",
            nameTamil = (string?)null,
            description = "A new garment cut and stitched from the customer's material.",
            displayOrder = 0,
            expectedDurationDays = 7,
            intakeWarning = (string?)null,
            measurementTemplateId = (Guid?)measurementTemplateId,
            workflowDefinitionId = (Guid?)Guid.CreateVersion7(),
            designOptionGroupIds = groupIds,
            priceListItemCode = $"PI_{code}",
            qcChecklistTemplateId = (Guid?)Guid.CreateVersion7(),
            allowIncomplete = false,
            activeFrom = (DateOnly?)null,
            activeTo = (DateOnly?)null,
            branchIds = new[] { HomeBranch },
            reason = (string?)null,
        };

    private static async Task<Guid> AddServiceAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string code,
        IReadOnlyList<Guid> groupIds,
        Guid measurementTemplateId)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/service-types",
            ServiceBody(code, groupIds, measurementTemplateId),
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("serviceTypeId").GetGuid();
    }

    /// <summary>A measurement template with one published version, so a service type may link to it and publish.</summary>
    private async Task<Guid> PublishedTemplateAsync(string prefix)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var organisationId = SessionTestData.OrganisationId;
        var code = $"MT_SEL_{prefix.Replace('-', '_').ToUpperInvariant()}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        var template = await handler.CreateAsync(
            new CreateMeasurementTemplateCommand(organisationId, code, code, null, null), Token);
        template.IsSuccess.ShouldBeTrue(template.IsFailure ? template.Error.Message : string.Empty);

        var templateId = template.Value.Template.Id;

        var draft = await handler.StartDraftAsync(
            new StartTemplateDraftCommand(templateId, organisationId, "Version 1", null, DisplayUnit.Inch, null, null),
            Token);
        draft.IsSuccess.ShouldBeTrue(draft.IsFailure ? draft.Error.Message : string.Empty);

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
                    "Body measurement. Round the fullest part of the bust, tape level at the back.",
                    "blouse_front_v1",
                    null,
                    "Round the fullest part of the bust, tape level at the back.",
                    null,
                    []),
                null,
                null),
            Token);
        field.IsSuccess.ShouldBeTrue(field.IsFailure ? field.Error.Message : string.Empty);

        var command = new TemplateLifecycleCommand(templateId, versionId, organisationId, "Fixture.", null, null);
        (await handler.SubmitAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.PublishAsync(command, Token)).IsSuccess.ShouldBeTrue();

        return templateId;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record FixtureCatalogue(Guid VersionId, Guid CategoryId, Guid ServiceTypeId);

    /// <summary>
    /// Builds and publishes a catalogue with two service types of one category, each linking only a
    /// subset of its groups (#140, the finding that a category-wide check leaks across services): cut
    /// (A, B) is shared by both; trim (PIPING only) belongs only to service A, and DR-01 requires it
    /// whenever cut = B, settling it on the customer's behalf since it has exactly one admissible option;
    /// lining (NONE, FULL — required) belongs only to service B.
    /// </summary>
    private async Task<MultiServiceFixture> BuildMultiServiceCatalogueAsync(
        AdministrationHarness.AdministratorClient owner, string prefix)
    {
        var measurementTemplateId = await PublishedTemplateAsync(prefix);
        var version = await DraftAsync(owner, $"{prefix} catalogue");
        var category = await AddCategoryAsync(
            owner, version, Code($"{prefix.Replace('-', '_').ToUpperInvariant()}_CAT"));
        var cut = await AddGroupAsync(owner, version, category, "cut", required: false);
        var trim = await AddGroupAsync(owner, version, category, "trim", required: false, displayOrder: 1);
        var lining = await AddGroupAsync(owner, version, category, "lining", required: true, displayOrder: 2);
        await AddOptionAsync(owner, version, cut, "STYLE_A");
        await AddOptionAsync(owner, version, cut, "STYLE_B", displayOrder: 1);
        await AddOptionAsync(owner, version, trim, "PIPING");
        await AddOptionAsync(owner, version, lining, "NONE");
        await AddOptionAsync(owner, version, lining, "FULL", displayOrder: 1);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/design-rules",
                new
                {
                    type = "Requires",
                    antecedent = new { groupCode = "cut", form = "Equals", optionCodes = OptionStyleB },
                    consequent = new { groupCode = "trim", form = "Equals", optionCodes = PipingOnly },
                    note = (string?)null,
                    why = "A style-B cut is always finished with piped trim.",
                    reason = (string?)null,
                },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var serviceA = await AddServiceAsync(
            owner, version, category, "STITCHING_A", [cut, trim], measurementTemplateId);
        var serviceB = await AddServiceAsync(
            owner, version, category, "STITCHING_B", [cut, lining], measurementTemplateId);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved for the multi-service tests." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return new MultiServiceFixture(version, category, serviceA, serviceB);
    }

    private sealed record MultiServiceFixture(Guid VersionId, Guid CategoryId, Guid ServiceAId, Guid ServiceBId);

    /// <summary>
    /// Builds and publishes a catalogue with a two-hop requires chain over three groups of one category
    /// (#140, the transitive-scoping finding): cut (A, B) requires trim = PIPING whenever cut = B, and
    /// trim = PIPING in turn requires edging = BOUND — each consequent has exactly one admissible option,
    /// so both settle on the customer's behalf. Service X links only cut and edging, never trim; service
    /// Y links all three.
    /// </summary>
    private async Task<ChainedRuleFixture> BuildChainedRuleCatalogueAsync(
        AdministrationHarness.AdministratorClient owner, string prefix)
    {
        var measurementTemplateId = await PublishedTemplateAsync(prefix);
        var version = await DraftAsync(owner, $"{prefix} catalogue");
        var category = await AddCategoryAsync(
            owner, version, Code($"{prefix.Replace('-', '_').ToUpperInvariant()}_CAT"));
        var cut = await AddGroupAsync(owner, version, category, "cut", required: false);
        var trim = await AddGroupAsync(owner, version, category, "trim", required: false, displayOrder: 1);
        var edging = await AddGroupAsync(owner, version, category, "edging", required: false, displayOrder: 2);
        await AddOptionAsync(owner, version, cut, "STYLE_A");
        await AddOptionAsync(owner, version, cut, "STYLE_B", displayOrder: 1);
        await AddOptionAsync(owner, version, trim, "PIPING");
        await AddOptionAsync(owner, version, edging, "BOUND");

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/design-rules",
                new
                {
                    type = "Requires",
                    antecedent = new { groupCode = "cut", form = "Equals", optionCodes = OptionStyleB },
                    consequent = new { groupCode = "trim", form = "Equals", optionCodes = PipingOnly },
                    note = (string?)null,
                    why = "A style-B cut is always finished with piped trim.",
                    reason = (string?)null,
                },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/design-rules",
                new
                {
                    type = "Requires",
                    antecedent = new { groupCode = "trim", form = "Equals", optionCodes = PipingOnly },
                    consequent = new { groupCode = "edging", form = "Equals", optionCodes = BoundOnly },
                    note = (string?)null,
                    why = "Piped trim is always finished with a bound edge.",
                    reason = (string?)null,
                },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var serviceX = await AddServiceAsync(
            owner, version, category, "STITCHING_X", [cut, edging], measurementTemplateId);
        var serviceY = await AddServiceAsync(
            owner, version, category, "STITCHING_Y", [cut, trim, edging], measurementTemplateId);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved for the chained-rule tests." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return new ChainedRuleFixture(version, category, serviceX, serviceY);
    }

    private sealed record ChainedRuleFixture(Guid VersionId, Guid CategoryId, Guid ServiceXId, Guid ServiceYId);
}
