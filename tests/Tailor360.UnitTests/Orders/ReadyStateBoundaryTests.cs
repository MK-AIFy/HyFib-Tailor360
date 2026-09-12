using Shouldly;
using Tailor360.Modules.Orders.Contracts.Orders;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// That the module cannot store a ready-state block reference its own published contract refuses to carry.
/// </summary>
/// <remarks>
/// <para>
/// <c>ReadyStateBlockTests</c> pins what <c>Orders.Contracts.ReadyStateBlock.Of</c> refuses. This pins the other
/// half: that nothing the Domain writes can ever reach it. The two used to disagree — the Domain accepted two
/// hundred characters of anything while the contract refused a GUID, a space and anything over forty, and refused
/// it by <em>throwing</em> — so a legitimately stored row could make <c>IOrderSnapshotQuery.GetJobAsync</c> throw
/// at a module boundary. The projection worked around it by copying the contract's four rules into
/// <c>OrderSnapshotQuery.IsPublishable</c> and dropping any reference that failed them, which duplicated a rule
/// across two assemblies and lost information across a boundary without saying so.
/// </para>
/// <para>
/// <strong>The rule is now stated once, on the write path, and this is the test that holds the two constants
/// together.</strong> ARCH-001 forbids the Domain referencing the Contracts project, so the two say the same
/// thing in two assemblies and nothing but a test can make that a guarantee — the arrangement
/// <c>DisplayNumberFormat.MaximumBranchCodeLength</c> already uses for Identity's branch code, and
/// <c>FinancialYearTokenTests</c> for the financial year's two constants.
/// </para>
/// <para>
/// <strong>Which side was wrong is a finding in itself, and <see cref="TheBoundIsTheLongestDisplayNumber"/> is
/// it.</strong> The contract's shape rules were right — <c>docs/prd/state-transitions.md</c> section 9.1 names all
/// six references and every one is a configured code or a display number, so neither free text nor an identity was
/// ever legitimate. Its <em>length</em> was not: forty is Catalog's code length, and the same field also carries a
/// sibling's garment job number, whose own bound is forty-eight.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ReadyStateBoundaryTests
{
    /// <summary>
    /// Every reference the Domain will store, in the shapes the six predicates of section 9.1 produce.
    /// </summary>
    private static readonly string[] Carriable =
    [
        "finishing",
        "CUTTING_01",
        "QC-000114",
        "fabric_short",
        "awaiting-material",
        "J-CBE01-2627-000512-02",
        "RECON-000007",
        "7",
    ];

    /// <summary>
    /// References that must never be stored: an identity in every spelling a GUID can be written, free text, an
    /// object key, and a reference longer than the boundary.
    /// </summary>
    private static readonly string[] NotCarriable =
    [
        "019bd660-4e55-7b76-9087-f60718293041",
        "019BD6604E557B769087F60718293041",
        "{019bd660-4e55-7b76-9087-f60718293041}",
        "(019bd660-4e55-7b76-9087-f60718293041)",
        "the lining has not arrived",
        "media/2026/09/019bd660.jpg",
        "qc-evidence.jpg",
        "https://example.invalid/evidence",
        "-leading-hyphen",
        "_leading_underscore",
        "QC-000114,QC-000115",
    ];

    /* The two assemblies agree ---------------------------------------------------------------------- */

    /// <summary>
    /// The whole point: what the module stores is exactly what the contract carries, in both directions.
    /// </summary>
    /// <remarks>
    /// Both directions matter. Domain-accepts-but-contract-refuses is the defect that started this — a stored row
    /// that throws at the boundary. Contract-accepts-but-domain-refuses is the other failure it would be easy to
    /// land on: a rule so tight that a legitimate phase code could not be recorded at all, and a delivery queue
    /// that then cannot say which phase.
    /// </remarks>
    [Fact]
    public void TheDomainStoresExactlyWhatTheContractCarries()
    {
        foreach (var reference in Carriable.Concat(NotCarriable).Concat([null!, "", "   "]))
        {
            var stored = ReadyGateBlock.Create(ReadyGatePredicate.WorkflowComplete, reference);

            var published = Record.Exception(
                () => ReadyStateBlock.Of(ReadyBlockReason.WorkflowComplete, reference));

            stored.IsSuccess.ShouldBe(
                published is null,
                $"'{reference}' is stored by the Domain and published by the contract, or by neither.");
        }
    }

    /// <summary>The reference the Domain keeps is the reference the contract publishes, unchanged.</summary>
    [Fact]
    public void AStoredReferenceIsPublishedUnchanged()
    {
        foreach (var reference in Carriable)
        {
            var stored = ReadyGateBlock.Create(ReadyGatePredicate.QcPassed, reference).Value;

            ReadyStateBlock.Of(ReadyBlockReason.QcPassed, stored.Reference).Reference.ShouldBe(reference);
        }
    }

    /// <summary>The two bounds are one bound, declared twice because ARCH-001 forbids one reference.</summary>
    [Fact]
    public void TheTwoAssembliesDeclareTheSameBound()
    {
        ReadyGateBlock.MaximumReferenceLength.ShouldBe(ReadyStateBlock.MaximumReferenceLength);
        ReadyGateInputs.MaximumReferenceLength.ShouldBe(ReadyGateBlock.MaximumReferenceLength);
    }

    /// <summary>
    /// The bound is the longest of the two things the field carries, and a display number is the longer.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would have failed while the contract said forty. <c>DependenciesMet</c> names a
    /// sibling's garment job number, which is bounded at forty-eight rather than at Catalog's forty, so a
    /// boundary set to forty could refuse a reference the gate legitimately produced — the boundary refusing the
    /// very thing it was published to carry.
    /// </remarks>
    [Fact]
    public void TheBoundIsTheLongestDisplayNumber()
        => ReadyStateBlock.MaximumReferenceLength.ShouldBeGreaterThanOrEqualTo(GarmentJobNumber.MaximumLength);

    /// <summary>
    /// The longest garment job number this module can mint is one a block carries and the contract publishes.
    /// </summary>
    [Fact]
    public void TheLongestJobNumberThatCanBeMintedIsCarried()
    {
        // Every part at its maximum: a sixteen-character branch code, and the widest sequence that still leaves
        // the order number inside its own forty characters. conventions.md section 3.2 says the sequence widens
        // past 999999 rather than wrapping, so the longest number is not the six-digit one a shop will ever see.
        var orderNumber = OrderNumber
            .Create("ABCDEFGHIJKLMNOP", OrdersTestData.Year(), 9_999_999_999_999_999L).Value;

        orderNumber.Value.Length.ShouldBe(OrderNumber.MaximumLength);

        var jobNumber = GarmentJobNumber.For(orderNumber, 99).Value;

        // Forty-three, and the constant it is measured against used to be forty: this is the block the gate
        // would have produced and the boundary would have thrown on.
        jobNumber.Value.Length.ShouldBeGreaterThan(40);

        ReadyGateBlock.IsCarriable(jobNumber.Value).ShouldBeTrue();
        ReadyStateBlock.Of(ReadyBlockReason.DependenciesMet, jobNumber.Value)
            .Reference.ShouldBe(jobNumber.Value);
    }

    /* Refused where it enters the module, not dropped where it leaves --------------------------------- */

    /// <summary>
    /// Each of the four facts the application gathers is checked as it arrives, and the caller is told which.
    /// </summary>
    /// <remarks>
    /// The reference a caller supplied is the one thing it can correct, so a refusal naming the field is worth
    /// more than a queue screen that silently shows the reason code without the phase.
    /// <c>ReadyGateInputs</c> carries an evidence reference among the four, and an evidence key is a UUIDv7 —
    /// which is exactly the identity <c>docs/nfr/data-classification.md</c> section 5.6 and security rule 9 keep
    /// off a row another module reads, so the refusal is the point rather than a side effect.
    /// </remarks>
    [Theory]
    [InlineData("incompletePhaseCode")]
    [InlineData("failedQcReference")]
    [InlineData("missingEvidenceReference")]
    [InlineData("openCustodyCaseReference")]
    public void AFactCarryingAnIdentityIsRefusedNamingItsOwnField(string field)
    {
        var refused = Inputs(field, "019bd660-4e55-7b76-9087-f60718293041");

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("orders.reference-not-carriable");
        refused.Error.Target.ShouldBe(field);
    }

    /// <summary>Free text in a code field is refused for the same reason and in the same words.</summary>
    [Fact]
    public void AFactCarryingFreeTextIsRefused()
        => Inputs("failedQcReference", "the seam is puckered").Error.Code
            .ShouldBe("orders.reference-not-carriable");

    /// <summary>A reference longer than the boundary is too long rather than malformed.</summary>
    /// <remarks>
    /// Length is reported before shape so that a caller which sent a sentence is told what is actually wrong
    /// with it. The sentence is refused either way.
    /// </remarks>
    [Fact]
    public void AFactLongerThanTheBoundIsTooLong()
    {
        var refused = Inputs(
            "incompletePhaseCode",
            new string('a', ReadyGateInputs.MaximumReferenceLength + 1));

        refused.Error.Code.ShouldBe("orders.value-too-long");
        refused.Error.Target.ShouldBe("incompletePhaseCode");
    }

    /// <summary>A code the shop actually configures still reaches the gate untouched.</summary>
    [Fact]
    public void AConfiguredCodeIsAccepted()
        => Inputs("incompletePhaseCode", "finishing").Value.IncompletePhaseCode.ShouldBe("finishing");

    /* The hold reason code, which is the fifth reference a block carries ------------------------------ */

    /// <summary>
    /// A hold reason code is refused unless it is one a published block can carry, because
    /// <c>GarmentJob.Hold</c> writes it straight onto the gate's own <c>NoOpenHold</c> block.
    /// </summary>
    [Theory]
    [InlineData("needs new fabric")]
    [InlineData("019bd660-4e55-7b76-9087-f60718293041")]
    [InlineData("-awaiting-material")]
    public void AHoldReasonCodeAPublishedBlockCouldNotCarryIsRefused(string reasonCode)
    {
        var order = InProductionOrder(out var garmentJobId);

        var held = Hold(order, garmentJobId, reasonCode);

        held.IsFailure.ShouldBeTrue();
        held.Error.Code.ShouldBe("orders.reference-not-carriable");
        held.Error.Target.ShouldBe("reasonCode");
    }

    /// <summary>
    /// And a configured one is held, blocks the gate, and publishes through the boundary unchanged.
    /// </summary>
    /// <remarks>
    /// The end-to-end statement of the fix: a block the Domain produced on its own hottest write path is one
    /// <c>ReadyStateBlock.Of</c> accepts without the projection re-testing it, which is what makes
    /// <c>OrderSnapshotQuery.PublishedBlock</c> able to pass the reference straight through.
    /// </remarks>
    [Fact]
    public void AHeldGarmentPublishesTheCodeItWasHeldUnder()
    {
        var order = InProductionOrder(out var garmentJobId);

        Hold(order, garmentJobId, "awaiting-material").IsSuccess.ShouldBeTrue();

        var block = order.FindJob(garmentJobId)!.ReadyStateBlocks.ShouldHaveSingleItem();
        block.Predicate.ShouldBe(ReadyGatePredicate.NoOpenHold);

        ReadyStateBlock.Of(ReadyBlockReason.NoOpenHold, block.Reference)
            .Reference.ShouldBe("awaiting-material");
    }

    /// <summary>
    /// The sixth reference: a <c>DependenciesMet</c> block names a sibling's job number, and the gate produces
    /// one the boundary carries.
    /// </summary>
    [Fact]
    public void ASiblingBlockPublishesTheJobNumberItNames()
    {
        var blouse = OrdersTestData.GarmentId(1);
        var skirt = OrdersTestData.GarmentId(2);
        var number = OrdersTestData.Number();

        var order = OrdersTestData.ConfirmedOrderOf(
            [
                OrdersTestData.Garment(number, jobIndex: 1),
                OrdersTestData.Garment(
                    number,
                    jobIndex: 2,
                    dependencies:
                    [
                        new GarmentJobDependencySpecification(
                            OrdersTestData.Id("dependency-1"),
                            blouse,
                            JobDependencyKind.DeliverTogether,
                            Reason: null),
                    ]),
            ],
            number);

        OrdersTestData.InProduction(order, blouse);

        var outcome = ReadyGate.Evaluate(
            order,
            blouse,
            OrdersTestData.GateInputs(partialDeliveryPermitted: false),
            OrdersTestData.Now).Value;

        var sibling = outcome.Blocks
            .ShouldHaveSingleItem();
        sibling.Predicate.ShouldBe(ReadyGatePredicate.DependenciesMet);

        ReadyStateBlock.Of(ReadyBlockReason.DependenciesMet, sibling.Reference)
            .Reference.ShouldBe(order.FindJob(skirt)!.JobNumber.Value);
    }

    /* Fixtures --------------------------------------------------------------------------------------- */

    private static Result<ReadyGateInputs> Inputs(string field, string reference) => ReadyGateInputs.Create(
        workflowComplete: true,
        incompletePhaseCode: field == "incompletePhaseCode" ? reference : null,
        qcPassed: true,
        reworkOpen: false,
        failedQcReference: field == "failedQcReference" ? reference : null,
        documentationComplete: true,
        missingEvidenceReference: field == "missingEvidenceReference" ? reference : null,
        partialDeliveryPermitted: true,
        custodyGateEnabled: false,
        custody: CustodyReconciliation.Reconciled,
        openCustodyCaseReference: field == "openCustodyCaseReference" ? reference : null);

    private static Order InProductionOrder(out Guid garmentJobId)
    {
        var order = OrdersTestData.ConfirmedOrder();
        garmentJobId = OrdersTestData.GarmentId(1);

        return OrdersTestData.InProduction(order, garmentJobId);
    }

    private static Result Hold(Order order, Guid garmentJobId, string reasonCode)
        => order.Hold(
            garmentJobId,
            reasonCode,
            "The lining has not arrived.",
            OrdersTestData.Approver,
            ReadyAggregation.EveryDeliverableJob,
            OrdersTestData.Now,
            OrdersTestData.Actor);

}
