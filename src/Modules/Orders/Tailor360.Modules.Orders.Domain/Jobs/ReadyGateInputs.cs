using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The facts the ready-for-delivery gate needs that the Orders domain does not itself hold.
/// </summary>
/// <remarks>
/// <para>
/// Phase completion and QC belong to the workflow engine (issues #33 and #34), evidence to Media, the
/// partial-delivery policy to the branch (issue #48) and the custodian to Custody's
/// <c>ICustodyStateQuery</c>. The application gathers all five and hands them here; the gate combines them with
/// what the job and its order already know. Passing them as a value object rather than reaching for a service
/// keeps <see cref="ReadyGate"/> a pure function of its arguments, which is what makes the six predicates
/// testable one at a time.
/// </para>
/// <para>
/// Every reference carried here is <strong>operational data and never personal data</strong>: a phase code, a
/// defect code, an evidence key, a case reference. Security rule 7 keeps customer names, measurements and contact
/// details out of anything that reaches a screen's reason line, a log or a trace.
/// </para>
/// </remarks>
public sealed record ReadyGateInputs
{
    /// <summary>
    /// The longest reference the column holds. Matches <see cref="ReadyGateBlock.MaximumReferenceLength"/>,
    /// because every reference here is carried straight through onto a block.
    /// </summary>
    public const int MaximumReferenceLength = 200;

    private ReadyGateInputs(
        bool workflowComplete,
        string? incompletePhaseCode,
        bool qcPassed,
        bool reworkOpen,
        string? failedQcReference,
        bool documentationComplete,
        string? missingEvidenceReference,
        bool partialDeliveryPermitted,
        bool custodyGateEnabled,
        CustodyReconciliation custody,
        string? openCustodyCaseReference)
    {
        WorkflowComplete = workflowComplete;
        IncompletePhaseCode = incompletePhaseCode;
        QcPassed = qcPassed;
        ReworkOpen = reworkOpen;
        FailedQcReference = failedQcReference;
        DocumentationComplete = documentationComplete;
        MissingEvidenceReference = missingEvidenceReference;
        PartialDeliveryPermitted = partialDeliveryPermitted;
        CustodyGateEnabled = custodyGateEnabled;
        Custody = custody;
        OpenCustodyCaseReference = openCustodyCaseReference;
    }

    /// <summary>Every non-skippable phase of the pinned workflow version is complete (issue #33).</summary>
    public bool WorkflowComplete { get; }

    /// <summary>The phase the queue screen names when it is not.</summary>
    public string? IncompletePhaseCode { get; }

    /// <summary>The latest QC result is a pass (issue #34).</summary>
    public bool QcPassed { get; }

    /// <summary>
    /// A rework task is open. A pass with an open rework does not satisfy the predicate: INV-JOB-06 keeps the
    /// gate closed while any rework stands, and a rework never inherits the previous pass.
    /// </summary>
    public bool ReworkOpen { get; }

    /// <summary>The failed criteria or defect codes the screen names.</summary>
    public string? FailedQcReference { get; }

    /// <summary>Every piece of evidence the workflow or the checklist requires is present and ready.</summary>
    public bool DocumentationComplete { get; }

    /// <summary>The missing evidence the screen names.</summary>
    public string? MissingEvidenceReference { get; }

    /// <summary>
    /// The branch policy permits a <c>deliver_together</c> sibling to go on its own (issue #48). Where it does,
    /// the <c>DependenciesMet</c> predicate is waived rather than merely softened.
    /// </summary>
    public bool PartialDeliveryPermitted { get; }

    /// <summary>
    /// Whether the custody predicate is enforced at all. When it is,
    /// <see cref="CustodyReconciliation.Unknown"/> blocks — the gate fails closed.
    /// </summary>
    public bool CustodyGateEnabled { get; }

    /// <summary>What Custody said about the custodian, as the gate reads it.</summary>
    public CustodyReconciliation Custody { get; }

    /// <summary>The open reconciliation case the screen names.</summary>
    public string? OpenCustodyCaseReference { get; }

    /// <summary>
    /// Validates the facts gathered for one evaluation of the gate.
    /// </summary>
    /// <remarks>
    /// Returns the first failure found. A missing reference is not a failure: a predicate that passes has nothing
    /// to name, and a predicate that blocks without a reference still blocks — the screen simply shows the reason
    /// code alone rather than refusing to render.
    /// </remarks>
    /// <param name="workflowComplete">Every non-skippable phase of the pinned version is complete.</param>
    /// <param name="incompletePhaseCode">The phase to name when it is not.</param>
    /// <param name="qcPassed">The latest QC result is a pass.</param>
    /// <param name="reworkOpen">A rework task is open.</param>
    /// <param name="failedQcReference">The failed criteria or defect codes to name.</param>
    /// <param name="documentationComplete">Required evidence is present and ready.</param>
    /// <param name="missingEvidenceReference">The missing evidence to name.</param>
    /// <param name="partialDeliveryPermitted">The branch policy permits partial delivery.</param>
    /// <param name="custodyGateEnabled">Whether the custody predicate is enforced.</param>
    /// <param name="custody">What Custody said about the custodian.</param>
    /// <param name="openCustodyCaseReference">The open reconciliation case to name.</param>
    /// <returns>The inputs, or the first failure found.</returns>
    public static Result<ReadyGateInputs> Create(
        bool workflowComplete,
        string? incompletePhaseCode,
        bool qcPassed,
        bool reworkOpen,
        string? failedQcReference,
        bool documentationComplete,
        string? missingEvidenceReference,
        bool partialDeliveryPermitted,
        bool custodyGateEnabled,
        CustodyReconciliation custody,
        string? openCustodyCaseReference)
    {
        var phase = Reference(incompletePhaseCode, "incompletePhaseCode");
        if (phase.IsFailure)
        {
            return Result.Failure<ReadyGateInputs>(phase.Error);
        }

        var qc = Reference(failedQcReference, "failedQcReference");
        if (qc.IsFailure)
        {
            return Result.Failure<ReadyGateInputs>(qc.Error);
        }

        var evidence = Reference(missingEvidenceReference, "missingEvidenceReference");
        if (evidence.IsFailure)
        {
            return Result.Failure<ReadyGateInputs>(evidence.Error);
        }

        var custodyCase = Reference(openCustodyCaseReference, "openCustodyCaseReference");
        if (custodyCase.IsFailure)
        {
            return Result.Failure<ReadyGateInputs>(custodyCase.Error);
        }

        return Result.Success(new ReadyGateInputs(
            workflowComplete,
            phase.Value,
            qcPassed,
            reworkOpen,
            qc.Value,
            documentationComplete,
            evidence.Value,
            partialDeliveryPermitted,
            custodyGateEnabled,
            custody,
            custodyCase.Value));
    }

    /// <summary>Trims a reference to null and checks it against the column that holds it.</summary>
    private static Result<string?> Reference(string? value, string field)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > MaximumReferenceLength)
        {
            return Result.Failure<string?>(OrdersErrors.TooLong(field, MaximumReferenceLength));
        }

        return Result.Success<string?>(trimmed);
    }
}
