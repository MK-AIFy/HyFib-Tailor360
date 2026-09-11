namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The two relationships one garment job of an order may declare on another.
/// </summary>
/// <remarks>
/// <para>
/// INV-JOB-09 names both and, importantly, enforces them in <strong>different places</strong>. Keeping the two in
/// one enumeration while the checks stay apart is deliberate: a reader who sees a dependency row needs to know
/// which gate will act on it, and a single "blocks the job" reading would be wrong for either value.
/// </para>
/// <para>
/// Neither value is a scheduling instruction. The promised date is a separate, reasoned decision
/// (<c>docs/prd/state-transitions.md</c> section 3.2, reschedule), and a dependency never moves one.
/// </para>
/// </remarks>
public enum JobDependencyKind
{
    /// <summary>
    /// The prerequisite must finish before the dependent job's first phase may start.
    /// </summary>
    /// <remarks>
    /// Checked at start of production, against the satisfied-prerequisite set the application established; the
    /// phase-level detail arrives with the workflow engine in issue #33.
    /// </remarks>
    FinishBefore = 0,

    /// <summary>
    /// The two garments go to the customer together.
    /// </summary>
    /// <remarks>
    /// Checked by the ready gate's <c>DependenciesMet</c> predicate and by the delivery queue, and waived when the
    /// branch policy permits partial delivery (issue #48). It never blocks production of either garment.
    /// </remarks>
    DeliverTogether = 1,
}
