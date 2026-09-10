using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The reconciliation of INV-MTV-06, end to end through the outbox (issue #91).
/// </summary>
/// <remarks>
/// <para>
/// The race this exists for cannot be produced through the API, because both guards work: the catalogue refuses to
/// publish against a template with no published version, and the template refuses to retire while a published
/// catalogue points at it. What makes the race real is that those two guards run in different modules, each
/// reading the other through a contract and then writing to its own schema — so a retirement whose guard read
/// happened <em>before</em> a catalogue publication committed will itself commit, leaving the catalogue stranded.
/// </para>
/// <para>
/// These tests reproduce exactly that by retiring the version the way the handler does, minus the guard: the same
/// domain transition, the same event, the same save. Faking the outcome — writing a breach row directly — would
/// prove nothing about the thing under test, which is the delivery.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CatalogReconciliationTests(WebApplicationFixture fixture)
{
    private static readonly string RunToken = AdministrationHarness.UniqueToken(6).ToUpperInvariant();

    [Fact]
    public async Task RecordsABreachWhenATemplateIsRetiredOutFromUnderAPublishedCatalogue()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("rec-open", "203.0.113.220");

        var templateId = await PublishedTemplateAsync("OPEN");
        var versionId = await PublishedCatalogueAsync(owner, Code("OPEN"), templateId);

        // Everything the fixture produced is delivered before the interesting event, so the breach below is
        // credited to the retirement rather than to whichever message happened to be at the front.
        await DispatchAsync();

        // The guard is not bypassed by accident: it refuses first, which is the guard working.
        (await RetireThroughHandlerAsync(templateId)).ShouldBeFalse();

        // Now the race, as it actually happens: the retirement's guard read the catalogue before this
        // publication committed, so the retirement commits anyway.
        await RetirePastTheGuardAsync(templateId);
        await DispatchAsync();

        var breaches = await OpenBreachesAsync(versionId);
        var breach = breaches.ShouldHaveSingleItem();

        breach.Code.ShouldBe("catalog.measurement-template-not-published");
        breach.Target.ShouldBe($"serviceTypes[{Code("OPEN")}.STITCHING].measurementTemplateId");
        breach.Validator.ShouldBe("measurement-templates");
        breach.DetectedBecauseOf.ShouldBe(MeasurementTemplateVersionRetired.Type);
    }

    [Fact]
    public async Task RecordsOneBreachHoweverManyTimesTheEventIsDelivered()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("rec-twice", "203.0.113.221");

        var templateId = await PublishedTemplateAsync("TWICE");
        var versionId = await PublishedCatalogueAsync(owner, Code("TWICE"), templateId);

        await DispatchAsync();
        await RetirePastTheGuardAsync(templateId);

        // Delivery is at least once. Two cycles is the ordinary case rather than the unlucky one — and a
        // second row would also move the detection time, which is what an administrator reads as how long
        // the shop was exposed.
        await DispatchAsync();
        var first = (await OpenBreachesAsync(versionId)).ShouldHaveSingleItem().DetectedAt;

        await DispatchAsync();
        var breaches = await OpenBreachesAsync(versionId);

        breaches.ShouldHaveSingleItem().DetectedAt.ShouldBe(first);
    }

    [Fact]
    public async Task ClosesTheBreachWhenTheShopPublishesATemplateVersionAgain()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("rec-heal", "203.0.113.222");

        var templateId = await PublishedTemplateAsync("HEAL");
        var versionId = await PublishedCatalogueAsync(owner, Code("HEAL"), templateId);

        await DispatchAsync();
        await RetirePastTheGuardAsync(templateId);
        await DispatchAsync();

        (await OpenBreachesAsync(versionId)).Count.ShouldBe(1);

        // The fix an administrator would actually make. Nothing else would tell the catalogue the template
        // can be measured against again, which is why the publication event is consumed at all.
        await PublishAnotherVersionAsync(templateId);
        await DispatchAsync();

        (await OpenBreachesAsync(versionId)).ShouldBeEmpty();

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var closed = await context.ReferenceBreaches
            .Where(breach => breach.CatalogVersionId == versionId)
            .SingleAsync(Token);

        // Closed rather than deleted: a template retired and republished twice in a month is a pattern worth
        // seeing, and a row that vanished would leave the second occurrence looking like the first.
        closed.ResolvedAt.ShouldNotBeNull();
        closed.ResolvedBecauseOf.ShouldBe(MeasurementTemplateVersionPublished.Type);
    }

    [Fact]
    public async Task RecordsNothingWhileEveryReferenceStillHolds()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var owner = await OwnerAsync("rec-clean", "203.0.113.223");

        var templateId = await PublishedTemplateAsync("CLEAN");
        var versionId = await PublishedCatalogueAsync(owner, Code("CLEAN"), templateId);

        // Publishing the catalogue is itself one of the three events that runs a reconciliation, so this
        // asserts the ordinary case: a healthy catalogue is checked and nothing is recorded about it.
        await DispatchAsync();

        (await OpenBreachesAsync(versionId)).ShouldBeEmpty();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid HomeBranch => SessionTestData.HomeBranchId;

    private static string Code(string stem) => $"REC{stem}_{RunToken}";

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private Task<AdministrationHarness.AdministratorClient> OwnerAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture, prefix, address, CatalogPermissions.Edit, CatalogPermissions.Publish);

    /// <summary>
    /// Runs dispatcher cycles until nothing is left to deliver.
    /// </summary>
    /// <remarks>
    /// One cycle is not enough, and the reason is the ordering guarantee rather than the batch size: only the
    /// oldest undelivered message of an aggregate is a candidate, so a template that was published, retired and
    /// published again has three messages that take three cycles. A test that ran one cycle would see the
    /// reconciliation credited to whichever event happened to be at the front, which is not what it means to
    /// assert.
    /// </remarks>
    private async Task DispatchAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

        // Bounded, so a message that fails and reschedules cannot spin here for the length of the test run.
        for (var cycle = 0; cycle < 20; cycle++)
        {
            if (await dispatcher.RunCycleAsync("reconciliation-test", Token) == 0)
            {
                return;
            }
        }
    }

    /// <summary>Every breach of one version that is still standing.</summary>
    private async Task<IReadOnlyList<Modules.Catalog.Domain.Catalogue.CatalogReferenceBreach>>
        OpenBreachesAsync(Guid catalogVersionId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return await context.ReferenceBreaches
            .Where(breach => breach.CatalogVersionId == catalogVersionId && breach.ResolvedAt == null)
            .OrderBy(breach => breach.Target)
            .ToListAsync(Token);
    }

    /// <summary>Drafts a catalogue with one category and one service type, and publishes it.</summary>
    private static async Task<Guid> PublishedCatalogueAsync(
        AdministrationHarness.AdministratorClient owner,
        string categoryCode,
        Guid measurementTemplateId)
    {
        var draft = await owner.PostAsync(
            "/api/v1/catalog/versions", new { name = $"Reconciliation {categoryCode}" }, Key());

        draft.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var draftBody = JsonDocument.Parse(await draft.Content.ReadAsStringAsync(Token));
        var versionId = draftBody.RootElement
            .GetProperty("version").GetProperty("catalogVersionId").GetGuid();

        var category = await owner.PostAsync(
            $"/api/v1/catalog/versions/{versionId}/categories",
            new
            {
                code = categoryCode,
                name = categoryCode,
                nameTamil = (string?)null,
                description = "A synthetic category written by a test.",
                displayOrder = 0,
                parentCategoryId = (Guid?)null,
                branchIds = new[] { HomeBranch },
                activeFrom = (DateOnly?)null,
                activeTo = (DateOnly?)null,
                featureFlagKey = (string?)null,
                reason = (string?)null,
            },
            await VersionKeyAsync(owner, versionId));

        category.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var categoryBody = JsonDocument.Parse(await category.Content.ReadAsStringAsync(Token));
        var categoryId = categoryBody.RootElement.GetProperty("categoryId").GetGuid();

        var service = await owner.PostAsync(
            $"/api/v1/catalog/versions/{versionId}/categories/{categoryId}/service-types",
            new
            {
                code = "STITCHING",
                name = "Stitching",
                nameTamil = (string?)null,
                description = "A synthetic service written by a test.",
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
                branchIds = new[] { HomeBranch },
                reason = (string?)null,
            },
            await VersionKeyAsync(owner, versionId));

        service.StatusCode.ShouldBe(HttpStatusCode.Created);

        var published = await owner.PostAsync(
            $"/api/v1/catalog/versions/{versionId}/publish",
            new { reason = "A synthetic publication written by a test." },
            await VersionKeyAsync(owner, versionId));

        published.StatusCode.ShouldBe(HttpStatusCode.OK);

        return versionId;
    }

    private static async Task<(string Name, string Value)[]> VersionKeyAsync(
        AdministrationHarness.AdministratorClient client, Guid version)
    {
        using var read = await client.GetAsync($"/api/v1/catalog/versions/{version}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();

        return [.. Key(), ("If-Match", read.Headers.ETag.ToString())];
    }

    /// <summary>Asks the handler to retire the published version, and answers whether it agreed.</summary>
    private async Task<bool> RetireThroughHandlerAsync(Guid templateId)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var store = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateStore>();

        var template = await store.FindAsync(templateId, SessionTestData.OrganisationId, Token);
        var versionId = template!.PublishedVersion!.Id;

        var result = await handler.RetireAsync(
            new TemplateLifecycleCommand(
                templateId, versionId, SessionTestData.OrganisationId, "Fixture.", null, null),
            Token);

        return result.IsSuccess;
    }

    /// <summary>
    /// Retires the published version the way the handler does, minus the guard.
    /// </summary>
    /// <remarks>
    /// The same domain transition, the same event and the same save. This is what a retirement whose guard read
    /// the catalogue before the publication committed actually does — the guard is not a lock, and a read that
    /// was true when it ran is what leaves this write free to commit.
    /// </remarks>
    private async Task RetirePastTheGuardAsync(Guid templateId)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMeasurementTemplateStore>();
        var events = scope.ServiceProvider.GetRequiredService<ICustomersEventPublisher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var template = await store.FindAsync(templateId, SessionTestData.OrganisationId, Token);
        var version = template!.PublishedVersion!;

        version.Retire(clock.UtcNow, null, "The race this reconciliation exists for.").IsSuccess
            .ShouldBeTrue();

        template.Touch(clock.UtcNow, null);

        events.Publish(new MeasurementTemplateVersionRetired(
            ids.NewId(),
            clock.UtcNow,
            template.Id,
            template.OrganisationId,
            version.Id,
            template.Code,
            version.VersionNumber,
            template.PublishedVersion is not null));

        (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Drafts, submits, approves and publishes a second version of an existing template.</summary>
    private async Task PublishAnotherVersionAsync(Guid templateId)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();

        var draft = await handler.StartDraftAsync(
            new StartTemplateDraftCommand(
                templateId, SessionTestData.OrganisationId, "Version 2", null, DisplayUnit.Inch, null, null),
            Token);

        draft.IsSuccess.ShouldBeTrue();

        var versionId = draft.Value.Version!.Id;

        (await handler.SaveFieldAsync(FieldCommand(templateId, versionId), Token)).IsSuccess.ShouldBeTrue();

        var command = new TemplateLifecycleCommand(
            templateId, versionId, SessionTestData.OrganisationId, "Fixture.", null, null);

        (await handler.SubmitAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.PublishAsync(command, Token)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Creates a measurement template with one published version, and answers its identifier.</summary>
    /// <remarks>
    /// Through the handler rather than over HTTP: this is a fixture for these tests, not the thing they are
    /// testing, and the template's own routes have their own tests. The acts are attributed to nobody, which is
    /// what lets one caller both submit and approve however many administrators the test database happens to hold.
    /// </remarks>
    private async Task<Guid> PublishedTemplateAsync(string stem)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var code = $"MT_REC_{stem}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        var template = await handler.CreateAsync(
            new CreateMeasurementTemplateCommand(SessionTestData.OrganisationId, code, code, null, null),
            Token);

        template.IsSuccess.ShouldBeTrue();

        var templateId = template.Value.Template.Id;

        var draft = await handler.StartDraftAsync(
            new StartTemplateDraftCommand(
                templateId, SessionTestData.OrganisationId, "Version 1", null, DisplayUnit.Inch, null, null),
            Token);

        draft.IsSuccess.ShouldBeTrue();

        var versionId = draft.Value.Version!.Id;

        (await handler.SaveFieldAsync(FieldCommand(templateId, versionId), Token)).IsSuccess.ShouldBeTrue();

        var command = new TemplateLifecycleCommand(
            templateId, versionId, SessionTestData.OrganisationId, "Fixture.", null, null);

        (await handler.SubmitAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.PublishAsync(command, Token)).IsSuccess.ShouldBeTrue();

        return templateId;
    }

    /// <summary>The one field every fixture template carries. Synthetic, and measured on nobody.</summary>
    private static SaveTemplateFieldCommand FieldCommand(Guid templateId, Guid versionId)
        => new(
            templateId,
            versionId,
            SessionTestData.OrganisationId,
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
            null);
}
