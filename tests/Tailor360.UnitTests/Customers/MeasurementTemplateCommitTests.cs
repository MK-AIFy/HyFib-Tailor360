using Shouldly;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.UnitTests.Identity;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// What the handler does when the commit itself is refused.
/// </summary>
/// <remarks>
/// <para>
/// Every template mutation now demands <c>If-Match</c>, and the endpoint contract for a precondition that no
/// longer holds is a <c>409</c>. The precondition is checked when the version is loaded — but the row can still
/// move between that check and the commit, and PostgreSQL answers that with a concurrency exception rather than a
/// polite result. If the handler let it through, the one case the precondition exists to describe would be the one
/// case that reached the client as a five hundred.
/// </para>
/// <para>
/// The window is a few milliseconds wide inside a single request, so no integration test can stage it reliably.
/// These tests close it deliberately with a store that refuses to commit.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasurementTemplateCommitTests
{
    [Fact]
    public async Task AFieldChangeThatLosesTheRowAtTheCommitIsAConflictRatherThanAFault()
    {
        var (template, draft) = MeasurementTestData.WithDraft();
        var store = Store(template);
        var handler = Handler(store);

        store.NextSave = Result.Failure(MeasurementErrors.VersionChanged);

        var result = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                template.Id,
                draft.Id,
                MeasurementTestData.OrganisationId,
                null,
                MeasurementTestData.Field("chest_bust"),
                null,
                null),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("measurements.version-changed");
        store.Saves.ShouldBe(1, "the commit was attempted, and its answer was carried rather than discarded");
    }

    [Fact]
    public async Task AFieldRemovalThatLosesTheRowAtTheCommitIsAConflictRatherThanAFault()
    {
        var (template, draft) = MeasurementTestData.WithDraft();
        var added = draft.AddField(
            MeasurementTestData.Id("field"),
            MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now,
            null);

        added.IsSuccess.ShouldBeTrue();

        var store = Store(template);
        var handler = Handler(store);

        store.NextSave = Result.Failure(MeasurementErrors.VersionChanged);

        var result = await handler.RemoveFieldAsync(
            new RemoveTemplateFieldCommand(
                template.Id,
                draft.Id,
                MeasurementTestData.OrganisationId,
                added.Value.Id,
                "Measured somewhere else now.",
                null,
                null),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("measurements.version-changed");
    }

    [Fact]
    public async Task ALifecycleTransitionThatLosesTheRowAtTheCommitIsAConflictRatherThanAFault()
    {
        // Submit is the cheapest transition to reach, and it commits through the same path as return, approve
        // and retire — TransitionAsync is one method serving all four.
        var (template, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
                MeasurementTestData.Id("field"),
                MeasurementTestData.Field("chest_bust"),
                MeasurementTestData.Now,
                null)
            .IsSuccess.ShouldBeTrue();

        var store = Store(template);
        var handler = Handler(store);

        store.NextSave = Result.Failure(MeasurementErrors.VersionChanged);

        var result = await handler.SubmitAsync(
            new TemplateLifecycleCommand(
                template.Id,
                draft.Id,
                MeasurementTestData.OrganisationId,
                string.Empty,
                null,
                null),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("measurements.version-changed");
    }

    [Fact]
    public async Task ACommitThatSucceedsStillAnswersWithTheTemplate()
    {
        // The guard on the three above: they must be failing because the commit was refused, not because the
        // handler refuses everything this test harness hands it.
        var (template, draft) = MeasurementTestData.WithDraft();
        var store = Store(template);
        var handler = Handler(store);

        var result = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                template.Id,
                draft.Id,
                MeasurementTestData.OrganisationId,
                null,
                MeasurementTestData.Field("chest_bust"),
                null,
                null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldNotBeNull();
        result.Value.Version.Fields.ShouldHaveSingleItem().Key.Value.ShouldBe("chest_bust");
    }

    private static StubTemplateStore Store(MeasurementTemplate template) => new(template);

    private static MeasurementTemplateHandler Handler(StubTemplateStore store)
        => new(
            store,
            new MovableClock(MeasurementTestData.Now),
            new MeasurementTestData.CountingIds("commit"),
            new RecordingAuditWriter(),
            new StubUserDirectory(activeWithPermission: 2),
            new StubCatalogAvailability(referencesTemplate: false));
}
