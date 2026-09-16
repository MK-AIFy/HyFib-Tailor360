using Shouldly;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// A workflow definition's own versions: numbering, the draft → published → retired lifecycle, and what a
/// mutator does once a version has left <see cref="WorkflowVersionStatus.Draft"/>.
/// </summary>
/// <remarks>
/// Driven through <see cref="WorkflowDefinition"/>'s public surface throughout, never by constructing a
/// <see cref="WorkflowVersion"/> directly — its own mutators are <see langword="internal"/>, reachable only
/// through the aggregate root, exactly as <c>OrderDraftTests</c> drives <c>OrderDraftGarment</c> through
/// <c>OrderDraft</c>. The phase graph's own six checks are <c>WorkflowGraphTests</c>'s, not this file's.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class WorkflowVersionTests
{
    private static readonly DateTimeOffset Now = OrdersTestData.Now;
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    [Theory]
    [InlineData("")]
    [InlineData("stitch_standard")]
    [InlineData("STITCH STANDARD")]
    [InlineData("NONE")]
    public void RefusesADefinitionWithNoWellFormedCode(string code)
    {
        var created = WorkflowDefinition.Create(
            OrdersTestData.Id("def"), OrdersTestData.Organisation, code, "Stitching, standard", null, Now, null);

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe("orders.workflow-code-not-well-formed");
    }

    [Fact]
    public void RefusesADefinitionWithNoName()
    {
        var created = WorkflowDefinition.Create(
            OrdersTestData.Id("def"), OrdersTestData.Organisation, "STITCH_STANDARD", "  ", null, Now, null);

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe("orders.value-required");
    }

    [Fact]
    public void ANewDefinitionIsActiveWithNoVersions()
    {
        var definition = Definition();

        definition.IsActive.ShouldBeTrue();
        definition.Versions.ShouldBeEmpty();
    }

    [Fact]
    public void CanBeWithdrawnFromANewCatalogueLinkAndReinstated()
    {
        var definition = Definition();

        definition.SetActive(false, Now, OrdersTestData.Actor);
        definition.IsActive.ShouldBeFalse();

        definition.SetActive(true, Later, OrdersTestData.Actor);
        definition.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void VersionsAreNumberedFromOneInTheOrderTheyWereStarted()
    {
        var definition = Definition();

        var first = definition.AddVersion(Ids(), Now, null);
        var second = definition.AddVersion(Ids(), Now, null);
        var third = definition.AddVersion(Ids(), Now, null);

        first.VersionNumber.ShouldBe(1);
        second.VersionNumber.ShouldBe(2);
        third.VersionNumber.ShouldBe(3);
    }

    [Fact]
    public void TwoDifferentDefinitionsNumberTheirOwnVersionsIndependently()
    {
        var one = Definition("one");
        var other = Definition("other");

        var oneVersion = one.AddVersion(Ids(), Now, null);
        var otherFirst = other.AddVersion(Ids(), Now, null);
        var otherSecond = other.AddVersion(Ids(), Now, null);

        oneVersion.VersionNumber.ShouldBe(1);
        otherFirst.VersionNumber.ShouldBe(1);
        otherSecond.VersionNumber.ShouldBe(2);
    }

    [Fact]
    public void ANewVersionStartsAsAnEmptyEditableDraft()
    {
        var version = Definition().AddVersion(Ids(), Now, null);

        version.Status.ShouldBe(WorkflowVersionStatus.Draft);
        version.IsEditable.ShouldBeTrue();
        version.Phases.ShouldBeEmpty();
        version.Transitions.ShouldBeEmpty();
        version.CategoryKeys.ShouldBeEmpty();
        version.PublishedAt.ShouldBeNull();
        version.RetiredAt.ShouldBeNull();
    }

    [Fact]
    public void ReplacingPhasesAndTransitionsAndCategoryMappingRoundTrips()
    {
        var (definition, version) = DefinitionWithDraft();
        var ids = Ids();

        definition.ReplacePhases(version.Id, ids, SixPhases(), Now, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();
        definition.ReplaceTransitions(version.Id, SixPhaseTransitions(), Now, OrdersTestData.Actor)
            .IsSuccess.ShouldBeTrue();
        definition.ReplaceCategoryMapping(version.Id, ["BLOUSE_PATTERN", "BLOUSE_AARI"], Now, OrdersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        version.Phases.Select(phase => phase.Code).ShouldBe(
            ["CUTTING", "SPECIALIST_WORK", "STITCHING", "FINISHING", "QC", "READY"], ignoreOrder: true);
        version.Phases.Single(phase => phase.Code == "CUTTING").DisplayName.ShouldBe("Cutting");
        version.Transitions.Count.ShouldBe(6);
        version.CategoryKeys.ShouldBe(["BLOUSE_PATTERN", "BLOUSE_AARI"], ignoreOrder: true);
    }

    [Fact]
    public void ReplacingPhasesTwiceReplacesTheWholeSetRatherThanAppending()
    {
        var (definition, version) = DefinitionWithDraft();
        var ids = Ids();

        definition.ReplacePhases(version.Id, ids, SixPhases(), Now, null);
        definition.ReplacePhases(
            version.Id, ids, [OneStartTerminalPhase("SOLO")], Later, OrdersTestData.Actor);

        version.Phases.Select(phase => phase.Code).ShouldBe(["SOLO"]);
        version.UpdatedAt.ShouldBe(Later);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesACategoryKeyThatIsBlank(string key)
    {
        var (definition, version) = DefinitionWithDraft();

        var replaced = definition.ReplaceCategoryMapping(version.Id, [key], Now, null);

        replaced.IsFailure.ShouldBeTrue();
        replaced.Error.Code.ShouldBe("orders.value-required");
    }

    [Fact]
    public void RefusesACategoryKeyNamedTwice()
    {
        var (definition, version) = DefinitionWithDraft();

        var replaced = definition.ReplaceCategoryMapping(version.Id, ["BLOUSE_PATTERN", "BLOUSE_PATTERN"], Now, null);

        replaced.IsFailure.ShouldBeTrue();
        replaced.Error.Code.ShouldBe("orders.duplicate-workflow-category-mapping");
    }

    [Fact]
    public void EveryMutatorOnAPublishedVersionIsRefusedNamingTheStatusAndTheRowIsUnchanged()
    {
        var (definition, version) = PublishedDefinitionAndVersion();
        var phasesBefore = version.Phases.Count;
        var transitionsBefore = version.Transitions.Count;
        var categoriesBefore = version.CategoryKeys.Count;

        var phases = definition.ReplacePhases(version.Id, Ids(), SixPhases(), Later, null);
        var transitions = definition.ReplaceTransitions(version.Id, SixPhaseTransitions(), Later, null);
        var categories = definition.ReplaceCategoryMapping(version.Id, ["BLOUSE_PATTERN"], Later, null);

        foreach (var refused in new[] { phases, transitions, categories })
        {
            refused.IsFailure.ShouldBeTrue();
            refused.Error.Code.ShouldBe("orders.workflow-version-not-editable");
            refused.Error.Message.ShouldContain("published");
        }

        // Read back, not trusted from the return value alone.
        version.Phases.Count.ShouldBe(phasesBefore);
        version.Transitions.Count.ShouldBe(transitionsBefore);
        version.CategoryKeys.Count.ShouldBe(categoriesBefore);
    }

    [Fact]
    public void EveryMutatorOnARetiredVersionIsRefusedNamingTheStatus()
    {
        var (definition, version) = PublishedDefinitionAndVersion();
        definition.Retire(version.Id, Later, null, "Superseded by a newer version.").IsSuccess.ShouldBeTrue();

        var replaced = definition.ReplaceCategoryMapping(version.Id, ["BLOUSE_PATTERN"], Later, null);

        replaced.IsFailure.ShouldBeTrue();
        replaced.Error.Code.ShouldBe("orders.workflow-version-not-editable");
        replaced.Error.Message.ShouldContain("retired");
    }

    [Fact]
    public void PublishingRefusesWithNoReason()
    {
        var (definition, version) = DefinitionWithSoundDraft();

        var published = definition.Publish(version.Id, Now, null, null);

        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("orders.reason-required");
        version.Status.ShouldBe(WorkflowVersionStatus.Draft);
    }

    [Fact]
    public void PublishingASoundGraphSucceedsAndStampsTheRecord()
    {
        var (definition, version) = DefinitionWithSoundDraft();

        var published = definition.Publish(version.Id, Now, OrdersTestData.Actor, "Launch.");

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(WorkflowVersionStatus.Published);
        version.PublishedAt.ShouldBe(Now);
        version.PublishedBy.ShouldBe(OrdersTestData.Actor);
        version.PublishReason.ShouldBe("Launch.");
    }

    [Fact]
    public void PublishingRefusesAVersionThatIsAlreadyPublishedNamingTheStatus()
    {
        var (definition, version) = PublishedDefinitionAndVersion();

        var published = definition.Publish(version.Id, Later, null, "Again.");

        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("orders.workflow-version-not-publishable");
        published.Error.Message.ShouldContain("published");
    }

    [Fact]
    public void PublishingRefusesAGraphThatStillHasFindings()
    {
        var (definition, version) = DefinitionWithDraft();
        definition.ReplacePhases(version.Id, Ids(), [OneStartTerminalPhase("SOLO")], Now, null);
        // No transitions declared at all: SOLO has no outbound edge and is terminal, so this graph is sound
        // by itself — replace it with one genuine defect instead: a phase with no role.
        var lonely = WorkflowPhaseContent.Create(
            "LONELY", "Lonely", 0, [], false, null, null, false, false, true).Value;
        definition.ReplacePhases(version.Id, Ids(), [lonely], Now, null);

        var published = definition.Publish(version.Id, Now, null, "Launch.");

        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("orders.workflow-graph-invalid");
        version.Status.ShouldBe(WorkflowVersionStatus.Draft);
        version.ValidateForPublication().ShouldContain(
            finding => finding.Code == WorkflowGraph.PhaseWithoutRequiredRole);
    }

    [Fact]
    public void ValidateForPublicationAnswersWithoutPublishing()
    {
        var (definition, version) = DefinitionWithSoundDraft();

        var findings = version.ValidateForPublication();

        findings.ShouldBeEmpty();
        version.Status.ShouldBe(WorkflowVersionStatus.Draft);
        _ = definition;
    }

    [Fact]
    public void RetiringRefusesADraftVersion()
    {
        var (definition, version) = DefinitionWithSoundDraft();

        var retired = definition.Retire(version.Id, Now, null, "Withdrawn.");

        retired.IsFailure.ShouldBeTrue();
        retired.Error.Code.ShouldBe("orders.workflow-version-not-retirable");
        retired.Error.Message.ShouldContain("draft");
    }

    [Fact]
    public void RetiringAPublishedVersionSucceedsAndTheVersionStaysReadable()
    {
        var (definition, version) = PublishedDefinitionAndVersion();

        var retired = definition.Retire(version.Id, Later, OrdersTestData.Actor, "Superseded.");

        retired.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(WorkflowVersionStatus.Retired);
        version.RetiredAt.ShouldBe(Later);
        version.RetiredReason.ShouldBe("Superseded.");
        // Never removed — INV-JOB-02's own reason: a job pinned to it must still render it.
        definition.FindVersion(version.Id).ShouldBeSameAs(version);
    }

    [Fact]
    public void ANameForAnUnknownVersionAnswersNotFound()
    {
        var definition = Definition();

        var result = definition.ReplaceCategoryMapping(OrdersTestData.Id("no-such-version"), [], Now, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("orders.workflow-version-not-found");
    }

    [Fact]
    public void FindPublishedVersionAnswersOnlyWithinItsPublishedWindow()
    {
        var (definition, version) = DefinitionWithSoundDraft();
        definition.FindPublishedVersion(Now).ShouldBeNull();

        definition.Publish(version.Id, Now, null, "Launch.");
        definition.FindPublishedVersion(Now).ShouldBeSameAs(version);
        definition.FindPublishedVersion(Now.AddDays(30)).ShouldBeSameAs(version);

        var retiredAt = Now.AddDays(60);
        definition.Retire(version.Id, retiredAt, null, "Superseded.");

        definition.FindPublishedVersion(Now.AddDays(30)).ShouldBeSameAs(version);
        definition.FindPublishedVersion(retiredAt).ShouldBeNull();
        definition.FindPublishedVersion(retiredAt.AddDays(1)).ShouldBeNull();
    }

    private static WorkflowDefinition Definition(string code = "def")
        => WorkflowDefinition.Create(
            OrdersTestData.Id(code), OrdersTestData.Organisation, "STITCH_STANDARD", "Stitching, standard",
            "The everyday stitching process.", Now, OrdersTestData.Actor).Value;

    private static (WorkflowDefinition Definition, WorkflowVersion Version) DefinitionWithDraft()
    {
        var definition = Definition();
        var version = definition.AddVersion(Ids(), Now, null);

        return (definition, version);
    }

    private static (WorkflowDefinition Definition, WorkflowVersion Version) DefinitionWithSoundDraft()
    {
        var (definition, version) = DefinitionWithDraft();
        var ids = Ids();

        definition.ReplacePhases(version.Id, ids, SixPhases(), Now, null);
        definition.ReplaceTransitions(version.Id, SixPhaseTransitions(), Now, null);

        return (definition, version);
    }

    private static (WorkflowDefinition Definition, WorkflowVersion Version) PublishedDefinitionAndVersion()
    {
        var (definition, version) = DefinitionWithSoundDraft();
        definition.Publish(version.Id, Now, OrdersTestData.Actor, "Launch.").IsSuccess.ShouldBeTrue();

        return (definition, version);
    }

    private static CountingWorkflowIds Ids() => new();

    /// <summary>The six phases <c>docs/prd/workflows/blouse.md</c> section 2.1 names for production.</summary>
    private static IReadOnlyList<WorkflowPhaseContent> SixPhases() =>
    [
        WorkflowPhaseContent.Create("CUTTING", "Cutting", 0, ["tailor"], false, null, null, false, false, false)
            .Value,
        WorkflowPhaseContent.Create(
            "SPECIALIST_WORK", "Specialist work", 1, ["aari_specialist"], true, TimeSpan.FromDays(10), null,
            true, true, false).Value,
        WorkflowPhaseContent.Create("STITCHING", "Stitching", 2, ["tailor"], false, null, null, false, false, false)
            .Value,
        WorkflowPhaseContent.Create("FINISHING", "Finishing", 3, ["tailor"], false, null, null, false, false, false)
            .Value,
        WorkflowPhaseContent.Create(
            "QC", "QC", 4, ["tailor_master"], true, null, TimeSpan.FromHours(4), false, false, false).Value,
        WorkflowPhaseContent.Create("READY", "Ready", 5, ["tailor_master"], false, null, null, false, false, true)
            .Value,
    ];

    /// <summary>The straight-line path through the six phases, with Specialist work optional.</summary>
    private static IReadOnlyList<PhaseTransition> SixPhaseTransitions() =>
    [
        PhaseTransition.Create("CUTTING", "SPECIALIST_WORK").Value,
        PhaseTransition.Create("CUTTING", "STITCHING").Value,
        PhaseTransition.Create("SPECIALIST_WORK", "STITCHING").Value,
        PhaseTransition.Create("STITCHING", "FINISHING").Value,
        PhaseTransition.Create("FINISHING", "QC").Value,
        PhaseTransition.Create("QC", "READY").Value,
    ];

    private static WorkflowPhaseContent OneStartTerminalPhase(string code)
        => WorkflowPhaseContent.Create(code, code, 0, ["tailor"], false, null, null, false, false, true).Value;
}

/// <summary>
/// A sequential <see cref="IIdGenerator"/> for these tests, mirroring <c>CatalogTestData.CountingCatalogIds</c>:
/// deterministic without depending on <c>Guid.NewGuid</c> (ARCH-015).
/// </summary>
internal sealed class CountingWorkflowIds : IIdGenerator
{
    private int _issued;

    /// <inheritdoc />
    public Guid NewId()
    {
        _issued++;
        return OrdersTestData.Id($"workflow-generated-{_issued}");
    }
}
