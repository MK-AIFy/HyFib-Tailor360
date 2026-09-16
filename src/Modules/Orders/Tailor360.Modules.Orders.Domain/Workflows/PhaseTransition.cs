using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// One directed edge of a version's phase graph: work may move from one phase to another.
/// </summary>
/// <remarks>
/// <para>
/// A pair of phase codes and nothing else. What happens when a transition is taken — who may take it, what
/// evidence it demands — is <strong>E06-F02-5</strong>'s ("transitions at run time"), out of scope here; this
/// slice models only that the edge exists, because <see cref="WorkflowGraph"/>'s six checks are entirely about
/// the edges' shape and need nothing more.
/// </para>
/// <para>
/// Referenced by phase <em>code</em>, not by a phase's row identity. The transitions live in
/// <c>workflow_versions.transitions</c> as a JSON document (<c>OrdersJson</c>) while the phases they name are
/// separate rows in <c>workflow_version_phases</c>, and a code is what survives a phase being replaced wholesale
/// by <see cref="WorkflowDefinition.ReplacePhases"/> — a fresh set of phase rows, same codes, keeps every
/// transition naming something real without this type carrying a foreign key across two independently
/// replaceable collections.
/// </para>
/// </remarks>
/// <param name="FromPhaseCode">The phase work leaves.</param>
/// <param name="ToPhaseCode">The phase work enters.</param>
public sealed record PhaseTransition(string FromPhaseCode, string ToPhaseCode)
{
    /// <summary>Validates and builds a transition.</summary>
    /// <remarks>
    /// Checks only that both codes are well formed. Whether they name phases that are actually on this version
    /// — and whether the resulting graph makes sense at all — is <see cref="WorkflowGraph"/>'s question, asked
    /// once the whole set of transitions and the whole set of phases are both known; a single edge validated in
    /// isolation cannot answer either.
    /// </remarks>
    /// <param name="fromPhaseCode">The phase work leaves.</param>
    /// <param name="toPhaseCode">The phase work enters.</param>
    /// <returns>The transition, or the reason it could not be built.</returns>
    public static Result<PhaseTransition> Create(string? fromPhaseCode, string? toPhaseCode)
    {
        if (!WorkflowCode.IsWellFormed(fromPhaseCode))
        {
            return Result.Failure<PhaseTransition>(OrdersErrors.WorkflowCodeNotWellFormed("fromPhaseCode"));
        }

        if (!WorkflowCode.IsWellFormed(toPhaseCode))
        {
            return Result.Failure<PhaseTransition>(OrdersErrors.WorkflowCodeNotWellFormed("toPhaseCode"));
        }

        return Result.Success(new PhaseTransition(fromPhaseCode!, toPhaseCode!));
    }
}
