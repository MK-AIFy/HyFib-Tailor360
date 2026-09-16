using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// Everything one phase of a workflow version holds, validated once.
/// </summary>
/// <remarks>
/// A record rather than a long parameter list on <see cref="WorkflowVersion.ReplacePhases"/>, for the reason
/// <c>OrderDraftGarmentContent</c> gives for the same shape: adding a field later should not change every call
/// site, and validation has one home. Unlike <c>OrderDraftGarmentContent</c>, nothing here is pinned server-side
/// against another module — a phase is configuration this module owns outright — so every field is checked here
/// and nowhere else.
/// </remarks>
public sealed record WorkflowPhaseContent
{
    /// <summary>The longest phase code the column holds. Matches <see cref="WorkflowCode.MaximumLength"/>.</summary>
    public const int MaximumCodeLength = WorkflowCode.MaximumLength;

    /// <summary>The longest display name the column holds.</summary>
    public const int MaximumDisplayNameLength = 120;

    /// <summary>The longest role key the column holds.</summary>
    public const int MaximumRoleKeyLength = 40;

    private WorkflowPhaseContent(
        string code,
        string displayName,
        int ordinal,
        IReadOnlyList<string> requiredRoleKeys,
        bool requiresEvidence,
        TimeSpan? expectedDuration,
        TimeSpan? sla,
        bool isOptional,
        bool isSkippable,
        bool isTerminal)
    {
        Code = code;
        DisplayName = displayName;
        Ordinal = ordinal;
        RequiredRoleKeys = requiredRoleKeys;
        RequiresEvidence = requiresEvidence;
        ExpectedDuration = expectedDuration;
        Sla = sla;
        IsOptional = isOptional;
        IsSkippable = isSkippable;
        IsTerminal = isTerminal;
    }

    /// <summary>The phase's identity as a concept, carried across versions of the same definition.</summary>
    public string Code { get; }

    /// <summary>What an administrator, and eventually a tailor's screen, calls it.</summary>
    public string DisplayName { get; }

    /// <summary>Display and processing order within the version, from zero.</summary>
    public int Ordinal { get; }

    /// <summary>Which role keys may work this phase. Never empty — <see cref="WorkflowGraph"/> refuses that.</summary>
    public IReadOnlyList<string> RequiredRoleKeys { get; }

    /// <summary>Whether completing this phase demands recorded evidence.</summary>
    public bool RequiresEvidence { get; }

    /// <summary>How long the phase is expected to take, or null when no figure is configured.</summary>
    public TimeSpan? ExpectedDuration { get; }

    /// <summary>The service-level window before the phase is overdue, or null when none is configured.</summary>
    public TimeSpan? Sla { get; }

    /// <summary>Whether a job may skip past this phase without entering it.</summary>
    public bool IsOptional { get; }

    /// <summary>Whether an already-entered phase may be skipped without completing it.</summary>
    public bool IsSkippable { get; }

    /// <summary>Whether a job may finish here without a further transition.</summary>
    public bool IsTerminal { get; }

    /// <summary>Validates and builds one phase's content.</summary>
    /// <param name="code">The phase's identity as a concept.</param>
    /// <param name="displayName">What it is called.</param>
    /// <param name="ordinal">Display and processing order, from zero.</param>
    /// <param name="requiredRoleKeys">
    /// Which role keys may work it. An empty set is accepted here — it is <see cref="WorkflowGraph"/>'s own
    /// check, asked at publication, not this constructor's; refusing it here would make that check unreachable.
    /// </param>
    /// <param name="requiresEvidence">Whether completion demands recorded evidence.</param>
    /// <param name="expectedDuration">How long it is expected to take, or null.</param>
    /// <param name="sla">The service-level window before it is overdue, or null.</param>
    /// <param name="isOptional">Whether a job may skip past it without entering it.</param>
    /// <param name="isSkippable">Whether an entered instance may be skipped without completing it.</param>
    /// <param name="isTerminal">Whether a job may finish here without a further transition.</param>
    /// <returns>The content, or the reason it could not be built.</returns>
    public static Result<WorkflowPhaseContent> Create(
        string? code,
        string? displayName,
        int ordinal,
        IReadOnlyCollection<string>? requiredRoleKeys,
        bool requiresEvidence,
        TimeSpan? expectedDuration,
        TimeSpan? sla,
        bool isOptional,
        bool isSkippable,
        bool isTerminal)
    {
        if (!WorkflowCode.IsWellFormed(code))
        {
            return Result.Failure<WorkflowPhaseContent>(OrdersErrors.WorkflowCodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<WorkflowPhaseContent>(OrdersErrors.Required("displayName"));
        }

        if (displayName.Length > MaximumDisplayNameLength)
        {
            return Result.Failure<WorkflowPhaseContent>(
                OrdersErrors.TooLong("displayName", MaximumDisplayNameLength));
        }

        if (ordinal < 0)
        {
            return Result.Failure<WorkflowPhaseContent>(OrdersErrors.SequenceOutOfRange("ordinal"));
        }

        var roles = requiredRoleKeys?.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        foreach (var role in roles)
        {
            if (role.Length > MaximumRoleKeyLength)
            {
                return Result.Failure<WorkflowPhaseContent>(
                    OrdersErrors.TooLong("requiredRoleKeys", MaximumRoleKeyLength));
            }
        }

        if (expectedDuration is { } duration && duration <= TimeSpan.Zero)
        {
            return Result.Failure<WorkflowPhaseContent>(OrdersErrors.AmountNegative("expectedDuration"));
        }

        if (sla is { } window && window <= TimeSpan.Zero)
        {
            return Result.Failure<WorkflowPhaseContent>(OrdersErrors.AmountNegative("sla"));
        }

        return Result.Success(
            new WorkflowPhaseContent(
                code!,
                displayName.Trim(),
                ordinal,
                roles,
                requiresEvidence,
                expectedDuration,
                sla,
                isOptional,
                isSkippable,
                isTerminal));
    }
}
