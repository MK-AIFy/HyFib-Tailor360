using Tailor360.Modules.Orders.Application.Workflows;

namespace Tailor360.Modules.Orders.Api.Payloads;

/// <summary>Creates a workflow definition.</summary>
/// <param name="Code">The definition's machine key, for example <c>STITCH_STANDARD</c>.</param>
/// <param name="Name">What an administrator calls it.</param>
/// <param name="Description">Longer notes, or null.</param>
public sealed record CreateWorkflowDefinitionRequest(string? Code, string? Name, string? Description);

/// <summary>Starts a new draft version of a definition.</summary>
/// <param name="CloneFromVersionId">
/// A version of the same definition to copy the graph from, or null to start empty.
/// </param>
public sealed record CreateWorkflowVersionRequest(Guid? CloneFromVersionId);

/// <summary>
/// The whole content of a draft version's graph — its phases, its transitions and its category mapping,
/// replaced together because a phase list and a transition matrix are only valid as a set.
/// </summary>
/// <param name="Phases">Every phase the version should now hold.</param>
/// <param name="Transitions">Every transition the version should now hold.</param>
/// <param name="CategoryKeys">Every catalogue category key that should now use this version.</param>
public sealed record ReplaceWorkflowVersionGraphRequest(
    IReadOnlyList<WorkflowPhaseRequest>? Phases,
    IReadOnlyList<WorkflowTransitionRequest>? Transitions,
    IReadOnlyList<string>? CategoryKeys)
{
    /// <summary>The phases, as the Application layer's unvalidated input shape.</summary>
    /// <returns>The inputs, in the order submitted.</returns>
    public IReadOnlyList<WorkflowPhaseInput> ToPhaseInputs()
        => [.. (Phases ?? []).Select(phase => phase.ToInput())];

    /// <summary>The transitions, as the Application layer's unvalidated input shape.</summary>
    /// <returns>The inputs, in the order submitted.</returns>
    public IReadOnlyList<WorkflowTransitionInput> ToTransitionInputs()
        => [.. (Transitions ?? []).Select(transition => transition.ToInput())];
}

/// <summary>One phase of a draft version's graph, as an administrator submitted it.</summary>
/// <param name="Code">The phase's identity as a concept.</param>
/// <param name="DisplayName">What it is called.</param>
/// <param name="Ordinal">Display and processing order, from zero.</param>
/// <param name="RequiredRoleKeys">Which role keys may work it.</param>
/// <param name="RequiresEvidence">Whether completion demands recorded evidence.</param>
/// <param name="ExpectedDuration">How long it is expected to take, or null.</param>
/// <param name="Sla">The service-level window before it is overdue, or null.</param>
/// <param name="IsOptional">Whether a job may skip past it without entering it.</param>
/// <param name="IsSkippable">Whether an entered instance may be skipped without completing it.</param>
/// <param name="IsTerminal">Whether a job may finish here without a further transition.</param>
public sealed record WorkflowPhaseRequest(
    string? Code,
    string? DisplayName,
    int Ordinal,
    IReadOnlyList<string>? RequiredRoleKeys,
    bool RequiresEvidence,
    TimeSpan? ExpectedDuration,
    TimeSpan? Sla,
    bool IsOptional,
    bool IsSkippable,
    bool IsTerminal)
{
    /// <summary>The domain-facing input this request describes, unvalidated.</summary>
    /// <returns>The input.</returns>
    public WorkflowPhaseInput ToInput()
        => new(
            Code,
            DisplayName,
            Ordinal,
            RequiredRoleKeys,
            RequiresEvidence,
            ExpectedDuration,
            Sla,
            IsOptional,
            IsSkippable,
            IsTerminal);
}

/// <summary>One transition of a draft version's graph, as an administrator submitted it.</summary>
/// <param name="FromPhaseCode">The phase work leaves.</param>
/// <param name="ToPhaseCode">The phase work enters.</param>
public sealed record WorkflowTransitionRequest(string? FromPhaseCode, string? ToPhaseCode)
{
    /// <summary>The domain-facing input this request describes, unvalidated.</summary>
    /// <returns>The input.</returns>
    public WorkflowTransitionInput ToInput() => new(FromPhaseCode, ToPhaseCode);
}
