using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That the <c>orders</c> schema accepts a workflow definition and every version anyone has drafted, published
/// or retired of it, and hands all of it back (#232).
/// </summary>
/// <remarks>
/// Written through the real <see cref="IWorkflowDefinitionStore"/>, never by inserting rows directly — the same
/// discipline <c>OrdersHarness</c>'s own remarks give for the rest of this module: what is under test is that
/// PostgreSQL took the rows Entity Framework produced, not that a fixture believes it did.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class WorkflowPersistenceTests(WebApplicationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADraftVersionRoundTripsWithItsPhasesTransitionsAndCategoryMappingIdentical()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var organisationId = SessionTestData.OrganisationId;
        Guid definitionId;
        Guid versionId;

        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var now = clock.UtcNow;

            var definition = WorkflowDefinition.Create(
                ids.NewId(), organisationId, $"WF_{ids.NewId():N}"[..20].ToUpperInvariant(), "A round trip test",
                "Written by WorkflowPersistenceTests.", now, null).Value;
            var version = definition.AddVersion(ids, now, null);

            var phases = new[]
            {
                WorkflowPhaseContent.Create(
                    "CUTTING", "Cutting", 0, ["tailor"], false, TimeSpan.FromHours(2), null, false, false, false)
                    .Value,
                WorkflowPhaseContent.Create(
                    "STITCHING", "Stitching", 1, ["tailor", "tailor_master"], true, TimeSpan.FromDays(1),
                    TimeSpan.FromDays(2), true, true, false).Value,
                WorkflowPhaseContent.Create(
                    "READY", "Ready", 2, ["tailor_master"], false, null, null, false, false, true).Value,
            };

            definition.ReplacePhases(version.Id, ids, phases, now, null).IsSuccess.ShouldBeTrue();
            definition.ReplaceTransitions(
                    version.Id,
                    [
                        PhaseTransition.Create("CUTTING", "STITCHING").Value,
                        PhaseTransition.Create("STITCHING", "READY").Value,
                    ],
                    now,
                    null)
                .IsSuccess.ShouldBeTrue();
            definition.ReplaceCategoryMapping(version.Id, ["BLOUSE_PATTERN", "BLOUSE_AARI"], now, null)
                .IsSuccess.ShouldBeTrue();

            store.Add(definition);
            var saved = await store.SaveAsync(Token);
            saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Error.Code : null);

            definitionId = definition.Id;
            versionId = version.Id;
        }

        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

            var read = await store.FindAsync(definitionId, organisationId, Token);
            read.ShouldNotBeNull();

            var version = read.FindVersion(versionId);
            version.ShouldNotBeNull();
            version.Status.ShouldBe(WorkflowVersionStatus.Draft);

            version.Phases.Select(phase => phase.Code).ShouldBe(["CUTTING", "STITCHING", "READY"], ignoreOrder: true);
            var stitching = version.Phases.Single(phase => phase.Code == "STITCHING");
            stitching.Ordinal.ShouldBe(1);
            stitching.RequiredRoleKeys.ShouldBe(["tailor", "tailor_master"], ignoreOrder: true);
            stitching.RequiresEvidence.ShouldBeTrue();
            stitching.ExpectedDuration.ShouldBe(TimeSpan.FromDays(1));
            stitching.Sla.ShouldBe(TimeSpan.FromDays(2));
            stitching.IsOptional.ShouldBeTrue();
            stitching.IsSkippable.ShouldBeTrue();
            stitching.IsTerminal.ShouldBeFalse();

            version.Transitions.ShouldBe(
                [
                    new PhaseTransition("CUTTING", "STITCHING"),
                    new PhaseTransition("STITCHING", "READY"),
                ],
                ignoreOrder: true);
            version.CategoryKeys.ShouldBe(["BLOUSE_PATTERN", "BLOUSE_AARI"], ignoreOrder: true);

            // xmin serves as the If-Match token: a version freshly read carries one, even with no route yet
            // to send it back over.
            store.EntityTagOf(version).Value.ShouldNotBeNullOrEmpty();
            store.EntityTagOf(read).Value.ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task TheHarnessesFixedWorkflowIdentifiersNameARealPublishedSixPhaseVersion()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Idempotent: whichever test in the collection runs first does the seeding, and this proves both that
        // a first call produces the row set OrdersHarness.WorkflowDefinition/WorkflowVersion name, and that
        // calling it again — as every other test's fixture setup does — does not fail or duplicate anything.
        await OrdersHarness.EnsureWorkflowSeededAsync(fixture);
        await OrdersHarness.EnsureWorkflowSeededAsync(fixture);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        var definition = await store.FindAsync(OrdersHarness.WorkflowDefinition, SessionTestData.OrganisationId, Token);
        definition.ShouldNotBeNull();
        definition.Versions.Count.ShouldBe(1, "seeding twice must not draft a second version");

        var version = definition.FindVersion(OrdersHarness.WorkflowVersion);
        version.ShouldNotBeNull();
        version.Status.ShouldBe(WorkflowVersionStatus.Published);
        version.Phases.Select(phase => phase.Code).ShouldBe(
            ["CUTTING", "SPECIALIST_WORK", "STITCHING", "FINISHING", "QC", "READY"], ignoreOrder: true);
        version.ValidateForPublication().ShouldBeEmpty();

        var pinned = await store.FindPublishedVersionAsync(
            OrdersHarness.WorkflowDefinition, SessionTestData.OrganisationId, DateTimeOffset.UtcNow, Token);
        pinned.ShouldNotBeNull();
        pinned.Id.ShouldBe(OrdersHarness.WorkflowVersion);
    }

    [Fact]
    public async Task TwoConcurrentAttemptsToPublishASecondVersionOfOneDefinitionDoNotBothSucceed()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var organisationId = SessionTestData.OrganisationId;
        Guid definitionId;

        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var now = clock.UtcNow;

            var definition = WorkflowDefinition.Create(
                ids.NewId(), organisationId, $"WF_{ids.NewId():N}"[..20].ToUpperInvariant(), "A publish race test",
                null, now, null).Value;
            var first = definition.AddVersion(ids, now, null);

            definition.ReplacePhases(
                    first.Id, ids,
                    [WorkflowPhaseContent.Create("ONLY", "Only", 0, ["tailor"], false, null, null, false, false, true)
                        .Value],
                    now,
                    null)
                .IsSuccess.ShouldBeTrue();
            definition.Publish(first.Id, now, null, "First launch.").IsSuccess.ShouldBeTrue();

            store.Add(definition);
            (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

            definitionId = definition.Id;
        }

        // Two administrators, each drafting and publishing a second version of the same definition, both
        // reading before either writes — the shape a genuine race needs, driven at the store level the way
        // OrderDraftEndpointTests's own concurrency tests are.
        using var one = fixture.Services.CreateScope();
        using var other = fixture.Services.CreateScope();
        var oneStore = one.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var otherStore = other.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var oneIds = one.ServiceProvider.GetRequiredService<IIdGenerator>();
        var otherIds = other.ServiceProvider.GetRequiredService<IIdGenerator>();
        var now2 = one.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var definitionForOne = (await oneStore.FindAsync(definitionId, organisationId, Token))!;
        var definitionForOther = (await otherStore.FindAsync(definitionId, organisationId, Token))!;

        var secondByOne = definitionForOne.AddVersion(oneIds, now2, null);
        definitionForOne.ReplacePhases(
            secondByOne.Id, oneIds,
            [WorkflowPhaseContent.Create("ONE", "One", 0, ["tailor"], false, null, null, false, false, true).Value],
            now2, null);
        definitionForOne.Publish(secondByOne.Id, now2, null, "Second launch, from one.").IsSuccess.ShouldBeTrue();

        var secondByOther = definitionForOther.AddVersion(otherIds, now2, null);
        definitionForOther.ReplacePhases(
            secondByOther.Id, otherIds,
            [WorkflowPhaseContent.Create("OTHER", "Other", 0, ["tailor"], false, null, null, false, false, true)
                .Value],
            now2, null);
        definitionForOther.Publish(secondByOther.Id, now2, null, "Second launch, from other.")
            .IsSuccess.ShouldBeTrue();

        var winner = await oneStore.SaveAsync(Token);
        winner.IsSuccess.ShouldBeTrue(winner.IsFailure ? winner.Error.Code : null);

        var loser = await otherStore.SaveAsync(Token);
        loser.IsFailure.ShouldBeTrue();
        loser.Error.Code.ShouldBe("orders.concurrent-change");

        // The named result the acceptance criterion asks for, not a raw PostgresException, and the database
        // agrees exactly one second version made it to Published.
        using var confirm = fixture.Services.CreateScope();
        var confirmed = await confirm.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
            .FindAsync(definitionId, organisationId, Token);
        confirmed!.Versions.Count(version => version.Status == WorkflowVersionStatus.Published).ShouldBe(1);
    }

    [Fact]
    public async Task AMutatorOnAPublishedOrRetiredVersionIsRefusedAndTheRowIsUnchanged()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var organisationId = SessionTestData.OrganisationId;
        Guid definitionId;
        Guid versionId;

        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var now = clock.UtcNow;

            var definition = WorkflowDefinition.Create(
                ids.NewId(), organisationId, $"WF_{ids.NewId():N}"[..20].ToUpperInvariant(),
                "An immutability test", null, now, null).Value;
            var version = definition.AddVersion(ids, now, null);

            definition.ReplacePhases(
                    version.Id, ids,
                    [WorkflowPhaseContent.Create("ONLY", "Only", 0, ["tailor"], false, null, null, false, false, true)
                        .Value],
                    now,
                    null)
                .IsSuccess.ShouldBeTrue();
            definition.Publish(version.Id, now, null, "Launch.").IsSuccess.ShouldBeTrue();
            definition.Retire(version.Id, now, null, "Immediately superseded, for this test.")
                .IsSuccess.ShouldBeTrue();

            store.Add(definition);
            (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

            definitionId = definition.Id;
            versionId = version.Id;
        }

        using var editScope = fixture.Services.CreateScope();
        var editStore = editScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var editDefinition = (await editStore.FindAsync(definitionId, organisationId, Token))!;

        var refused = editDefinition.ReplaceCategoryMapping(
            versionId, ["BLOUSE_PATTERN"], DateTimeOffset.UtcNow, null);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.workflow-version-not-editable");
        refused.Error.Message.ShouldContain("retired");

        // Read back once more from a fresh scope, not trusted from the in-memory aggregate alone.
        using var reread = fixture.Services.CreateScope();
        var rereadStore = reread.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var rereadVersion = (await rereadStore.FindAsync(definitionId, organisationId, Token))!
            .FindVersion(versionId)!;
        rereadVersion.Status.ShouldBe(WorkflowVersionStatus.Retired);
        rereadVersion.CategoryKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARetiredVersionStaysReadableForever()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var organisationId = SessionTestData.OrganisationId;
        Guid definitionId;
        Guid versionId;

        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var now = clock.UtcNow;

            var definition = WorkflowDefinition.Create(
                ids.NewId(), organisationId, $"WF_{ids.NewId():N}"[..20].ToUpperInvariant(),
                "A retention test", null, now, null).Value;
            var version = definition.AddVersion(ids, now, null);

            definition.ReplacePhases(
                    version.Id, ids,
                    [WorkflowPhaseContent.Create("ONLY", "Only", 0, ["tailor"], false, null, null, false, false, true)
                        .Value],
                    now,
                    null)
                .IsSuccess.ShouldBeTrue();
            definition.Publish(version.Id, now, null, "Launch.").IsSuccess.ShouldBeTrue();
            definition.Retire(version.Id, now.AddDays(1), null, "Superseded.").IsSuccess.ShouldBeTrue();

            store.Add(definition);
            (await store.SaveAsync(Token)).IsSuccess.ShouldBeTrue();

            definitionId = definition.Id;
            versionId = version.Id;
        }

        using var readScope = fixture.Services.CreateScope();
        var read = await readScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
            .FindAsync(definitionId, organisationId, Token);

        var rereadVersion2 = read!.FindVersion(versionId);
        rereadVersion2.ShouldNotBeNull();
        rereadVersion2.Status.ShouldBe(WorkflowVersionStatus.Retired);
        rereadVersion2.Phases.ShouldNotBeEmpty();
        rereadVersion2.RetiredReason.ShouldBe("Superseded.");
    }
}
