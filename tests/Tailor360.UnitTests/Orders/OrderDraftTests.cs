using Shouldly;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The order draft — work in progress at the counter, shared within the branch, and never an obligation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrderDraftTests
{
    private static readonly DateTimeOffset Later = OrdersTestData.Now.AddHours(1);

    /* Starting one ------------------------------------------------------------------------------- */

    [Fact]
    public void AStartedDraftIsOpenEmptyAndExpiresAtTheEndOfTheWindowTheBranchConfigured()
    {
        var draft = OrdersTestData.Draft(TimeSpan.FromHours(24));

        draft.IsOpen.ShouldBeTrue();
        draft.ConsumedAt.ShouldBeNull();
        draft.Garments.ShouldBeEmpty();
        draft.BranchId.ShouldBe(OrdersTestData.Branch);
        draft.ExpiresAt.ShouldBe(OrdersTestData.Now.AddHours(24));
    }

    [Fact]
    public void TheDocumentedDefaultWindowIsSeventyTwoHours()
    {
        // glossary.md section 4 and state-transitions.md section 2.1. The window itself is branch
        // configuration, so the number has exactly one home rather than being repeated at a call site.
        OrderDraft.DefaultLifetime.ShouldBe(TimeSpan.FromHours(72));
    }

    [Fact]
    public void ADraftCannotBeStartedWithoutAnIdentity()
    {
        var refused = OrderDraft.Start(
            Guid.Empty,
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Customer,
            OrdersTestData.Now,
            OrderDraft.DefaultLifetime);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("id");
    }

    [Fact]
    public void ADraftCannotBeStartedOutsideAnOrganisation()
    {
        var refused = OrderDraft.Start(
            OrdersTestData.Id("draft"),
            Guid.Empty,
            OrdersTestData.Branch,
            OrdersTestData.Customer,
            OrdersTestData.Now,
            OrderDraft.DefaultLifetime);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("organisationId");
    }

    [Fact]
    public void ADraftCannotBeStartedOutsideABranch()
    {
        // A draft with no branch could not be shared with the right people, could not be swept by the
        // right retention job, and would leave the confirmation with no sequence to take its number from.
        var refused = OrderDraft.Start(
            OrdersTestData.Id("draft"),
            OrdersTestData.Organisation,
            Guid.Empty,
            OrdersTestData.Customer,
            OrdersTestData.Now,
            OrderDraft.DefaultLifetime);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.no-branch-in-context");
    }

    [Fact]
    public void ADraftCannotBeStartedWithoutACustomer()
    {
        var refused = OrderDraft.Start(
            OrdersTestData.Id("draft"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            Guid.Empty,
            OrdersTestData.Now,
            OrderDraft.DefaultLifetime);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("customerId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADraftCannotBeStartedWithAWindowOfNoTimeAtAll(int hours)
    {
        // A window of zero or less would make the draft expired the moment it was started, so the person
        // at the counter would be refused by the next command with no way to tell why.
        var refused = OrderDraft.Start(
            OrdersTestData.Id("draft"),
            OrdersTestData.Organisation,
            OrdersTestData.Branch,
            OrdersTestData.Customer,
            OrdersTestData.Now,
            TimeSpan.FromHours(hours));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.draft-lifetime-not-positive");
    }

    /* Expiry ------------------------------------------------------------------------------------- */

    [Fact]
    public void ADraftIsExpiredFromTheInstantItsWindowCloses()
    {
        var draft = OrdersTestData.Draft(TimeSpan.FromHours(24));

        draft.IsExpiredAt(draft.ExpiresAt.AddTicks(-1)).ShouldBeFalse();
        draft.IsExpiredAt(draft.ExpiresAt).ShouldBeTrue();
        draft.IsExpiredAt(draft.ExpiresAt.AddTicks(1)).ShouldBeTrue();
    }

    [Fact]
    public void AnExpiredDraftCannotBecomeAnOrderEvenBeforeTheRetentionJobHasSweptIt()
    {
        // The prices, the availability and the promised dates in it were agreed three days ago.
        var draft = WithOneGarment();

        var refused = draft.Consume(draft.ExpiresAt);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.draft-expired");
        draft.IsOpen.ShouldBeTrue();
    }

    /// <summary>
    /// The reasoning <c>Consume</c> gives for refusing a draft that outlived its window — the prices, the
    /// availability and the promised dates in it were agreed three days ago — is a reason not to go on
    /// building on one either. With the guard on <c>Consume</c> alone, the counter could photograph
    /// material, add garments and declare dependencies onto a draft that was already too old to confirm and
    /// find out only when somebody pressed confirm, with the session's whole work to do again.
    /// </summary>
    [Fact]
    public void AnExpiredDraftCannotBeWrittenToEither()
    {
        var draft = WithOneGarment();
        var tooLate = draft.ExpiresAt;

        var attempts = new List<Result>
        {
            draft.SetCustomer(OrdersTestData.Id("someone-else"), tooLate, OrdersTestData.Actor),
            draft.SetSchedule(OrdersTestData.DueDate, "Late", tooLate, OrdersTestData.Actor),
            draft.AddGarment(
                OrdersTestData.Id("second"),
                OrdersTestData.GarmentContent(),
                tooLate,
                OrdersTestData.Actor),
            draft.SaveGarment(
                OrdersTestData.Id("first"),
                OrdersTestData.GarmentContent(),
                tooLate,
                OrdersTestData.Actor),
            draft.RemoveGarment(OrdersTestData.Id("first"), tooLate, OrdersTestData.Actor),
            draft.DeclareDependency(
                OrdersTestData.Id("first"),
                OrdersTestData.Id("second"),
                JobDependencyKind.FinishBefore,
                reason: null,
                tooLate,
                OrdersTestData.Actor),
            draft.WithdrawDependency(
                OrdersTestData.Id("first"),
                OrdersTestData.Id("second"),
                JobDependencyKind.FinishBefore,
                tooLate,
                OrdersTestData.Actor),
        };

        foreach (var refused in attempts)
        {
            refused.IsFailure.ShouldBeTrue();
            refused.Error.Code.ShouldBe("orders.draft-expired");
        }

        draft.Garments.Count.ShouldBe(1);
    }

    [Fact]
    public void AnExpiredDraftIsRefusedForBeingOldBeforeItIsRefusedForBeingEmpty()
    {
        // The state questions are answered first, so the counter is told the draft is too old rather than
        // sent to add a garment to something that could never be confirmed anyway.
        var draft = OrdersTestData.Draft(TimeSpan.FromHours(1));

        var refused = draft.Consume(draft.ExpiresAt);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.draft-expired");
    }

    /* Garment sections --------------------------------------------------------------------------- */

    [Fact]
    public void GarmentSectionsAreNumberedFromOneInTheOrderTheyWereAdded()
    {
        var draft = OrdersTestData.Draft();

        Add(draft, "first");
        Add(draft, "second");
        Add(draft, "third");

        draft.Garments.Select(garment => garment.Position).ShouldBe(Enumerable.Range(1, 3));
    }

    [Fact]
    public void AddingAGarmentTwiceUnderOneIdentifierIsRefusedRatherThanTreatedAsASave()
    {
        // The two requests may describe different garments, and quietly overwriting one with the other is
        // the loss the per-section lock exists to prevent.
        var draft = OrdersTestData.Draft();
        Add(draft, "first");

        var refused = draft.AddGarment(
            OrdersTestData.Id("first"),
            OrdersTestData.GarmentContent(),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-already-on-draft");
        draft.Garments.Count.ShouldBe(1);
    }

    [Fact]
    public void AGarmentSectionCannotBeAddedWithoutAnIdentity()
    {
        var draft = OrdersTestData.Draft();

        var refused = draft.AddGarment(
            Guid.Empty,
            OrdersTestData.GarmentContent(),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("garmentId");
    }

    [Fact]
    public void SavingAGarmentThatIsNotOnTheDraftIsRefused()
    {
        var draft = WithOneGarment();

        var refused = draft.SaveGarment(
            OrdersTestData.Id("a-section-of-another-draft"),
            OrdersTestData.GarmentContent(),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-not-on-draft");
    }

    [Fact]
    public void SavingOneSectionLeavesEveryOtherSectionAlone()
    {
        // The section is the unit the per-garment lock protects, because a draft is built by two or three
        // people between them and last-writer-wins over the whole draft loses a garment.
        var draft = OrdersTestData.Draft();
        Add(draft, "first");
        Add(draft, "second");

        draft.SaveGarment(
            OrdersTestData.Id("first"),
            OrdersTestData.GarmentContent(categoryKey: "saree-fall"),
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.FindGarment(OrdersTestData.Id("first"))!.CategoryKey.ShouldBe("saree-fall");
        draft.FindGarment(OrdersTestData.Id("second"))!.CategoryKey.ShouldBe("blouse");
    }

    [Fact]
    public void RemovingASectionTakesEveryDependencyNamingItInEitherDirection()
    {
        // A dependency is a statement about two garments and only one of them holds the row. Leaving the
        // other half behind would carry a prerequisite naming a garment the order does not have into the
        // confirmation, where it would block a job against nothing (INV-JOB-09).
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");
        Add(draft, "skirt");

        Declare(draft, "blouse", "lining", JobDependencyKind.FinishBefore);
        Declare(draft, "skirt", "lining", JobDependencyKind.DeliverTogether);
        Declare(draft, "lining", "skirt", JobDependencyKind.FinishBefore);

        draft.RemoveGarment(OrdersTestData.Id("lining"), Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.FindGarment(OrdersTestData.Id("lining")).ShouldBeNull();
        draft.FindGarment(OrdersTestData.Id("blouse"))!.Dependencies.ShouldBeEmpty();
        draft.FindGarment(OrdersTestData.Id("skirt"))!.Dependencies.ShouldBeEmpty();
    }

    [Fact]
    public void RemovingASectionThatIsNotOnTheDraftIsRefused()
    {
        var draft = WithOneGarment();

        var refused = draft.RemoveGarment(
            OrdersTestData.Id("a-section-of-another-draft"),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-not-on-draft");
        draft.Garments.Count.ShouldBe(1);
    }

    [Fact]
    public void PositionsAreNotRenumberedWhenASectionIsRemoved()
    {
        // Renumbering would move a garment out from under somebody editing it on another device, and the
        // job index that reaches the job number is assigned at confirmation rather than here.
        var draft = OrdersTestData.Draft();
        Add(draft, "first");
        Add(draft, "second");
        Add(draft, "third");

        draft.RemoveGarment(OrdersTestData.Id("second"), Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.FindGarment(OrdersTestData.Id("first"))!.Position.ShouldBe(1);
        draft.FindGarment(OrdersTestData.Id("third"))!.Position.ShouldBe(3);
    }

    /* Dependencies ------------------------------------------------------------------------------- */

    [Fact]
    public void ADependencyMustNameASectionOfTheSameDraft()
    {
        // A dependency across two drafts could not survive confirmation, which turns exactly one draft
        // into exactly one order.
        var draft = WithOneGarment();

        var unknownPrerequisite = draft.DeclareDependency(
            OrdersTestData.Id("first"),
            OrdersTestData.Id("a-section-of-another-draft"),
            JobDependencyKind.FinishBefore,
            reason: null,
            Later,
            OrdersTestData.Actor);

        var unknownDependent = draft.DeclareDependency(
            OrdersTestData.Id("a-section-of-another-draft"),
            OrdersTestData.Id("first"),
            JobDependencyKind.FinishBefore,
            reason: null,
            Later,
            OrdersTestData.Actor);

        unknownPrerequisite.IsFailure.ShouldBeTrue();
        unknownPrerequisite.Error.Code.ShouldBe("orders.garment-not-on-draft");
        unknownDependent.IsFailure.ShouldBeTrue();
        unknownDependent.Error.Code.ShouldBe("orders.garment-not-on-draft");
    }

    [Fact]
    public void AGarmentCannotWaitForItself()
    {
        var draft = WithOneGarment();

        var refused = draft.DeclareDependency(
            OrdersTestData.Id("first"),
            OrdersTestData.Id("first"),
            JobDependencyKind.FinishBefore,
            reason: null,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.dependency-on-itself");
    }

    [Fact]
    public void TheSameDependencyDeclaredTwiceIsRefused()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");
        Declare(draft, "blouse", "lining", JobDependencyKind.FinishBefore);

        var refused = draft.DeclareDependency(
            OrdersTestData.Id("blouse"),
            OrdersTestData.Id("lining"),
            JobDependencyKind.FinishBefore,
            reason: null,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.duplicate-dependency");
        draft.FindGarment(OrdersTestData.Id("blouse"))!.Dependencies.Count.ShouldBe(1);
    }

    [Fact]
    public void TheOtherKindOfDependencyOnTheSameSectionIsNotADuplicate()
    {
        // "Finish this one first" and "deliver the two together" are different promises, enforced by
        // different gates.
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");

        Declare(draft, "blouse", "lining", JobDependencyKind.FinishBefore);
        Declare(draft, "blouse", "lining", JobDependencyKind.DeliverTogether);

        draft.FindGarment(OrdersTestData.Id("blouse"))!.Dependencies.Count.ShouldBe(2);
    }

    [Fact]
    public void ADependencyReasonLongerThanTheColumnIsRefused()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");

        var refused = draft.DeclareDependency(
            OrdersTestData.Id("blouse"),
            OrdersTestData.Id("lining"),
            JobDependencyKind.FinishBefore,
            new string('x', OrderDraftGarmentDependency.MaximumReasonLength + 1),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("reason");
    }

    [Fact]
    public void WithdrawingADependencyThatWasNeverDeclaredIsRefused()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");

        var refused = draft.WithdrawDependency(
            OrdersTestData.Id("blouse"),
            OrdersTestData.Id("lining"),
            JobDependencyKind.FinishBefore,
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.dependency-not-declared");
    }

    [Fact]
    public void WithdrawingADependencyLeavesTheOtherKindStanding()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "lining");
        Add(draft, "blouse");
        Declare(draft, "blouse", "lining", JobDependencyKind.FinishBefore);
        Declare(draft, "blouse", "lining", JobDependencyKind.DeliverTogether);

        draft.WithdrawDependency(
            OrdersTestData.Id("blouse"),
            OrdersTestData.Id("lining"),
            JobDependencyKind.FinishBefore,
            Later,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.FindGarment(OrdersTestData.Id("blouse"))!
            .Dependencies.ShouldHaveSingleItem()
            .Kind.ShouldBe(JobDependencyKind.DeliverTogether);
    }

    /* The customer and the schedule -------------------------------------------------------------- */

    [Fact]
    public void PointingTheDraftAtADifferentCustomerKeepsTheGarmentSections()
    {
        // Correcting a mis-selection at the counter is ordinary, and the sections describe the garments
        // rather than the person.
        var draft = WithOneGarment();
        var reallyFor = OrdersTestData.Id("the-other-customer");

        draft.SetCustomer(reallyFor, Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.CustomerId.ShouldBe(reallyFor);
        draft.Garments.Count.ShouldBe(1);
    }

    [Fact]
    public void TheDraftCannotBePointedAtNobody()
    {
        var draft = OrdersTestData.Draft();

        var refused = draft.SetCustomer(Guid.Empty, Later, OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-required");
        refused.Error.Target.ShouldBe("customerId");
    }

    [Fact]
    public void OrderLevelNotesLongerThanTheColumnAreRefused()
    {
        var draft = OrdersTestData.Draft();

        var refused = draft.SetSchedule(
            OrdersTestData.DueDate,
            new string('x', OrderDraft.MaximumNotesLength + 1),
            Later,
            OrdersTestData.Actor);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("notes");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OrderLevelNotesNobodyWroteAreStoredAsNothing(string? notes)
    {
        var draft = OrdersTestData.Draft();

        draft.SetSchedule(OrdersTestData.DueDate, notes, Later, OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

        draft.Notes.ShouldBeNull();
        draft.DueDate.ShouldBe(OrdersTestData.DueDate);
    }

    /* What confirmation would refuse ------------------------------------------------------------- */

    [Fact]
    public void TheSectionsConfirmationWouldRefuseAreNamedAllAtOnceAndInDisplayOrder()
    {
        // The application answers with one field error listing every garment that has neither a confirmed
        // measurement nor an explicit reuse, rather than sending somebody back once per garment.
        var draft = OrdersTestData.Draft();
        Add(draft, "first", MeasurementIntent.TakeLater);
        Add(draft, "second");
        Add(draft, "third", MeasurementIntent.TakeNow);

        draft.GarmentsWithoutMeasurements()
            .Select(garment => garment.Position)
            .ShouldBe(new List<int> { 1, 3 });
    }

    [Theory]
    [InlineData(MeasurementIntent.Undecided)]
    [InlineData(MeasurementIntent.TakeNow)]
    [InlineData(MeasurementIntent.TakeLater)]
    public void TakingAMeasurementLaterIsAPlanAndNotAMeasurement(MeasurementIntent intent)
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "first", intent);

        draft.FindGarment(OrdersTestData.Id("first"))!.HasMeasurementDecision.ShouldBeFalse();
        draft.GarmentsWithoutMeasurements().Count.ShouldBe(1);
    }

    /// <summary>
    /// The <strong>Measurements needed</strong> queue is built from this value and confirmation refuses a
    /// garment nobody measured by reading it, so an intent from a cast — which is what a deserialiser produces
    /// from a number it did not recognise — would be counted as a decision that was never taken, and persisted
    /// that way.
    /// </summary>
    [Fact]
    public void AMeasurementIntentThatIsNoneOfTheFourIsRefused()
    {
        var content = OrderDraftGarmentContent.Create(
            "blouse",
            "stitch-new",
            OrdersTestData.CatalogVersion,
            designSelectionDraftId: null,
            (MeasurementIntent)99,
            measurementVersionId: null,
            OrdersTestData.Id("measurement-template"),
            OrdersTestData.DueDate,
            instructions: null,
            referenceMediaIds: null);

        content.IsFailure.ShouldBeTrue();
        content.Error.Code.ShouldBe("orders.value-not-understood");
        content.Error.Target.ShouldBe("measurementIntent");
    }

    /// <summary>
    /// The same hole on the draft's own dependency rows: a kind that is neither of INV-JOB-09's two is carried
    /// into the confirmation and then enforced by neither gate, so it is refused where it is declared.
    /// </summary>
    [Fact]
    public void ADependencyOfNeitherKindIsRefusedOnADraftToo()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "first");
        Add(draft, "second");

        var declared = draft.DeclareDependency(
            OrdersTestData.Id("second"),
            OrdersTestData.Id("first"),
            (JobDependencyKind)99,
            reason: null,
            OrdersTestData.Now,
            OrdersTestData.Actor);

        declared.IsFailure.ShouldBeTrue();
        declared.Error.Code.ShouldBe("orders.value-not-understood");
        declared.Error.Target.ShouldBe("kind");
        draft.FindGarment(OrdersTestData.Id("second"))!.Dependencies.ShouldBeEmpty();
    }

    [Fact]
    public void AReusedMeasurementIsTheDecisionConfirmationActsOn()
    {
        var draft = WithOneGarment();

        draft.FindGarment(OrdersTestData.Id("first"))!.HasMeasurementDecision.ShouldBeTrue();
        draft.GarmentsWithoutMeasurements().ShouldBeEmpty();
    }

    /* Becoming an order -------------------------------------------------------------------------- */

    [Fact]
    public void ConsumingADraftMarksItSpent()
    {
        var draft = WithOneGarment();

        draft.Consume(Later).IsSuccess.ShouldBeTrue();

        draft.IsOpen.ShouldBeFalse();
        draft.ConsumedAt.ShouldBe(Later);
    }

    [Fact]
    public void ADraftIsConsumedExactlyOnceSoARetriedConfirmationCannotMakeASecondOrder()
    {
        // This is where the idempotency of confirmation lives in the domain: the second attempt is refused
        // here rather than quietly producing a second commitment against the same draft.
        var draft = WithOneGarment();
        draft.Consume(Later).IsSuccess.ShouldBeTrue();
        var when = draft.ConsumedAt;

        var refused = draft.Consume(Later.AddMinutes(1));

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.draft-already-confirmed");
        draft.ConsumedAt.ShouldBe(when);
    }

    [Fact]
    public void ADraftWithNoGarmentSectionCannotBecomeAnOrder()
    {
        var draft = OrdersTestData.Draft();

        var refused = draft.Consume(Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.draft-has-no-garments");
        draft.IsOpen.ShouldBeTrue();
    }

    /// <summary>
    /// Section 2.1 makes "every garment has a confirmed measurement version or an explicit reuse" a
    /// precondition of the confirmation, and the draft is the only thing that can see it —
    /// <c>Order.Confirm</c> never sees a draft at all. With no guard here the precondition was enforced
    /// nowhere. <c>GarmentsWithoutMeasurements</c> stays how the application names every such garment at
    /// once, which is the field error section 2.1 asks for.
    /// </summary>
    [Fact]
    public void ADraftWithAGarmentNobodyDecidedTheMeasurementsForCannotBecomeAnOrder()
    {
        var draft = WithOneGarment();
        Add(draft, "second", MeasurementIntent.TakeLater);

        var refused = draft.Consume(Later);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.garment-measurement-not-decided");
        refused.Error.Target.ShouldBe("garments");
        draft.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void EveryCommandIsRefusedOnceTheDraftHasBecomeAnOrder()
    {
        var draft = WithOneGarment();
        Add(draft, "second");
        draft.Consume(Later).IsSuccess.ShouldBeTrue();

        var customer = draft.SetCustomer(OrdersTestData.Id("someone-else"), Later, OrdersTestData.Actor);
        var schedule = draft.SetSchedule(OrdersTestData.DueDate, "Late", Later, OrdersTestData.Actor);
        var added = draft.AddGarment(
            OrdersTestData.Id("third"),
            OrdersTestData.GarmentContent(),
            Later,
            OrdersTestData.Actor);
        var saved = draft.SaveGarment(
            OrdersTestData.Id("first"),
            OrdersTestData.GarmentContent(),
            Later,
            OrdersTestData.Actor);
        var removed = draft.RemoveGarment(OrdersTestData.Id("first"), Later, OrdersTestData.Actor);
        var declared = draft.DeclareDependency(
            OrdersTestData.Id("second"),
            OrdersTestData.Id("first"),
            JobDependencyKind.FinishBefore,
            reason: null,
            Later,
            OrdersTestData.Actor);
        var withdrawn = draft.WithdrawDependency(
            OrdersTestData.Id("second"),
            OrdersTestData.Id("first"),
            JobDependencyKind.FinishBefore,
            Later,
            OrdersTestData.Actor);

        var attempts = new List<Result> { customer, schedule, added, saved, removed, declared, withdrawn };

        foreach (var refused in attempts)
        {
            refused.IsFailure.ShouldBeTrue();
            refused.Error.Code.ShouldBe("orders.draft-already-confirmed");
        }

        draft.Garments.Count.ShouldBe(2);
    }

    private static OrderDraft WithOneGarment()
    {
        var draft = OrdersTestData.Draft();
        Add(draft, "first");

        return draft;
    }

    private static void Add(
        OrderDraft draft,
        string name,
        MeasurementIntent intent = MeasurementIntent.ReuseVersion)
        => draft.AddGarment(
            OrdersTestData.Id(name),
            OrdersTestData.GarmentContent(intent),
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();

    private static void Declare(
        OrderDraft draft,
        string dependent,
        string prerequisite,
        JobDependencyKind kind)
        => draft.DeclareDependency(
            OrdersTestData.Id(dependent),
            OrdersTestData.Id(prerequisite),
            kind,
            reason: null,
            OrdersTestData.Now,
            OrdersTestData.Actor).IsSuccess.ShouldBeTrue();
}
