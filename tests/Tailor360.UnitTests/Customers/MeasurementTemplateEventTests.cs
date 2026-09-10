using Shouldly;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.UnitTests.Identity;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The two lifecycle events the catalogue reconciles INV-MTV-06 on (issue #91).
/// </summary>
/// <remarks>
/// What matters here is not the payload's prettiness but that the events are staged <em>at all</em>, and staged on
/// the transitions that can strand a catalogue. Without them Catalog is never told, and the reconciliation G-6
/// promises for every cross-module reference has nothing to run on.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasurementTemplateEventTests
{
    [Fact]
    public async Task PublishingAVersionStagesTheEventCatalogHealsOn()
    {
        var (template, _) = MeasurementTestData.WithDraft();
        var store = new StubTemplateStore(template);
        var events = new RecordingEventPublisher();
        var handler = Handler(store, events);
        var versionId = await ReadyVersionAsync(handler, template);

        var result = await handler.PublishAsync(
            Lifecycle(template, versionId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        var published = events.Published.OfType<MeasurementTemplateVersionPublished>()
            .ShouldHaveSingleItem();

        // The template and not the version, because a catalogue service type references the template — which is
        // what makes publishing a version configuration rather than a release.
        published.AggregateId.ShouldBe(template.Id);
        published.TemplateVersionId.ShouldBe(versionId);
        published.TemplateCode.ShouldBe(template.Code);
        published.OrganisationId.ShouldBe(MeasurementTestData.OrganisationId);
        published.EventType.ShouldBe("customers.measurement-template-version-published.v1");
    }

    [Fact]
    public async Task RetiringAVersionStagesTheEventThatCanStrandACatalogue()
    {
        var (template, _) = MeasurementTestData.WithDraft();
        var store = new StubTemplateStore(template);
        var events = new RecordingEventPublisher();
        var handler = Handler(store, events);
        var versionId = await ReadyVersionAsync(handler, template);

        (await handler.PublishAsync(Lifecycle(template, versionId), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();

        var result = await handler.RetireAsync(
            Lifecycle(template, versionId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        var retired = events.Published.OfType<MeasurementTemplateVersionRetired>().ShouldHaveSingleItem();

        retired.AggregateId.ShouldBe(template.Id);
        retired.TemplateVersionId.ShouldBe(versionId);

        // The fact the event is about: the template now has nothing to measure with, which is the case
        // worth reconciling. Retiring a superseded version is routine and strands nothing.
        retired.TemplateStillPublishable.ShouldBeFalse();
        retired.EventType.ShouldBe("customers.measurement-template-version-retired.v1");
    }

    [Fact]
    public async Task StagesNothingWhenTheTransitionIsRefused()
    {
        // An event announcing a change that did not happen is worse than no event: every consumer would act on
        // it, and the reconciliation would open or close a breach on a template nobody touched.
        var (template, draft) = MeasurementTestData.WithDraft();
        var store = new StubTemplateStore(template);
        var events = new RecordingEventPublisher();
        var handler = Handler(store, events);

        var result = await handler.RetireAsync(
            Lifecycle(template, draft.Id), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        events.Published.ShouldBeEmpty();
    }

    private static TemplateLifecycleCommand Lifecycle(MeasurementTemplate template, Guid versionId)
        => new(template.Id, versionId, MeasurementTestData.OrganisationId, "A synthetic reason.", null, null);

    /// <summary>Drives a draft as far as a version that may be published, and answers its identifier.</summary>
    private static async Task<Guid> ReadyVersionAsync(
        MeasurementTemplateHandler handler,
        MeasurementTemplate template)
    {
        var token = TestContext.Current.CancellationToken;
        var draft = template.Versions.Single(version => version.Status == TemplateStatus.Draft);

        var field = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                template.Id,
                draft.Id,
                MeasurementTestData.OrganisationId,
                null,
                MeasurementTestData.Field("chest_bust"),
                null,
                null),
            token);

        field.IsSuccess.ShouldBeTrue();

        var command = Lifecycle(template, draft.Id);

        (await handler.SubmitAsync(command, token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, token)).IsSuccess.ShouldBeTrue();

        return draft.Id;
    }

    private static MeasurementTemplateHandler Handler(
        StubTemplateStore store,
        RecordingEventPublisher events)
        => new(
            store,
            new MovableClock(MeasurementTestData.Now),
            new MeasurementTestData.CountingIds("events"),
            new RecordingAuditWriter(),
            new StubUserDirectory(activeWithPermission: 2),
            new StubCatalogAvailability(referencesTemplate: false),
            events);
}
