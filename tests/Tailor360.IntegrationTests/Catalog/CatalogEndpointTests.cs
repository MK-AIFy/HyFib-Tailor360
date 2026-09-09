using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The catalogue over HTTP: drafting a version, publishing it, and what a branch may then order.
/// </summary>
/// <remarks>
/// <para>
/// These run against a real PostgreSQL because most of what is under test is a property of the
/// database rather than of the application: the immutability of a published version is a trigger, the
/// one-published-version rule is a partial unique index, and the concurrent publish is a race only the
/// database can settle. A test with an in-memory store would prove that the C# refuses what the C#
/// refuses.
/// </para>
/// <para>
/// The acceptance criteria of issue #29 are the shape of this file. "An administrator adds a new
/// category without code changes" is the first test and is deliberately written as the demonstration:
/// a draft, a category, a service type, a publication, and the category appearing at the counter.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CatalogEndpointTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task AnAdministratorAddsACategoryAndACounterCanOrderItWithoutADeployment()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-add", "203.0.113.200");

        var version = await DraftAsync(owner, "Adds a category");
        var category = await AddCategoryAsync(owner, version, Code("GOWN"), branches: [HomeBranch]);
        await AddServiceAsync(owner, version, category, "STITCHING", branches: [HomeBranch]);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "The owner workshop approved the gown line." },
            Key());

        published.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var counter = await ReaderAsync("cat-read", "203.0.113.201");

        var current = await counter.GetAsync("/api/v1/catalog/current");

        current.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await current.Content.ReadAsStringAsync(Token));
        var services = body.RootElement.GetProperty("services").EnumerateArray().ToArray();

        services.ShouldContain(
            service => service.GetProperty("categoryCode").GetString() == Code("GOWN"),
            "the category an administrator added a moment ago is orderable at the counter, and "
            + "nothing was deployed in between");

        var gown = services.Single(
            service => service.GetProperty("categoryCode").GetString() == Code("GOWN"));

        gown.GetProperty("serviceCode").GetString().ShouldBe("STITCHING");
        gown.GetProperty("qualifiedReference").GetString().ShouldBe($"{Code("GOWN")}.STITCHING");
        gown.GetProperty("expectedDurationDays").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task RefusesToPublishADraftWithAServiceThatHasNoLinks()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-incomplete", "203.0.113.202");

        var version = await DraftAsync(owner, "A service with nothing behind it");
        var category = await AddCategoryAsync(owner, version, Code("SALWAR"), branches: [HomeBranch]);
        await AddServiceAsync(
            owner, version, category, "STITCHING", branches: [HomeBranch], complete: false);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "Trying to publish before the price list exists." },
            Key());

        published.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(await published.Content.ReadAsStringAsync(Token));

        problem.RootElement.GetProperty("code").GetString()
            .ShouldBe("catalog.publish-validation-failed");

        var findings = problem.RootElement.GetProperty("findings").EnumerateArray().ToArray();

        findings.ShouldContain(
            finding => finding.GetProperty("code").GetString() == "catalog.service-type-missing-link",
            "the refusal names every reason, each with the path a screen can focus");

        findings.ShouldAllBe(finding => finding.GetProperty("validator").GetString() == "catalog");
    }

    [Fact]
    public async Task PublishesAnIncompleteServiceTheAdministratorAcceptedAndKeepsItOffTheCounter()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-allow", "203.0.113.203");

        var version = await DraftAsync(owner, "Reviewed before the price list is ready");
        var category = await AddCategoryAsync(owner, version, Code("KIDS"), branches: [HomeBranch]);
        await AddServiceAsync(
            owner, version, category, "STITCHING",
            branches: [HomeBranch], complete: false, allowIncomplete: true);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "The category is reviewed; its rates follow next week." },
            Key());

        published.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await published.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("findings").EnumerateArray()
            .ShouldContain(
                finding => finding.GetProperty("code").GetString() == "catalog.service-type-incomplete",
                "publishing says what it published, not only that it published");

        using var counter = await ReaderAsync("cat-allow-read", "203.0.113.204");
        using var current = JsonDocument.Parse(
            await (await counter.GetAsync("/api/v1/catalog/current")).Content.ReadAsStringAsync(Token));

        current.RootElement.GetProperty("services").EnumerateArray()
            .ShouldNotContain(
                service => service.GetProperty("categoryCode").GetString() == Code("KIDS"),
                "a service published with a link missing is flagged not orderable, and intake neither "
                + "lists nor accepts it");
    }

    [Fact]
    public async Task RefusesToEditAPublishedVersionAndAcceptsALabelCorrection()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-immutable", "203.0.113.205");

        var version = await DraftAsync(owner, "Immutability");
        var category = await AddCategoryAsync(owner, version, Code("LEHENGA"), branches: [HomeBranch]);
        await AddServiceAsync(owner, version, category, "STITCHING", branches: [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved at the owner workshop." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var edited = await owner.PutAsync(
            $"/api/v1/catalog/versions/{version}/categories/{category}",
            CategoryBody(Code("LEHENGA"), [HomeBranch], name: "Lehenga renamed"),
            Key());

        edited.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(edited)).ShouldBe("catalog.version-not-editable");

        var corrected = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{category}/presentation",
            new
            {
                name = "Lehenga — bridal",
                nameTamil = (string?)null,
                description = "Bridal and festive lehenga sets.",
                displayOrder = 3,
                reason = "The counter reads 'bridal' to customers; the label now matches.",
            },
            Key());

        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync(Token));
        var read = body.RootElement.GetProperty("categories").EnumerateArray().Single();

        read.GetProperty("name").GetString().ShouldBe("Lehenga — bridal");
        read.GetProperty("code").GetString().ShouldBe(
            Code("LEHENGA"), "a correction changes what is shown and never what anything refers to");
    }

    [Fact]
    public async Task RefusesAPresentationCorrectionThatCarriesNoReason()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-reason", "203.0.113.213");

        var version = await DraftAsync(owner, "Reasoned");
        var category = await AddCategoryAsync(owner, version, Code("KURTA"), branches: [HomeBranch]);
        var service = await AddServiceAsync(owner, version, category, "STITCHING", [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved at the owner workshop." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A correction is the single mutation a published version admits, so the reason is the only
        // record of why a label a customer was quoted from reads differently today. `reasonRequired`
        // on the endpoint documents that; it does not enforce it, and this is the enforcement.
        foreach (var reason in new[] { (string?)null, string.Empty, "   " })
        {
            var refusedCategory = await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/categories/{category}/presentation",
                Presentation("Kurta — renamed", reason),
                Key());

            refusedCategory.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeOfAsync(refusedCategory)).ShouldBe("catalog.value-required");

            var refusedService = await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/service-types/{service}/presentation",
                Presentation("Stitching — renamed", reason),
                Key());

            refusedService.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeOfAsync(refusedService)).ShouldBe("catalog.value-required");
        }

        var read = await owner.GetAsync($"/api/v1/catalog/versions/{version}");

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("categories").EnumerateArray().Single()
            .GetProperty("name").GetString()
            .ShouldBe(Code("KURTA"), "a refused correction leaves the published label alone");
    }

    [Fact]
    public async Task RefusesAnInsertIntoAPublishedVersionAtTheDatabase()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-insert", "203.0.113.214");

        var version = await DraftAsync(owner, "Insert");
        var category = await AddCategoryAsync(owner, version, Code("SHERWANI"), branches: [HomeBranch]);
        await AddServiceAsync(owner, version, category, "STITCHING", [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Approved at the owner workshop." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // Freezing every column of every existing row says nothing about a row that was not there
        // before. Reaching the table directly and adding a category to a published version would
        // change what that version offers — and what its finished orders render from — without
        // cloning, validating or publishing anything.
        var refused = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO catalog.categories
                     (id, category_key, catalog_version_id, organisation_id, code, name,
                      display_order)
                 VALUES
                     ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {version},
                      {SessionTestData.OrganisationId}, {Code("SMUGGLED")}, 'Smuggled in', 0)
                 """,
                Token));

        refused.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        refused.MessageText.ShouldContain("published or retired catalogue version");

        var read = await owner.GetAsync($"/api/v1/catalog/versions/{version}");

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("categories").EnumerateArray().Count()
            .ShouldBe(1, "the refused insert added nothing");
    }

    [Fact]
    public async Task RefusesASecondDraftThatTookTheSameVersionNumber()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var number = await store.NextVersionNumberAsync(SessionTestData.OrganisationId, Token);

        // Two administrators starting a draft in the same moment both read this maximum and both
        // choose the number after it. Both drafts are added to one context here because that is the
        // deterministic way to make the index fire — the production race is two requests, and what is
        // under test is the same either way: the loser is answered a conflict rather than the five
        // hundred an uncaught DbUpdateException would be.
        foreach (var name in new[] { "First past the post", "Second past the post" })
        {
            var draft = CatalogVersion.CreateDraft(
                ids.NewId(), SessionTestData.OrganisationId, number, name, null, clock.UtcNow, null);

            draft.IsSuccess.ShouldBeTrue();
            store.Add(draft.Value);
        }

        var saved = await store.SaveDraftAsync(Token);

        saved.IsFailure.ShouldBeTrue();
        saved.Error.Code.ShouldBe("catalog.draft-number-conflict");
    }

    [Fact]
    public async Task PublishingSupersedesTheVersionBeforeItAndBothStayReadable()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-supersede", "203.0.113.206");

        var first = await DraftAsync(owner, "First");
        var firstCategory = await AddCategoryAsync(
            owner, first, Code("BLOUSE"), branches: [HomeBranch]);
        await AddServiceAsync(owner, first, firstCategory, "STITCHING", branches: [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{first}/publish",
                new { reason = "First publication." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await CloneAsync(owner, first, "Second");

        var publishedSecond = await owner.PostAsync(
            $"/api/v1/catalog/versions/{second}/publish",
            new { reason = "Second publication, superseding the first." },
            Key());

        publishedSecond.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await publishedSecond.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("supersededVersionId").GetGuid().ShouldBe(first);

        // The superseded version is retired and stays fully readable, which is what makes an order
        // pinned to it still render.
        using var old = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{first}")).Content
                .ReadAsStringAsync(Token));

        old.RootElement.GetProperty("version").GetProperty("status").GetString().ShouldBe("Retired");
        old.RootElement.GetProperty("categories").EnumerateArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task RefusesASecondPublicationOfTheSameDraft()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-twice", "203.0.113.207");

        var version = await DraftAsync(owner, "Published once");
        var category = await AddCategoryAsync(owner, version, Code("GOWN2"), branches: [HomeBranch]);
        await AddServiceAsync(owner, version, category, "STITCHING", branches: [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "First time." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await owner.PostAsync(
            $"/api/v1/catalog/versions/{version}/publish",
            new { reason = "Second time." },
            Key());

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(again)).ShouldBe("catalog.version-not-publishable");
    }

    [Fact]
    public async Task RetiringTheOnlyPublishedVersionLeavesNothingOrderableAndEverythingReadable()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-retire", "203.0.113.208");

        var version = await DraftAsync(owner, "Retired later");
        var category = await AddCategoryAsync(owner, version, Code("SAREE"), branches: [HomeBranch]);
        await AddServiceAsync(owner, version, category, "STITCHING", branches: [HomeBranch]);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Published." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.PostAsync(
                $"/api/v1/catalog/versions/{version}/retire",
                new { reason = "Closing the saree line for the season." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var counter = await ReaderAsync("cat-retire-read", "203.0.113.209");
        using var current = JsonDocument.Parse(
            await (await counter.GetAsync("/api/v1/catalog/current")).Content.ReadAsStringAsync(Token));

        current.RootElement.GetProperty("services").EnumerateArray()
            .ShouldNotContain(
                service => service.GetProperty("categoryCode").GetString() == Code("SAREE"));

        using var readBack = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}")).Content
                .ReadAsStringAsync(Token));

        readBack.RootElement.GetProperty("version").GetProperty("status").GetString()
            .ShouldBe("Retired");
        readBack.RootElement.GetProperty("categories").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("code").GetString()
            .ShouldBe(Code("SAREE"), "a retired version renders exactly as it did");
    }

    [Fact]
    public async Task RefusesEveryCatalogueRouteToACallerHoldingNothing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var nobody = await AdministrationHarness.AdministratorAsync(
            fixture, "cat-none", "203.0.113.210", grantPermission: null);

        (await nobody.GetAsync("/api/v1/catalog/versions")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/catalog/current")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.PostAsync("/api/v1/catalog/versions", new { name = "Not allowed" }, Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefusesToPublishWithOnlyTheEditPermission()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var drafter = await AdministrationHarness.AdministratorAsync(
            fixture, "cat-drafter", "203.0.113.211", CatalogPermissions.Edit);

        var version = await DraftAsync(drafter, "Drafted by somebody who may not publish");

        (await drafter.PostAsync(
                $"/api/v1/catalog/versions/{version}/publish",
                new { reason = "Not mine to do." },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReportsWhatIsWrongWithADraftWithoutChangingIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("cat-preview", "203.0.113.212");

        var version = await DraftAsync(owner, "Preview");
        var blouse = await AddCategoryAsync(owner, version, Code("BL"), branches: [HomeBranch]);
        await AddCategoryAsync(
            owner, version, Code("BLSUB"), branches: [HomeBranch], parentId: blouse);

        // A service on the grouping node: it could never be ordered, so publication refuses it.
        await AddServiceAsync(owner, version, blouse, "STITCHING", branches: [HomeBranch]);

        var report = await owner.GetAsync($"/api/v1/catalog/versions/{version}/validation");

        report.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await report.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("publishable").GetBoolean().ShouldBeFalse();
        body.RootElement.GetProperty("errorCount").GetInt32().ShouldBeGreaterThan(0);
        body.RootElement.GetProperty("findings").EnumerateArray()
            .ShouldContain(finding =>
                finding.GetProperty("code").GetString() == "catalog.service-type-on-grouping-node");

        using var unchanged = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/v1/catalog/versions/{version}")).Content
                .ReadAsStringAsync(Token));

        unchanged.RootElement.GetProperty("version").GetProperty("status").GetString()
            .ShouldBe("Draft", "a preview reports and changes nothing");
    }

    [Fact]
    public async Task SeedsTheInitialHierarchyOnceAndLeavesItUnpublished()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A fresh organisation, so the seeder's "does this organisation have a catalogue?" question
        // has the answer a first install gives it.
        var organisationId = Guid.CreateVersion7();

        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ICatalogReferenceDataSeeder>();

        var first = await seeder.SeedInitialCatalogAsync(organisationId, Token);

        first.Created.ShouldBeTrue();
        first.CategoryCount.ShouldBe(7);
        first.ServiceTypeCount.ShouldBe(18);
        first.AwaitingPublication.ShouldBeTrue(
            "publishing is an Owner's act with a second factor and a stated reason, and the hierarchy "
            + "itself is open decision OD-CAT-01");

        var second = await seeder.SeedInitialCatalogAsync(organisationId, Token);

        second.Created.ShouldBeFalse("running it twice must not create a second catalogue version");
        second.CatalogVersionId.ShouldBe(first.CatalogVersionId);
        second.CategoryCount.ShouldBe(7);
        second.ServiceTypeCount.ShouldBe(18);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A fresh idempotency key. Every command route in this module declares
    /// <c>RequireIdempotency</c>, so a request without one is refused before it reaches a handler —
    /// which is the rule working, and is why the tests carry one rather than the endpoints relaxing.
    /// </summary>
    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static Guid HomeBranch => SessionTestData.HomeBranchId;

    /// <summary>A code unique to this run, so two runs against one database do not collide.</summary>
    private static string Code(string stem) => $"{stem}_{RunToken}";

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(
        string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Edit, CatalogPermissions.Publish);

    private Task<AdministrationHarness.AdministratorClient> ReaderAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Read);

    private static async Task<Guid> DraftAsync(
        AdministrationHarness.AdministratorClient client, string name)
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
        AdministrationHarness.AdministratorClient client,
        Guid version,
        string code,
        IReadOnlyList<Guid> branches,
        Guid? parentId = null)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories",
            CategoryBody(code, branches, parentId: parentId),
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("categoryId").GetGuid();
    }

    private static async Task<Guid> AddServiceAsync(
        AdministrationHarness.AdministratorClient client,
        Guid version,
        Guid categoryId,
        string code,
        IReadOnlyList<Guid> branches,
        bool complete = true,
        bool allowIncomplete = false)
    {
        var response = await client.PostAsync(
            $"/api/v1/catalog/versions/{version}/categories/{categoryId}/service-types",
            new
            {
                code,
                name = "Stitching",
                nameTamil = (string?)null,
                description = "A new garment cut and stitched from the customer's material.",
                displayOrder = 0,
                expectedDurationDays = 7,
                intakeWarning = (string?)null,
                measurementTemplateId = complete ? Guid.CreateVersion7() : (Guid?)null,
                workflowDefinitionId = complete ? Guid.CreateVersion7() : (Guid?)null,
                designOptionGroupIds = Array.Empty<Guid>(),
                priceListItemCode = complete ? "PL-SYNTHETIC" : null,
                qcChecklistTemplateId = complete ? Guid.CreateVersion7() : (Guid?)null,
                allowIncomplete,
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                branchIds = branches,
                reason = (string?)null,
            },
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("serviceTypeId").GetGuid();
    }

    private static object Presentation(string name, string? reason)
        => new
        {
            name,
            nameTamil = (string?)null,
            description = "A synthetic correction written by a test.",
            displayOrder = 1,
            reason,
        };

    private static object CategoryBody(
        string code,
        IReadOnlyList<Guid> branches,
        Guid? parentId = null,
        string? name = null)
        => new
        {
            code,
            name = name ?? code,
            nameTamil = (string?)null,
            description = "A synthetic category written by a test.",
            parentCategoryId = parentId,
            displayOrder = 0,
            activeFrom = (DateOnly?)null,
            activeTo = (DateOnly?)null,
            featureFlagKey = (string?)null,
            branchIds = branches,
            reason = "Written by an integration test.",
        };

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
