using Shouldly;
using Tailor360.Modules.Orders.Contracts.Events;
using Tailor360.Platform.Abstractions.Events;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The eleven facts Orders publishes for another module to act on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A wire name is a published contract, and a typo in one is a silent integration failure.</strong> The
/// subscriber does not crash — it simply never matches, so the delivery queue stays empty and the reservation is
/// never made, with nothing in any log to say why. The contract tier holds each name to a schema file and each
/// schema to the record's shape, but it reads the names off the types themselves, so a name that is wrong in the
/// type is wrong consistently everywhere and passes. These assertions are the other half: the literal string,
/// written out once, against <c>docs/architecture/module-ownership.md</c> section 5.5.
/// </para>
/// <para>
/// Every event also has to carry <c>EventType</c> through from its own <c>Type</c> constant and default its
/// <c>SchemaVersion</c> to 1, because the outbox serialises both onto the envelope and a subscriber routes on
/// them (<c>docs/platform/outbox.md</c>). A record that declared <c>Type</c> and forgot to override
/// <c>EventType</c> would publish the base class's value and route nowhere.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrdersIntegrationEventTests
{
    private static readonly DateTimeOffset Now = OrdersTestData.Now;
    private static readonly DateOnly Due = OrdersTestData.DueDate;

    private static Guid Id(string name) => OrdersTestData.Id(name);

    /* The wire names, against module-ownership.md section 5.5 ------------------------------------- */

    [Fact]
    public void TheElevenWireNamesAreTheOnesTheOwnershipDocumentPublishes()
    {
        EstimateIssued.Type.ShouldBe("orders.estimate-issued.v1");
        OrderConfirmed.Type.ShouldBe("orders.order-confirmed.v1");
        GarmentJobCreated.Type.ShouldBe("orders.garment-job-created.v1");
        OrderRevised.Type.ShouldBe("orders.order-revised.v1");
        OrderCancelled.Type.ShouldBe("orders.order-cancelled.v1");
        GarmentJobCancelled.Type.ShouldBe("orders.job-cancelled.v1");
        GarmentJobEnteredProduction.Type.ShouldBe("orders.job-entered-production.v1");
        GarmentJobHeld.Type.ShouldBe("orders.job-held.v1");
        GarmentJobResumed.Type.ShouldBe("orders.job-resumed.v1");
        GarmentJobRescheduled.Type.ShouldBe("orders.job-rescheduled.v1");
        GarmentJobReadyForDelivery.Type.ShouldBe("orders.job-ready-for-delivery.v1");
    }

    [Fact]
    public void EveryEventCarriesItsOwnNameOnTheEnvelopeAtSchemaVersionOne()
    {
        foreach (var published in All())
        {
            var name = published.GetType().Name;

            published.EventType.ShouldNotBeNullOrWhiteSpace();

            // A name under another module's prefix would be routed to that module's subscribers, and one
            // without its major version could not be superseded by a v2 without breaking every reader.
            published.EventType.StartsWith("orders.", StringComparison.Ordinal)
                .ShouldBeTrue($"'{name}' publishes under another module's prefix.");
            published.EventType.EndsWith(".v1", StringComparison.Ordinal)
                .ShouldBeTrue($"'{name}' does not carry its major version on the wire.");
            published.SchemaVersion.ShouldBe(1, name);
        }
    }

    [Fact]
    public void NoTwoEventsPublishUnderOneName()
        => All().Select(published => published.EventType).ShouldBeUnique();

    [Fact]
    public void EveryEventCarriesTheIdentityTheOutboxWritesTheRowAgainst()
    {
        // The outbox claims the oldest undelivered message per aggregate, so an event with no aggregate
        // identity would be ordered against nothing and could overtake the change it describes.
        foreach (var published in All())
        {
            published.EventId.ShouldNotBe(Guid.Empty, published.EventType);
            published.AggregateId.ShouldNotBe(Guid.Empty, published.EventType);
            published.OccurredAt.ShouldBe(Now, published.EventType);
        }
    }

    /* What each one says --------------------------------------------------------------------------- */

    [Fact]
    public void AnEstimateAnnouncesTheDraftItPricedAndTheDayItStopsStanding()
    {
        var issued = Estimate();

        issued.OrderDraftId.ShouldBe(Id("draft"));
        issued.EstimateNumber.ShouldBe("E-CBE01-2627-000001");
        issued.ValidUntil.ShouldBeGreaterThan(issued.IssuedOn);
    }

    [Fact]
    public void AConfirmationAnnouncesHowManyGarmentsItCommittedTo()
    {
        var confirmed = Confirmed();

        confirmed.OrderNumber.ShouldBe("O-CBE01-2627-000001");
        confirmed.GarmentJobCount.ShouldBe(2);
        confirmed.RevisionNumber.ShouldBe(1);
    }

    [Fact]
    public void AConfirmationFromADraftThatWasNeverEstimatedCarriesNoEstimate()
        => (Confirmed() with { EstimateId = null }).EstimateId.ShouldBeNull();

    [Fact]
    public void AGarmentJobAnnouncesWhatIsBeingMadeAndWhen()
    {
        var created = JobCreated();

        created.GarmentJobNumber.ShouldBe("J-CBE01-2627-000001-01");
        created.JobIndex.ShouldBe(1);
        created.CategoryKey.ShouldBe("blouse");
        created.DueDate.ShouldBe(Due);
    }

    [Fact]
    public void ARevisionAnnouncesWhichGarmentsMovedAndWhatItSuperseded()
    {
        var revised = Revised();

        revised.RevisionNumber.ShouldBe(2);
        revised.RevisedGarmentJobIds.ShouldBe([Id("job-1")]);
        revised.SupersededEstimateId.ShouldBe(Id("estimate"));
    }

    [Theory]
    [InlineData("EXAMPLE-CANCELLATION-REASON-CODE")]
    [InlineData("a-configured-code")]
    public void ACancellationCarriesItsConfiguredCodeAndNotTheReasonSomebodyTyped(string reasonCode)
    {
        // OD-10 owns the vocabulary; what matters here is that the free-text reason the domain also takes
        // has no way onto the wire (docs/nfr/data-classification.md section 5).
        var cancelled = OrderCancellation() with { ReasonCode = reasonCode };

        cancelled.ReasonCode.ShouldBe(reasonCode);
        cancelled.GetType().GetProperty("Reason").ShouldBeNull();
    }

    [Fact]
    public void AHeldGarmentCarriesItsReasonCodeAndNoReasonText()
    {
        var held = Held();

        held.ReasonCode.ShouldBe("EXAMPLE-HOLD-REASON-CODE");
        held.GetType().GetProperty("Reason").ShouldBeNull();
    }

    [Fact]
    public void AResumedGarmentSaysOnlyThatItIsBackInProduction()
    {
        // Resume takes a mandatory reason in the domain and none of it travels: a resumption is not a fact
        // another module acts on the wording of.
        var resumed = Resumed();

        resumed.GarmentJobNumber.ShouldBe("J-CBE01-2627-000001-01");
        resumed.GetType().GetProperty("Reason").ShouldBeNull();
    }

    [Fact]
    public void ARescheduledGarmentCarriesBothDatesSoASubscriberCanSeeTheMove()
    {
        var rescheduled = Rescheduled();

        rescheduled.PreviousDueDate.ShouldBe(Due);
        rescheduled.DueDate.ShouldBeGreaterThan(rescheduled.PreviousDueDate);
    }

    [Fact]
    public void AGarmentEnteringProductionNamesThePinnedWorkflowVersion()
    {
        // INV-JOB-02: the version is pinned at start of production and never migrates, so a subscriber that
        // renders a workboard has to be told which one this garment is running.
        var started = EnteredProduction();

        started.WorkflowVersionId.ShouldBe(Id("workflow-version"));
        started.WorkflowDefinitionId.ShouldBe(Id("workflow-definition"));
    }

    [Fact]
    public void AReadyGarmentNamesTheParcelItWasJudgedInsideAtThatMoment()
    {
        // SQ-07 and SQ-08 are open, so this is what the gate evaluated at EvaluatedAt and not durable
        // membership — the handover check belongs at the door.
        var ready = Ready();

        ready.BoundWithGarmentJobIds.ShouldBe([Id("job-2")]);
        ready.EvaluatedAt.ShouldBe(Now);
    }

    [Fact]
    public void AGarmentBoundToNothingAnnouncesAnEmptyParcelRatherThanNone()
        => (Ready() with { BoundWithGarmentJobIds = [] }).BoundWithGarmentJobIds.ShouldBeEmpty();

    /* Fixtures ------------------------------------------------------------------------------------- */

    private static IEnumerable<IntegrationEvent> All() =>
    [
        Estimate(),
        Confirmed(),
        JobCreated(),
        Revised(),
        OrderCancellation(),
        JobCancellation(),
        EnteredProduction(),
        Held(),
        Resumed(),
        Rescheduled(),
        Ready(),
    ];

    private static EstimateIssued Estimate() => new(
        Id("event-estimate"), Now, Id("draft"), Id("organisation"), Id("branch"), Id("customer"),
        Id("draft"), "E-CBE01-2627-000001", Due.AddDays(-14), Due.AddDays(-1));

    private static OrderConfirmed Confirmed() => new(
        Id("event-confirmed"), Now, Id("order"), Id("organisation"), Id("branch"), Id("customer"),
        "O-CBE01-2627-000001", Id("draft"), Id("estimate"), Due, 2, 1);

    private static GarmentJobCreated JobCreated() => new(
        Id("event-job-created"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", 1, "blouse", "stitching", Id("workflow-definition"), Due);

    private static OrderRevised Revised() => new(
        Id("event-revised"), Now, Id("order"), Id("organisation"), Id("branch"), Id("customer"),
        "O-CBE01-2627-000001", Id("revision"), 2, Due, Id("estimate"), [Id("job-1")]);

    private static OrderCancelled OrderCancellation() => new(
        Id("event-order-cancelled"), Now, Id("order"), Id("organisation"), Id("branch"), Id("customer"),
        "O-CBE01-2627-000001", "EXAMPLE-CANCELLATION-REASON-CODE");

    private static GarmentJobCancelled JobCancellation() => new(
        Id("event-job-cancelled"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", "EXAMPLE-CANCELLATION-REASON-CODE");

    private static GarmentJobEnteredProduction EnteredProduction() => new(
        Id("event-started"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", Id("workflow-definition"), Id("workflow-version"));

    private static GarmentJobHeld Held() => new(
        Id("event-held"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", "EXAMPLE-HOLD-REASON-CODE");

    private static GarmentJobResumed Resumed() => new(
        Id("event-resumed"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01");

    private static GarmentJobRescheduled Rescheduled() => new(
        Id("event-rescheduled"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", Due.AddDays(7), Due);

    private static GarmentJobReadyForDelivery Ready() => new(
        Id("event-ready"), Now, Id("order"), Id("organisation"), Id("branch"), Id("order"),
        "J-CBE01-2627-000001-01", Due, Now, [Id("job-2")]);
}
