namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// What Custody's <c>ICustodyStateQuery</c> said about a garment's custodian, as the ready gate reads it.
/// </summary>
/// <remarks>
/// <para>
/// Three values, and the third is the whole point of the type. <c>docs/prd/state-transitions.md</c> section 9.1
/// says the <c>CustodyReconciled</c> predicate treats custody as blocked while custody state is
/// <em>unknown</em> and the custody gate is enabled — <strong>the gate fails closed</strong>. A boolean would have
/// had to fold "no answer" into one of the two answers, and folding it into "reconciled" is how a garment in
/// somebody else's hands reaches the delivery queue.
/// </para>
/// <para>
/// An Orders-local vocabulary rather than a copy of a Custody type, because ARCH-004 lets only a module's
/// <c>Contracts</c> project cross a boundary and a Domain project may reference nothing but
/// <c>Tailor360.Platform.Abstractions</c> (ARCH-001). The application translates the contract's answer into this.
/// </para>
/// </remarks>
public enum CustodyReconciliation
{
    /// <summary>A consistent custodian with no open reconciliation case.</summary>
    Reconciled = 0,

    /// <summary>A reconciliation case is open, or the custodian contradicts the recorded chain.</summary>
    NotReconciled = 1,

    /// <summary>
    /// Custody could not be established — the contract was unreachable, or the chain is silent. Treated as blocked
    /// while the custody gate is enabled, because the gate fails closed (state-transitions.md section 9.1).
    /// </summary>
    Unknown = 2,
}
