namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The six predicates of the ready-for-delivery gate.
/// </summary>
/// <remarks>
/// Named exactly as <c>docs/prd/state-transitions.md</c> section 9.1 names them, and in that order. The member
/// <em>is</em> the reason code: INV-JOB-07 requires that each predicate return its own reason rather than a single
/// "not ready", so the queue screen can tell a Tailor Master which of six things to go and do. A renamed member is
/// therefore a change to a published reason code, not a refactor.
/// </remarks>
public enum ReadyGatePredicate
{
    /// <summary>Every non-skippable phase of the pinned workflow version is complete.</summary>
    WorkflowComplete = 0,

    /// <summary>The latest QC result is a pass and no rework task is open (INV-JOB-06).</summary>
    QcPassed = 1,

    /// <summary>Every piece of evidence the workflow or the checklist requires is present and ready.</summary>
    DocumentationComplete = 2,

    /// <summary>No holds row is open on the job.</summary>
    NoOpenHold = 3,

    /// <summary>
    /// <c>deliver_together</c> siblings are ready, unless the branch policy permits partial delivery (INV-JOB-09,
    /// issue #48).
    /// </summary>
    DependenciesMet = 4,

    /// <summary>
    /// <c>ICustodyStateQuery</c> reports a consistent custodian with no open case.
    /// <see cref="CustodyReconciliation.Unknown"/> counts as blocked.
    /// </summary>
    CustodyReconciled = 5,
}
