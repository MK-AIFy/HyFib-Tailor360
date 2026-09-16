namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>How much a phase-graph finding matters.</summary>
public enum WorkflowFindingSeverity
{
    /// <summary>Worth saying, and not worth refusing publication for.</summary>
    Warning = 0,

    /// <summary>Publication is refused while this stands.</summary>
    Error = 1,
}

/// <summary>
/// One thing <see cref="WorkflowGraph"/> found wrong with a version's phases and transitions.
/// </summary>
/// <remarks>
/// Mirrors <c>Catalog.Contracts.Catalogue.CatalogFinding</c>'s shape rather than referencing it: this type
/// answers a question entirely within the <c>orders</c> schema — whether a version's own phase graph is
/// internally consistent — and needs no cross-module call, so it is a plain <c>Domain</c> type rather than a
/// published contract (unlike Catalog's dependency validation, which genuinely asks other modules). Giving it
/// the same shape means a screen that renders one renders the other identically, without the two ever being
/// the same type across a boundary ARCH-004 forbids crossing.
/// </remarks>
/// <param name="Severity">Whether it stops publication.</param>
/// <param name="Code">
/// A stable dotted code naming which of <see cref="WorkflowGraph"/>'s six checks fired, for example
/// <c>orders.workflow-graph-unreachable-phase</c>. One code per check, not per occurrence — a check that finds
/// several phases wrong reports the same code once per phase, so the code alone answers "which rule".
/// </param>
/// <param name="Message">What is wrong, in an administrator's words. Never a phase's private data — there is none; a phase is configuration.</param>
/// <param name="Target">
/// The phase code the finding is about, or null when the finding is about the graph as a whole (there is no
/// start phase, for instance, which is not a defect of any one phase).
/// </param>
public sealed record WorkflowFinding(
    WorkflowFindingSeverity Severity,
    string Code,
    string Message,
    string? Target = null)
{
    /// <summary>A finding that stops publication.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">What is wrong.</param>
    /// <param name="target">The phase code it is about, or null for the graph as a whole.</param>
    /// <returns>The finding.</returns>
    public static WorkflowFinding Error(string code, string message, string? target = null)
        => new(WorkflowFindingSeverity.Error, code, message, target);

    /// <summary>A finding worth saying that does not stop publication.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">What is worth saying.</param>
    /// <param name="target">The phase code it is about, or null for the graph as a whole.</param>
    /// <returns>The finding.</returns>
    public static WorkflowFinding Warning(string code, string message, string? target = null)
        => new(WorkflowFindingSeverity.Warning, code, message, target);
}
