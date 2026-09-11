using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The values the Orders unit tests are built from.
/// </summary>
/// <remarks>
/// <para>
/// Every name, number and measurement here is synthetic and belongs to nobody. The identifiers are derived
/// from a name rather than generated, so a failure names the same value twice and a test that wants "the
/// same garment" gets it without threading a variable through.
/// </para>
/// <para>
/// The aggregate takes the instant and every identifier as a parameter (ARCH-014, ARCH-015), so a fixed
/// <see cref="Now"/> is all the clock these tests need and nothing here reaches for one.
/// </para>
/// </remarks>
internal static class OrdersTestData
{
    /// <summary>The instant every test runs at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 7, 11, 5, 0, TimeSpan.FromHours(5.5));

    /// <summary>The branch-local date the tests treat as today.</summary>
    public static readonly DateOnly Today = new(2026, 5, 7);

    /// <summary>The promised date the tests confirm orders against.</summary>
    public static readonly DateOnly DueDate = new(2026, 5, 21);

    /// <summary>The organisation the tests run in.</summary>
    public static readonly Guid Organisation = Id("organisation");

    /// <summary>The branch the tests take orders at.</summary>
    public static readonly Guid Branch = Id("branch-cbe01");

    /// <summary>A second branch, for the branch-scope guards.</summary>
    public static readonly Guid OtherBranch = Id("branch-mdu01");

    /// <summary>The member of staff the tests act as.</summary>
    public static readonly Guid Actor = Id("reception");

    /// <summary>
    /// The member of staff who approves a hold, where the branch's hold policy asks for one.
    /// </summary>
    /// <remarks>
    /// A different person from <see cref="Actor"/>, because the approval a hold records is somebody else's
    /// (<c>docs/architecture/module-ownership.md</c> section 5.5, <c>docs/prd/state-transitions.md</c>
    /// section 3.2). The policy itself — which holds need one, and from whom — is unsettled and is not
    /// asserted anywhere; these tests only check that what was supplied is what is stored.
    /// </remarks>
    public static readonly Guid Approver = Id("branch-manager");

    /// <summary>The customer the tests commit to.</summary>
    public static readonly Guid Customer = Id("customer");

    /// <summary>The published catalogue version every snapshot is taken against.</summary>
    public static readonly Guid CatalogVersion = Id("catalog-version");

    /// <summary>The price-list version every priced result is calculated from.</summary>
    public static readonly Guid PriceListVersion = Id("price-list-version");

    /// <summary>The tax configuration version every priced result is calculated from.</summary>
    public static readonly Guid TaxConfigurationVersion = Id("tax-configuration-version");

    /// <summary>The workflow definition a garment job records at confirmation.</summary>
    public static readonly Guid WorkflowDefinition = Id("workflow-definition");

    /// <summary>The workflow version a garment job pins at start of production.</summary>
    public static readonly Guid WorkflowVersion = Id("workflow-version");

    /// <summary>A deterministic identifier for a name, so a failure names the same value twice.</summary>
    /// <param name="name">Any name.</param>
    /// <returns>The identifier.</returns>
    public static Guid Id(string name)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    /// <summary>The identity the garment at one position of an order is created with.</summary>
    /// <param name="jobIndex">The one-based position within the order.</param>
    /// <returns>The identifier.</returns>
    public static Guid GarmentId(int jobIndex) => Id($"garment-{jobIndex}");

    /// <summary>The financial year the display numbers are allocated in.</summary>
    /// <param name="startYear">The year the financial year opens in.</param>
    /// <returns>The financial year.</returns>
    public static FinancialYear Year(int startYear = 2026) => FinancialYear.FromStartYear(startYear).Value;

    /// <summary>An allocated order number.</summary>
    /// <param name="sequence">The position within the branch and financial year.</param>
    /// <param name="branchCode">The branch code the sequence belongs to.</param>
    /// <returns>The number.</returns>
    public static OrderNumber Number(long sequence = 1, string branchCode = "CBE01")
        => OrderNumber.Create(branchCode, Year(), sequence).Value;

    /// <summary>A garment number minted from an order's own number.</summary>
    /// <param name="orderNumber">The order's number.</param>
    /// <param name="jobIndex">The one-based position within the order.</param>
    /// <returns>The number.</returns>
    public static GarmentJobNumber JobNumber(OrderNumber orderNumber, int jobIndex)
        => GarmentJobNumber.For(orderNumber, jobIndex).Value;

    /// <summary>An allocated estimate number, from the estimate series and never the invoice series.</summary>
    /// <param name="sequence">The position within the branch and financial year.</param>
    /// <param name="branchCode">The branch code the sequence belongs to.</param>
    /// <returns>The number.</returns>
    public static EstimateNumber QuoteNumber(long sequence = 1, string branchCode = "CBE01")
        => EstimateNumber.Create(branchCode, Year(), sequence).Value;

    /// <summary>A priced result carrying all three configuration versions and one currency.</summary>
    /// <param name="grandTotal">What the document asks for, in rupees.</param>
    /// <param name="calculatedAt">When Billing calculated it.</param>
    /// <returns>The snapshot.</returns>
    public static PriceSnapshot Price(decimal grandTotal = 2400m, DateTimeOffset? calculatedAt = null)
        => PriceSnapshot.Create(
            CatalogVersion,
            PriceListVersion,
            TaxConfigurationVersion,
            Money.Rupees(grandTotal),
            Money.Zero,
            Money.Rupees(grandTotal),
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            Money.Rupees(grandTotal),
            calculatedAt ?? Now).Value;

    /// <summary>A measurement copy to freeze onto a garment job.</summary>
    /// <param name="measurementVersionId">The version copied from, as provenance only.</param>
    /// <returns>The snapshot.</returns>
    public static MeasurementSnapshot Measurements(Guid? measurementVersionId = null)
        => MeasurementSnapshot.Create(
            measurementVersionId ?? Id("measurement-version"),
            Id("measurement-template"),
            Id("measurement-template-version"),
            versionNumber: 1,
            takenAt: Now.AddDays(-1),
            takenBy: Actor,
            values: [MeasuredValue.Create("chest", 860m, "cm", choice: null, acknowledged: false).Value],
            frozenAt: Now).Value;

    /// <summary>A design copy to freeze onto a garment job.</summary>
    /// <param name="optionCode">The chosen option, so two garments can differ.</param>
    /// <param name="categoryKey">
    /// What the copy says the garment is, so a copy that answers a different garment can be built.
    /// </param>
    /// <param name="serviceTypeKey">What the copy says the service is, for the same reason.</param>
    /// <returns>The snapshot.</returns>
    public static DesignSnapshot Design(
        string optionCode = "round",
        string categoryKey = "blouse",
        string serviceTypeKey = "stitch-new")
        => DesignSnapshot.Create(
            CatalogVersion,
            categoryKey,
            "Blouse",
            serviceTypeKey,
            "Stitch a new garment",
            [
                DesignSelection.Create(
                    "neckline",
                    "Neckline",
                    groupDisplayOrder: 1,
                    optionCode,
                    "Round",
                    optionDisplayOrder: 1,
                    optionVersion: 1,
                    priceListItemCode: null,
                    illustrationMediaId: null,
                    illustrationAlternativeText: null).Value,
            ],
            garmentInstructions: null,
            conditionalNotes: null,
            frozenAt: Now).Value;

    /// <summary>One validated garment of a confirmation.</summary>
    /// <param name="orderNumber">The order's number, which the garment's number is minted from.</param>
    /// <param name="jobIndex">The one-based position within the order.</param>
    /// <param name="garmentJobId">Identity to give the job, where the default is not wanted.</param>
    /// <param name="dueDate">The promised date for this garment.</param>
    /// <param name="dependencies">What this garment waits for, or is delivered with.</param>
    /// <returns>The specification.</returns>
    public static GarmentJobSpecification Garment(
        OrderNumber orderNumber,
        int jobIndex = 1,
        Guid? garmentJobId = null,
        DateOnly? dueDate = null,
        IReadOnlyCollection<GarmentJobDependencySpecification>? dependencies = null)
        => GarmentJobSpecification.Create(
            garmentJobId ?? GarmentId(jobIndex),
            JobNumber(orderNumber, jobIndex),
            jobIndex,
            "blouse",
            "stitch-new",
            WorkflowDefinition,
            Measurements(),
            Design(),
            Price(),
            dueDate ?? DueDate,
            referenceMediaIds: null,
            dependencies).Value;

    /// <summary>A confirmed order carrying one garment per position, numbered from position one.</summary>
    /// <param name="garments">How many garments the confirmation carries.</param>
    /// <param name="orderNumber">The allocated order number, where the default is not wanted.</param>
    /// <param name="estimateId">The accepted estimate, where one was issued.</param>
    /// <returns>The order.</returns>
    public static Order ConfirmedOrder(int garments = 1, OrderNumber? orderNumber = null, Guid? estimateId = null)
    {
        var number = orderNumber ?? Number();
        var specifications = new List<GarmentJobSpecification>();

        for (var jobIndex = 1; jobIndex <= garments; jobIndex++)
        {
            specifications.Add(Garment(number, jobIndex));
        }

        return ConfirmedOrderOf(specifications, number, estimateId);
    }

    /// <summary>A confirmed order over garments the test composed itself.</summary>
    /// <param name="garments">The validated garments.</param>
    /// <param name="orderNumber">The allocated order number.</param>
    /// <param name="estimateId">The accepted estimate, where one was issued.</param>
    /// <returns>The order.</returns>
    public static Order ConfirmedOrderOf(
        IReadOnlyCollection<GarmentJobSpecification> garments,
        OrderNumber? orderNumber = null,
        Guid? estimateId = null)
        => Order.Confirm(
            Id("order"),
            Organisation,
            Branch,
            Customer,
            orderNumber ?? Number(),
            Id("draft"),
            estimateId,
            DueDate,
            notes: null,
            Price(),
            garments,
            Id("revision-1"),
            Now,
            Actor).Value;

    /// <summary>An open order draft with no garment section on it yet.</summary>
    /// <param name="lifetime">How long the branch lets a draft stay work in progress.</param>
    /// <param name="customerId">The customer it is being built for.</param>
    /// <returns>The draft.</returns>
    public static OrderDraft Draft(TimeSpan? lifetime = null, Guid? customerId = null)
        => OrderDraft.Start(
            Id("draft"),
            Organisation,
            Branch,
            customerId ?? Customer,
            Now,
            lifetime ?? OrderDraft.DefaultLifetime,
            Actor).Value;

    /// <summary>Validated content for one garment section of a draft.</summary>
    /// <param name="intent">What the section says about its measurements.</param>
    /// <param name="measurementVersionId">The version being reused, where one is named.</param>
    /// <param name="categoryKey">The garment category key.</param>
    /// <param name="referenceMediaIds">Reference and material images, by Media id.</param>
    /// <returns>The content.</returns>
    public static OrderDraftGarmentContent GarmentContent(
        MeasurementIntent intent = MeasurementIntent.ReuseVersion,
        Guid? measurementVersionId = null,
        string categoryKey = "blouse",
        IReadOnlyCollection<Guid>? referenceMediaIds = null)
        => OrderDraftGarmentContent.Create(
            categoryKey,
            "stitch-new",
            CatalogVersion,
            designSelectionDraftId: null,
            intent,
            intent is MeasurementIntent.ReuseVersion
                ? measurementVersionId ?? Id("measurement-version")
                : measurementVersionId,
            measurementTemplateId: Id("measurement-template"),
            DueDate,
            instructions: null,
            referenceMediaIds).Value;

    /// <summary>An issued, outstanding estimate against a draft.</summary>
    /// <param name="estimateId">Identity of the estimate.</param>
    /// <param name="validUntil">The branch-local date it stops being honourable.</param>
    /// <returns>The estimate.</returns>
    public static Estimate IssuedEstimate(Guid? estimateId = null, DateOnly? validUntil = null)
        => Estimate.Issue(
            estimateId ?? Id("estimate-1"),
            Organisation,
            Branch,
            Id("draft"),
            Customer,
            QuoteNumber(),
            Price(),
            Today,
            validUntil ?? Today.AddDays(14),
            Now,
            Actor).Value;

    /// <summary>
    /// Gate facts with every predicate satisfied, so a caller states only the one it wants to fail.
    /// </summary>
    /// <param name="workflowComplete">Every non-skippable phase of the pinned version is complete.</param>
    /// <param name="qcPassed">The latest QC result is a pass.</param>
    /// <param name="reworkOpen">A rework task is open.</param>
    /// <param name="documentationComplete">Required evidence is present and ready.</param>
    /// <param name="partialDeliveryPermitted">The branch policy permits partial delivery.</param>
    /// <param name="custodyGateEnabled">Whether the custody predicate is enforced.</param>
    /// <param name="custody">What Custody said about the custodian.</param>
    /// <returns>The inputs.</returns>
    public static ReadyGateInputs GateInputs(
        bool workflowComplete = true,
        bool qcPassed = true,
        bool reworkOpen = false,
        bool documentationComplete = true,
        bool partialDeliveryPermitted = true,
        bool custodyGateEnabled = false,
        CustodyReconciliation custody = CustodyReconciliation.Reconciled)
        => ReadyGateInputs.Create(
            workflowComplete,
            incompletePhaseCode: null,
            qcPassed,
            reworkOpen,
            failedQcReference: null,
            documentationComplete,
            missingEvidenceReference: null,
            partialDeliveryPermitted,
            custodyGateEnabled,
            custody,
            openCustodyCaseReference: null).Value;

    /// <summary>Takes one garment job into production.</summary>
    /// <param name="order">The order the garment belongs to.</param>
    /// <param name="garmentJobId">The garment.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <returns>The same order, so a fixture reads in one expression.</returns>
    public static Order InProduction(
        Order order,
        Guid garmentJobId,
        ReadyAggregation aggregation = ReadyAggregation.EveryDeliverableJob)
    {
        Ensure(order.StartProduction(garmentJobId, WorkflowVersion, [], aggregation, Now, Actor));

        return order;
    }

    /// <summary>
    /// Takes one garment job through production and the gate to ready.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ReadyGate"/> and not around it: ready state has exactly one writer (INV-JOB-07),
    /// and a fixture that set the status directly could not, because no such route exists.
    /// </remarks>
    /// <param name="order">The order the garment belongs to.</param>
    /// <param name="garmentJobId">The garment.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <returns>The same order.</returns>
    public static Order Ready(
        Order order,
        Guid garmentJobId,
        ReadyAggregation aggregation = ReadyAggregation.EveryDeliverableJob)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.FindJob(garmentJobId)?.Status is GarmentJobStatus.Confirmed)
        {
            InProduction(order, garmentJobId, aggregation);
        }

        var outcome = ReadyGate.Evaluate(order, garmentJobId, GateInputs(), Now);
        Ensure(outcome);
        Ensure(order.ApplyReadyGate(garmentJobId, outcome.Value, aggregation, Now));

        return order;
    }

    /// <summary>Hands one garment job over at the door.</summary>
    /// <param name="order">The order the garment belongs to.</param>
    /// <param name="garmentJobId">The garment.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <returns>The same order.</returns>
    public static Order Delivered(
        Order order,
        Guid garmentJobId,
        ReadyAggregation aggregation = ReadyAggregation.EveryDeliverableJob)
    {
        ArgumentNullException.ThrowIfNull(order);

        Ready(order, garmentJobId, aggregation);
        Ensure(order.ConfirmDelivery(garmentJobId, aggregation, Now, Actor));

        return order;
    }

    /// <summary>Cancels one garment job with a configured reason code and a reason.</summary>
    /// <param name="order">The order the garment belongs to.</param>
    /// <param name="garmentJobId">The garment.</param>
    /// <param name="aggregation">Which jobs the branch dispatch policy requires to be ready (SQ-02).</param>
    /// <returns>The same order.</returns>
    public static Order CancelledJob(
        Order order,
        Guid garmentJobId,
        ReadyAggregation aggregation = ReadyAggregation.EveryDeliverableJob)
    {
        ArgumentNullException.ThrowIfNull(order);

        Ensure(order.CancelJob(
            garmentJobId,
            "customer-withdrew",
            "The customer withdrew this garment at the counter.",
            [],
            aggregation,
            Now,
            Actor));

        return order;
    }

    /// <summary>
    /// Stops a fixture that could not be built from being read as the behaviour under test.
    /// </summary>
    /// <remarks>
    /// A refused step here is a defect in the test rather than a shop-floor situation, so it throws where the
    /// domain returns: the failure names the step that would otherwise have gone unnoticed.
    /// </remarks>
    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"The fixture could not be built: {result.Error.Code}.");
        }
    }
}
