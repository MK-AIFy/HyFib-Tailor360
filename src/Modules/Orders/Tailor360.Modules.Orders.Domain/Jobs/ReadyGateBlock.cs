namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// One failing ready-gate predicate, and the thing it names.
/// </summary>
/// <remarks>
/// <para>
/// This is what the delivery-queue screen shows instead of a bare refusal. INV-JOB-07 requires each predicate to
/// return its own reason code; a reason code on its own would still leave the reader asking <em>which</em> phase,
/// <em>which</em> sibling, <em>which</em> hold — so the reference travels with it.
/// </para>
/// <para>
/// A positional record on purpose, unlike the validated value objects of this module: it carries no rule of its
/// own and is only ever produced by <see cref="ReadyGate"/>, which trims and length-checks the references it was
/// handed through <see cref="ReadyGateInputs"/>.
/// </para>
/// </remarks>
/// <param name="Predicate">Which predicate failed. The member name is the reason code.</param>
/// <param name="Reference">
/// What it names, where it names something: a phase code, a defect code, an evidence key, a hold reason code, a
/// sibling job number or a reconciliation case reference. <strong>Never personal data</strong> — security rule 7
/// keeps customer names, measurements and contact details out of anything a screen, a log or a trace can carry.
/// </param>
public sealed record ReadyGateBlock(ReadyGatePredicate Predicate, string? Reference)
{
    /// <summary>
    /// The longest reference the column holds. Matches <see cref="ReadyGateInputs.MaximumReferenceLength"/>.
    /// </summary>
    public const int MaximumReferenceLength = 200;
}
