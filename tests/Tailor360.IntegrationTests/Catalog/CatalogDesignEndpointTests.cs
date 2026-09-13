using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The design catalogue's administration routes (#30, #137): groups, options and rules on a draft,
/// their words on a published version, and the refusals — a stale tag, a colliding code, a group of
/// another category, a change to a published version at the API and at the database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CatalogDesignEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();
    private static readonly string[] PaddingCodes = ["LIGHT", "MOULDED_CUP"];
    private static readonly string[] FullOnly = ["FULL"];

    [Fact]
    public async Task AnAdministratorAddsAGroupItsOptionsAndARuleAndTheVersionReadsThemBack()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-add", "203.0.113.220");
        var version = await DraftAsync(owner, "Adds a design group");
        var blouse = await AddCategoryAsync(owner, version, Code("BLOUSE_PATTERN"));

        var padding = await AddGroupAsync(owner, version, blouse, "padding", required: false);
        var lining = await AddGroupAsync(owner, version, blouse, "lining", displayOrder: 1);
        await AddOptionAsync(owner, version, padding, "LIGHT");
        await AddOptionAsync(owner, version, padding, "MOULDED_CUP");
        await AddOptionAsync(owner, version, lining, "NONE");
        await AddOptionAsync(owner, version, lining, "FULL", displayOrder: 1);

        var added = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{blouse}/design-rules",
            new
            {
                type = "Requires",
                antecedent = new { groupCode = "padding", form = "In", optionCodes = PaddingCodes },
                consequent = new { groupCode = "lining", form = "Equals", optionCodes = FullOnly },
                note = (string?)null,
                why = "Padding stitched against a single layer shows through and works loose.",
                reason = (string?)null,
            },
            await VersionKeyAsync(owner, version));
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var rule = JsonDocument.Parse(await added.Content.ReadAsStringAsync(Token));
        // Numbers are allocated across the organisation's whole catalogue and never re-used, so what
        // earlier tests and runs allocated counts; the shape is the property, not the value.
        var number = rule.RootElement.GetProperty("number").GetInt32();
        number.ShouldBeGreaterThanOrEqualTo(1);
        rule.RootElement.GetProperty("identifier").GetString().ShouldBe($"DR-{number:00}");
        rule.RootElement.GetProperty("statement").GetString()
            .ShouldBe("padding in (LIGHT, MOULDED_CUP) requires lining = FULL");
        rule.RootElement.GetProperty("blocks").GetBoolean().ShouldBeTrue();

        using var read = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}")).Content.ReadAsStringAsync(Token));
        var groups = read.RootElement.GetProperty("designGroups").EnumerateArray().ToArray();
        groups.Select(group => group.GetProperty("code").GetString()).ShouldBe(["padding", "lining"]);
        groups[1].GetProperty("options").EnumerateArray()
            .Select(option => option.GetProperty("code").GetString())
            .ShouldBe(["NONE", "FULL"]);
        groups[0].GetProperty("selectionMode").GetString().ShouldBe("SingleChoice");
        read.RootElement.GetProperty("designRules").EnumerateArray().Single()
            .GetProperty("antecedent").GetProperty("optionCodes").EnumerateArray().Count().ShouldBe(2);
    }

    [Fact]
    public async Task AllocatesRuleNumbersAcrossVersionsAndNeverReusesOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-number", "203.0.113.221");
        var first = await DraftAsync(owner, "Numbers");
        var blouse = await AddCategoryAsync(owner, first, Code("SALWAR"));
        await AddGroupAsync(owner, first, blouse, "neck");

        var one = await AddNoteRuleAsync(owner, first, blouse, "First note.");
        var two = await AddNoteRuleAsync(owner, first, blouse, "Second note.");
        two.Number.ShouldBeGreaterThan(one.Number);

        // The highest number is removed — the case a "highest plus one" allocation would get wrong.
        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{first}/design-rules/{two.Id}/delete",
                new { reason = "Withdrawn." },
                await VersionKeyAsync(owner, first)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Numbers are unique across the organisation's catalogue, so what other tests allocated
        // counts too; the property under test is order and non-reuse, not the absolute values.
        var three = await AddNoteRuleAsync(owner, first, blouse, "Third note.");
        three.Number.ShouldBeGreaterThan(two.Number, "a retired number is never given to another rule");

        // A clone carries the numbers it had, and the next allocation in the clone continues after them.
        var second = await CloneAsync(owner, first, "Cloned");
        using var cloned = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{second}")).Content.ReadAsStringAsync(Token));
        cloned.RootElement.GetProperty("designRules").EnumerateArray()
            .Select(rule => rule.GetProperty("number").GetInt32())
            .ShouldBe([one.Number, three.Number]);
        var clonedCategory = cloned.RootElement.GetProperty("categories").EnumerateArray().Single()
            .GetProperty("categoryId").GetGuid();
        var four = await AddNoteRuleAsync(owner, second, clonedCategory, "Fourth note.");
        four.Number.ShouldBeGreaterThan(three.Number);
    }

    [Fact]
    public async Task RefusesAGroupCodeCollisionAndAStaleTag()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-collide", "203.0.113.222");
        var version = await DraftAsync(owner, "Collisions");
        var blouse = await AddCategoryAsync(owner, version, Code("GOWN"));
        var staleKey = await VersionKeyAsync(owner, version);
        await AddGroupAsync(owner, version, blouse, "neckline");

        var collided = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{blouse}/design-groups",
            GroupBody("neckline"),
            await VersionKeyAsync(owner, version));
        collided.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(collided)).ShouldBe("catalog.code-not-unique");

        var stale = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{blouse}/design-groups",
            GroupBody("sleeve_style"),
            staleKey);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(stale)).ShouldBe("catalog.version-changed");
    }

    [Fact]
    public async Task RefusesAtPublicationAServiceTypeOfferingAGroupOfAnotherCategory()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-xcat", "203.0.113.223");
        var version = await DraftAsync(owner, "A group of another category");
        var blouse = await AddCategoryAsync(owner, version, Code("BLOUSE_AARI"));
        var gown = await AddCategoryAsync(owner, version, Code("GOWN2"));
        var gownNeck = await AddGroupAsync(owner, version, gown, "neckline");
        await AddOptionAsync(owner, version, gownNeck, "ROUND");

        var linked = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{blouse}/service-types",
            ServiceBody("STITCHING", [gownNeck]),
            await VersionKeyAsync(owner, version));
        linked.StatusCode.ShouldBe(HttpStatusCode.Created, "the draft accepts it; publication decides");

        using var report = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}/validation")).Content
                .ReadAsStringAsync(Token));
        report.RootElement.GetProperty("publishable").GetBoolean().ShouldBeFalse();
        report.RootElement.GetProperty("findings").EnumerateArray()
            .ShouldContain(
                finding => finding.GetProperty("code").GetString() == "catalog.design-group-of-another-category",
                "a gown's neckline is not a blouse's to offer");
    }

    [Fact]
    public async Task RefusesChangesOnAPublishedVersionAndAcceptsACorrectionOfAnOptionsWords()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-published", "203.0.113.224");
        var version = await DraftAsync(owner, "Published design");
        var blouse = await AddCategoryAsync(owner, version, Code("LEHENGA"));
        var skirt = await AddGroupAsync(owner, version, blouse, "skirt_style");
        var option = await AddOptionAsync(owner, version, skirt, "KALI");
        await AddServiceAsync(owner, version, blouse, "STITCHING", [skirt]);
        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved at the owner workshop." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var edited = await owner.PutAsync(
            $"/api/v1/catalog/versions/{version}/design-groups/{skirt}",
            GroupBody("skirt_style", name: "Skirt style renamed"),
            await VersionKeyAsync(owner, version));
        edited.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(edited)).ShouldBe("catalog.version-not-editable");

        var withoutReason = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-options/{option}/presentation",
            OptionPresentation("Kali skirt", reason: null),
            await VersionKeyAsync(owner, version));
        withoutReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corrected = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-options/{option}/presentation",
            OptionPresentation("Kali skirt", reason: "The counter says 'kali'; the label now matches."),
            await VersionKeyAsync(owner, version));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync(Token));
        var read = body.RootElement.GetProperty("designGroups").EnumerateArray().Single()
            .GetProperty("options").EnumerateArray().Single();
        read.GetProperty("name").GetString().ShouldBe("Kali skirt");
        read.GetProperty("code").GetString().ShouldBe("KALI", "a correction changes what is shown, never what anything refers to");
    }

    [Fact]
    public async Task RefusesAnInsertIntoAPublishedVersionsOptionsAtTheDatabase()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-trigger", "203.0.113.225");
        var version = await DraftAsync(owner, "Immutable at the database");
        var blouse = await AddCategoryAsync(owner, version, Code("KIDS"));
        var lining = await AddGroupAsync(owner, version, blouse, "lining");
        await AddOptionAsync(owner, version, lining, "FULL");
        await AddServiceAsync(owner, version, blouse, "STITCHING", [lining]);
        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved." },
                await VersionKeyAsync(owner, version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var refused = await Should.ThrowAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO catalog.design_options
                     (id, design_option_key, design_option_group_id, catalog_version_id, organisation_id,
                      code, name, help_text, illustration_alt, time_impact_days, display_order, active)
                 VALUES
                     ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {lining}, {version},
                      {SessionTestData.OrganisationId}, 'SMUGGLED', 'Smuggled in', 'Help.', 'Alt.', 0, 9, true)
                 """,
                Token));
        refused.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        refused.MessageText.ShouldContain("published or retired catalogue version");

        var deleted = await Should.ThrowAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"DELETE FROM catalog.design_option_group_branches WHERE design_option_group_id = {lining}",
                Token));
        deleted.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task RemovingAGroupTakesItsRulesAndTheServiceLinkWithIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-remove", "203.0.113.226");
        var version = await DraftAsync(owner, "Removal");
        var blouse = await AddCategoryAsync(owner, version, Code("BLOUSE_R"));
        var padding = await AddGroupAsync(owner, version, blouse, "padding");
        var lining = await AddGroupAsync(owner, version, blouse, "lining");
        var service = await AddServiceAsync(owner, version, blouse, "STITCHING", [padding, lining]);
        await AddNoteRuleAsync(owner, version, blouse, "Reads padding.", "padding");

        var removed = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-groups/{padding}/delete",
            new { reason = "The shop no longer pads." },
            await VersionKeyAsync(owner, version));
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var read = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}")).Content.ReadAsStringAsync(Token));
        read.RootElement.GetProperty("designGroups").EnumerateArray()
            .Select(group => group.GetProperty("designOptionGroupId").GetGuid()).ShouldBe([lining]);
        read.RootElement.GetProperty("designRules").EnumerateArray().ShouldBeEmpty();
        read.RootElement.GetProperty("serviceTypes").EnumerateArray()
            .Single(type => type.GetProperty("serviceTypeId").GetGuid() == service)
            .GetProperty("designOptionGroupIds").EnumerateArray()
            .Select(id => id.GetGuid()).ShouldBe([lining]);
    }

    [Fact]
    public async Task RefusesEveryDesignRouteToACallerHoldingOnlyTheReadPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("des-deny-o", "203.0.113.227");
        var version = await DraftAsync(owner, "Denied");
        var blouse = await AddCategoryAsync(owner, version, Code("DENIED"));
        var group = await AddGroupAsync(owner, version, blouse, "lining");
        var option = await AddOptionAsync(owner, version, group, "FULL");
        var (rule, _) = await AddNoteRuleAsync(owner, version, blouse, "A note.");
        var key = await VersionKeyAsync(owner, version);

        using var reader = await AdministrationHarness.AdministratorAsync(
            fixture, "des-deny-r", "203.0.113.228", CatalogPermissions.Read);
        var root = $"/api/v1/catalog/versions/{version}";

        foreach (var (method, path) in new[]
                 {
                     ("POST", $"{root}/categories/{blouse}/design-groups"),
                     ("PUT", $"{root}/design-groups/{group}"),
                     ("POST", $"{root}/design-groups/{group}/delete"),
                     ("POST", $"{root}/design-groups/{group}/options"),
                     ("PUT", $"{root}/design-options/{option}"),
                     ("POST", $"{root}/design-options/{option}/delete"),
                     ("POST", $"{root}/categories/{blouse}/design-rules"),
                     ("PUT", $"{root}/design-rules/{rule}"),
                     ("POST", $"{root}/design-rules/{rule}/delete"),
                     ("POST", $"{root}/design-groups/{group}/presentation"),
                     ("POST", $"{root}/design-options/{option}/presentation"),
                 })
        {
            using var refused = method == "PUT"
                ? await reader.PutAsync(path, new { }, key)
                : await reader.PostAsync(path, new { }, key);
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();
        return [.. Key(), ("If-Match", read.Headers.ETag.ToString())];
    }

    private static Guid HomeBranch => SessionTestData.HomeBranchId;

    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Edit, CatalogPermissions.Publish);

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

    private static object GroupBody(
        string code, bool required = true, string? name = null, int displayOrder = 0)
        => new
        {
            code,
            name = name ?? code.Replace('_', ' '),
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
            GroupBody(code, required, displayOrder: displayOrder),
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designOptionGroupId").GetGuid();
    }

    private static async Task<Guid> AddOptionAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid groupId,
        string code,
        int displayOrder = 0)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/design-groups/{groupId}/options",
            new
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
                active = true,
                reason = (string?)null,
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("designOptionId").GetGuid();
    }

    private static async Task<(Guid Id, int Number)> AddNoteRuleAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string note,
        string? groupCode = null)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/design-rules",
            new
            {
                type = "Note",
                antecedent = groupCode is null
                    ? new { groupCode = (string?)null, form = "Always", optionCodes = Array.Empty<string>() }
                    : new { groupCode = (string?)groupCode, form = "AnySelection", optionCodes = Array.Empty<string>() },
                consequent = (object?)null,
                note,
                why = (string?)null,
                reason = (string?)null,
            },
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return (
            body.RootElement.GetProperty("designRuleId").GetGuid(),
            body.RootElement.GetProperty("number").GetInt32());
    }

    private static object ServiceBody(string code, IReadOnlyList<Guid> groupIds)
        => new
        {
            code,
            name = "Stitching",
            nameTamil = (string?)null,
            description = "A new garment cut and stitched from the customer's material.",
            displayOrder = 0,
            expectedDurationDays = 7,
            intakeWarning = (string?)null,
            measurementTemplateId = (Guid?)null,
            workflowDefinitionId = (Guid?)null,
            designOptionGroupIds = groupIds,
            priceListItemCode = (string?)null,
            qcChecklistTemplateId = (Guid?)null,
            // Accepted incomplete: the links into other modules are not what these tests are about,
            // and a service type published incomplete is flagged not orderable rather than refused.
            allowIncomplete = true,
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
        IReadOnlyList<Guid> groupIds)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/service-types",
            ServiceBody(code, groupIds),
            await VersionKeyAsync(client, version));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.GetProperty("serviceTypeId").GetGuid();
    }

    private static object OptionPresentation(string name, string? reason)
        => new
        {
            name,
            nameTamil = (string?)null,
            helpText = "A synthetic correction written by a test.",
            illustrationAlt = "The shape in words, corrected.",
            displayOrder = 1,
            reason,
        };

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
