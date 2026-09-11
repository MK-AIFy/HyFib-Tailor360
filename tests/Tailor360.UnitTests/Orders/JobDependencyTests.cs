using System.Reflection;
using Shouldly;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// What one garment of an order may declare about another (INV-JOB-09), and where each of the two kinds
/// is actually enforced.
/// </summary>
/// <remarks>
/// <para>
/// The two kinds are deliberately enforced in different places, and a reader who sees a dependency row
/// needs to know which gate will act on it: <c>finish_before</c> is checked at start of production, and
/// <c>deliver_together</c> by the ready gate and the delivery queue. Neither is a scheduling instruction
/// — a dependency never moves a promised date.
/// </para>
/// <para>
/// The validation is split for the same reason: a single row cannot see the order it belongs to, so a
/// dependency on the garment itself is caught by <see cref="GarmentJobSpecification.Create"/> and one
/// naming a garment outside the confirmation by <see cref="Order.Confirm"/>, which is the only place the
/// whole set is visible.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class JobDependencyTests
{
    private static readonly Guid Blouse = OrdersTestData.GarmentId(1);

    private static readonly Guid Skirt = OrdersTestData.GarmentId(2);

    /* Declaring --------------------------------------------------------------------------------- */

    [Fact]
    public void ADeclaredDependencyRecordsWhatItIsWhoDeclaredItAndWhen()
    {
        var order = Pair(JobDependencyKind.FinishBefore, "The blouse is cut from the same length.");

        var dependency = order.FindJob(Skirt)!.Dependencies.ShouldHaveSingleItem();
        dependency.Id.ShouldBe(OrdersTestData.Id("dependency-1"));
        dependency.GarmentJobId.ShouldBe(Skirt);
        dependency.PrerequisiteGarmentJobId.ShouldBe(Blouse);
        dependency.Kind.ShouldBe(JobDependencyKind.FinishBefore);
        dependency.Reason.ShouldBe("The blouse is cut from the same length.");
        dependency.DeclaredAt.ShouldBe(OrdersTestData.Now);
        dependency.DeclaredBy.ShouldBe(OrdersTestData.Actor);
    }

    [Fact]
    public void AGarmentWithNoDependenciesDeclaresNone()
    {
        OrdersTestData.ConfirmedOrder().Jobs.Single().Dependencies.ShouldBeEmpty();
    }

    [Fact]
    public void APrerequisiteIsReadBackOnlyUnderTheKindItWasDeclaredAs()
    {
        var order = Pair(JobDependencyKind.FinishBefore);
        var job = order.FindJob(Skirt)!;

        job.PrerequisiteJobIds(JobDependencyKind.FinishBefore).ShouldBe([Blouse]);
        job.PrerequisiteJobIds(JobDependencyKind.DeliverTogether).ShouldBeEmpty();
    }

    [Fact]
    public void AReasonIsOptionalOnADependencyAndABlankOneIsNoReasonAtAll()
    {
        var order = Pair(JobDependencyKind.DeliverTogether, reason: "   ");

        order.FindJob(Skirt)!.Dependencies.Single().Reason.ShouldBeNull();
    }

    [Fact]
    public void OneGarmentMayDeclareBothKindsAgainstTheSamePrerequisite()
    {
        // The two are different promises: one is about when the work may start, the other about when the
        // garments reach the customer, and declaring both says both.
        var order = PairWith(
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-1"), Blouse, JobDependencyKind.FinishBefore, null),
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-2"), Blouse, JobDependencyKind.DeliverTogether, null),
            ]);

        order.FindJob(Skirt)!.Dependencies.Count.ShouldBe(2);
    }

    /* Refusals ---------------------------------------------------------------------------------- */

    /// <summary>
    /// A garment that must finish before itself never starts, and one delivered with itself binds the
    /// queue to a job that can never become ready. Both are screen defects, and neither is recoverable at
    /// the gate — by then the order is confirmed and the only remedy is a new one.
    /// </summary>
    [Theory]
    [InlineData(JobDependencyKind.FinishBefore)]
    [InlineData(JobDependencyKind.DeliverTogether)]
    public void AGarmentCannotWaitForItself(JobDependencyKind kind)
    {
        var declared = Specification(
            [new GarmentJobDependencySpecification(OrdersTestData.Id("dependency-1"), Skirt, kind, null)]);

        declared.IsFailure.ShouldBeTrue();
        declared.Error.Code.ShouldBe("orders.dependency-on-itself");
    }

    [Fact]
    public void TheSamePrerequisiteAndKindCannotBeDeclaredTwice()
    {
        var declared = Specification(
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-1"), Blouse, JobDependencyKind.DeliverTogether, null),
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-2"), Blouse, JobDependencyKind.DeliverTogether, null),
            ]);

        declared.IsFailure.ShouldBeTrue();
        declared.Error.Code.ShouldBe("orders.duplicate-dependency");
    }

    [Theory]
    [InlineData(true, false, "orders.value-required")]
    [InlineData(false, true, "orders.value-required")]
    public void ADependencyNeedsBothAnIdentityAndAPrerequisite(
        bool blankId,
        bool blankPrerequisite,
        string expected)
    {
        var declared = Specification(
            [
                new GarmentJobDependencySpecification(
                    blankId ? Guid.Empty : OrdersTestData.Id("dependency-1"),
                    blankPrerequisite ? Guid.Empty : Blouse,
                    JobDependencyKind.FinishBefore,
                    null),
            ]);

        declared.IsFailure.ShouldBeTrue();
        declared.Error.Code.ShouldBe(expected);
    }

    [Fact]
    public void AReasonLongerThanTheColumnHoldsIsRefusedRatherThanTruncated()
    {
        var declared = Specification(
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-1"),
                    Blouse,
                    JobDependencyKind.FinishBefore,
                    new string('x', JobDependency.MaximumReasonLength + 1)),
            ]);

        declared.IsFailure.ShouldBeTrue();
        declared.Error.Code.ShouldBe("orders.value-too-long");
    }

    /// <summary>
    /// INV-JOB-09 binds garments <em>of one order</em>. A dependency reaching across orders would be a
    /// promise the delivery queue could never resolve, because the two orders are delivered on their own
    /// dates to their own customers.
    /// </summary>
    [Fact]
    public void ADependencyOnAGarmentOutsideTheConfirmationIsRefused()
    {
        var number = OrdersTestData.Number();

        var confirmed = Order.Confirm(
            OrdersTestData.Id("order"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Customer,
            number,
            OrdersTestData.Id("draft"),
            estimateId: null,
            OrdersTestData.DueDate,
            notes: null,
            OrdersTestData.Price(),
            [
                OrdersTestData.Garment(
                    number,
                    jobIndex: 1,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"),
                            OrdersTestData.Id("a-garment-of-another-order"),
                            JobDependencyKind.FinishBefore,
                            null),
                    ]),
            ],
            OrdersTestData.Id("revision-1"),
            OrdersTestData.Now,
            OrdersTestData.Actor);

        confirmed.IsFailure.ShouldBeTrue();
        confirmed.Error.Code.ShouldBe("orders.dependency-not-in-same-order");
    }

    /* finish_before ----------------------------------------------------------------------------- */

    /// <summary>
    /// Phase-level completion is not something the Orders domain can see in this increment, so the fact
    /// arrives from the application rather than being guessed. A prerequisite absent from that set blocks
    /// the dependent garment's first phase.
    /// </summary>
    [Fact]
    public void AGarmentWaitingOnAnotherCannotStartUntilTheApplicationSaysThatOneIsFinished()
    {
        var order = Pair(JobDependencyKind.FinishBefore);

        var started = order.StartProduction(
            Skirt,
            OrdersTestData.WorkflowVersion,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe("orders.prerequisite-not-finished");
        started.Error.Message.ShouldContain(order.FindJob(Blouse)!.JobNumber.Value);
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.Confirmed);
    }

    [Fact]
    public void AGarmentWhosePrerequisiteIsFinishedStarts()
    {
        var order = Pair(JobDependencyKind.FinishBefore);

        var started = order.StartProduction(
            Skirt,
            OrdersTestData.WorkflowVersion,
            [Blouse],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        started.IsSuccess.ShouldBeTrue();
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// <c>deliver_together</c> is about when the garments reach the customer and never blocks production
    /// of either one — which is exactly why the two kinds are enforced in different places.
    /// </summary>
    [Fact]
    public void AGarmentDeliveredWithAnotherStillStartsOnItsOwn()
    {
        var order = Pair(JobDependencyKind.DeliverTogether);

        var started = order.StartProduction(
            Skirt,
            OrdersTestData.WorkflowVersion,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        started.IsSuccess.ShouldBeTrue();
        order.FindJob(Skirt)!.Status.ShouldBe(GarmentJobStatus.InProduction);
    }

    /// <summary>
    /// What counts as finished — including whether a cancelled prerequisite counts — is the application's
    /// call and is deliberately not decided here. The domain refuses to guess: a prerequisite the
    /// application has not named stays a prerequisite, whatever state it is in.
    /// </summary>
    [Fact]
    public void ACancelledPrerequisiteStillBlocksUntilTheApplicationSaysOtherwise()
    {
        var order = Pair(JobDependencyKind.FinishBefore);
        OrdersTestData.CancelledJob(order, Blouse);

        var blocked = Start(order, Skirt);
        var allowed = order.StartProduction(
            Skirt,
            OrdersTestData.WorkflowVersion,
            [Blouse],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        blocked.IsFailure.ShouldBeTrue();
        blocked.Error.Code.ShouldBe("orders.prerequisite-not-finished");
        allowed.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The refusal names the same garment on every attempt, because a refusal that moves around reads as
    /// a different problem each time.
    /// </summary>
    [Fact]
    public void AGarmentBlockedByTwoPrerequisitesNamesTheSameOneEveryTime()
    {
        var number = OrdersTestData.Number();
        var third = OrdersTestData.GarmentId(3);
        var order = OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(number, jobIndex: 2),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 3,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-2"), Skirt, JobDependencyKind.FinishBefore, null),
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"), Blouse, JobDependencyKind.FinishBefore, null),
                    ]),
            ],
            number);

        var first = Start(order, third);
        var again = Start(order, third);

        first.Error.Message.ShouldBe(again.Error.Message);
        first.Error.Message.ShouldContain(order.FindJob(Blouse)!.JobNumber.Value);
    }

    /* deliver_together -------------------------------------------------------------------------- */

    /// <summary>
    /// The relationship is symmetric even though the row is not: "this garment goes with that one" is the
    /// same promise read from either end, so both ends are returned.
    /// </summary>
    [Fact]
    public void SiblingsAreFoundFromEitherEndOfTheDeclaration()
    {
        var order = Pair(JobDependencyKind.DeliverTogether);

        order.DeliverTogetherSiblingsOf(Skirt).Select(sibling => sibling.Id).ShouldBe([Blouse]);
        order.DeliverTogetherSiblingsOf(Blouse).Select(sibling => sibling.Id).ShouldBe([Skirt]);
    }

    [Fact]
    public void AGarmentIsNeverItsOwnSibling()
    {
        var order = PairWith(
            [
                new GarmentJobDependencySpecification(
                    OrdersTestData.Id("dependency-1"), Blouse, JobDependencyKind.DeliverTogether, null),
            ]);

        order.DeliverTogetherSiblingsOf(Skirt).ShouldNotContain(sibling => sibling.Id == Skirt);
    }

    [Fact]
    public void AGarmentBoundByTheOtherKindIsNotASibling()
    {
        var order = Pair(JobDependencyKind.FinishBefore);

        order.DeliverTogetherSiblingsOf(Skirt).ShouldBeEmpty();
        order.DeliverTogetherSiblingsOf(Blouse).ShouldBeEmpty();
    }

    [Fact]
    public void AGarmentThatIsNotOnTheOrderHasNoSiblings()
    {
        var order = Pair(JobDependencyKind.DeliverTogether);

        order.DeliverTogetherSiblingsOf(OrdersTestData.Id("a-garment-of-another-order")).ShouldBeEmpty();
    }

    /* Shape ------------------------------------------------------------------------------------- */

    /// <summary>
    /// A dependency is a record of something that was declared, so it is append-only and carries no
    /// concurrency token: only the aggregate in its own assembly can create one, and nothing edits one.
    /// </summary>
    [Fact]
    public void ADependencyCannotBeCreatedOrEditedFromOutsideItsAggregate()
    {
        typeof(JobDependency)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();

        typeof(JobDependency)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();

        typeof(JobDependency)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    /// <summary>Two garments of one order, the second declaring the dependency on the first.</summary>
    private static Order Pair(JobDependencyKind kind, string? reason = null)
        => PairWith(
            [new GarmentJobDependencySpecification(OrdersTestData.Id("dependency-1"), Blouse, kind, reason)]);

    private static Order PairWith(IReadOnlyCollection<GarmentJobDependencySpecification> dependencies)
    {
        var number = OrdersTestData.Number();

        return OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(number, jobIndex: 2, dependencies: dependencies),
            ],
            number);
    }

    /// <summary>One garment's specification, so a refusal can be read without confirming an order.</summary>
    private static Result<GarmentJobSpecification> Specification(
        IReadOnlyCollection<GarmentJobDependencySpecification> dependencies)
        => GarmentJobSpecification.Create(
            Skirt,
            OrdersTestData.JobNumber(OrdersTestData.Number(), 2),
            2,
            "blouse",
            "stitch-new",
            OrdersTestData.WorkflowDefinition,
            OrdersTestData.Measurements(),
            OrdersTestData.Design(),
            OrdersTestData.Price(),
            OrdersTestData.DueDate,
            referenceMediaIds: null,
            dependencies);

    private static Result Start(Order order, Guid garmentJobId)
        => order.StartProduction(
            garmentJobId,
            OrdersTestData.WorkflowVersion,
            [],
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);
}
