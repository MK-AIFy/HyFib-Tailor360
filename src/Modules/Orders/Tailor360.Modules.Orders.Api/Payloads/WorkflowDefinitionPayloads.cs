using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Orders.Api.Payloads;

/// <summary>A workflow definition and every version it has, as the administration surface reads it.</summary>
/// <remarks>
/// Carries no tag of its own: this slice adds no route that edits a definition's own fields (only
/// <c>WorkflowDefinition.SetActive</c>, out of scope here), so there is nothing yet for a definition-level
/// precondition to guard. Each version's own tag is on <see cref="WorkflowVersionSummaryPayload"/>
/// instead, the same split <c>OrderDraftPayload</c> and <c>OrderDraftGarmentPayload</c> use for the same
/// reason: editing happens at the version, not the definition.
/// </remarks>
/// <param name="Id">Identity of the definition.</param>
/// <param name="Code">The definition's machine key, for example <c>STITCH_STANDARD</c>.</param>
/// <param name="Name">What an administrator calls it.</param>
/// <param name="Description">Longer notes, or null.</param>
/// <param name="IsActive">Whether the definition may be chosen for a new catalogue link.</param>
/// <param name="Versions">Every version, draft, published or retired, newest first.</param>
public sealed record WorkflowDefinitionPayload(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<WorkflowVersionSummaryPayload> Versions)
{
    /// <summary>Projects a definition.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="tagOf">Each version's own current entity tag.</param>
    /// <returns>The payload.</returns>
    public static WorkflowDefinitionPayload From(WorkflowDefinition definition, Func<WorkflowVersion, EntityTag> tagOf)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(tagOf);

        return new WorkflowDefinitionPayload(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.Description,
            definition.IsActive,
            [.. definition.Versions
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => WorkflowVersionSummaryPayload.From(version, tagOf(version)))]);
    }
}

/// <summary>One version of a definition, without its graph — what a definition's own list carries.</summary>
/// <remarks>
/// <see cref="Tag"/> is this version's own row version, unquoted here and quoted on the wire as a header
/// — <c>OrderDraftGarmentPayload.Version</c>'s own convention, named differently only to stay clear of
/// <see cref="VersionNumber"/> beside it. It is what a screen sends back as <c>If-Match</c> to edit this
/// version directly from a list, with no second read.
/// </remarks>
/// <param name="Id">Identity of the version. What a job pins forever once it starts production against it.</param>
/// <param name="VersionNumber">The version's number within its definition, counting from one.</param>
/// <param name="Status"><c>Draft</c>, <c>Published</c> or <c>Retired</c>.</param>
/// <param name="Tag">This version's own concurrency token.</param>
/// <param name="PublishedAt">When it was published, or null while it is a draft.</param>
/// <param name="RetiredAt">When it was retired, or null while it is not.</param>
public sealed record WorkflowVersionSummaryPayload(
    Guid Id,
    int VersionNumber,
    string Status,
    string Tag,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt)
{
    /// <summary>Projects a version's summary.</summary>
    /// <param name="version">The version.</param>
    /// <param name="tag">Its current entity tag.</param>
    /// <returns>The payload.</returns>
    public static WorkflowVersionSummaryPayload From(WorkflowVersion version, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new WorkflowVersionSummaryPayload(
            version.Id,
            version.VersionNumber,
            version.Status.ToString(),
            tag.Version,
            version.PublishedAt,
            version.RetiredAt);
    }
}

/// <summary>One version of a definition, with its whole graph.</summary>
/// <param name="Id">Identity of the version.</param>
/// <param name="WorkflowDefinitionId">The definition this is a version of.</param>
/// <param name="VersionNumber">The version's number within its definition, counting from one.</param>
/// <param name="Status"><c>Draft</c>, <c>Published</c> or <c>Retired</c>.</param>
/// <param name="Tag">Its own <c>xmin</c>, as the <c>ETag</c> an edit is made against — distinct from the definition's.</param>
/// <param name="Phases">The phases, ordered for display and processing.</param>
/// <param name="Transitions">The transitions between phases.</param>
/// <param name="CategoryKeys">Which catalogue categories use this version.</param>
/// <param name="PublishedAt">When it was published, or null while it is a draft.</param>
/// <param name="PublishReason">Why it was published, or null.</param>
/// <param name="RetiredAt">When it was retired, or null while it is not.</param>
/// <param name="RetiredReason">Why it was retired, or null.</param>
/// <param name="CreatedAt">When the row was created, in UTC.</param>
/// <param name="UpdatedAt">When it was last changed, in UTC.</param>
public sealed record WorkflowVersionPayload(
    Guid Id,
    Guid WorkflowDefinitionId,
    int VersionNumber,
    string Status,
    string Tag,
    IReadOnlyList<WorkflowPhasePayload> Phases,
    IReadOnlyList<WorkflowTransitionPayload> Transitions,
    IReadOnlyList<string> CategoryKeys,
    DateTimeOffset? PublishedAt,
    string? PublishReason,
    DateTimeOffset? RetiredAt,
    string? RetiredReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a version with its whole graph.</summary>
    /// <param name="version">The version.</param>
    /// <param name="tag">Its current entity tag.</param>
    /// <returns>The payload.</returns>
    public static WorkflowVersionPayload From(WorkflowVersion version, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new WorkflowVersionPayload(
            version.Id,
            version.WorkflowDefinitionId,
            version.VersionNumber,
            version.Status.ToString(),
            tag.Version,
            [.. version.Phases.OrderBy(phase => phase.Ordinal).Select(WorkflowPhasePayload.From)],
            [.. version.Transitions.Select(WorkflowTransitionPayload.From)],
            [.. version.CategoryKeys],
            version.PublishedAt,
            version.PublishReason,
            version.RetiredAt,
            version.RetiredReason,
            version.CreatedAt,
            version.UpdatedAt);
    }
}

/// <summary>One phase of a version's graph.</summary>
/// <param name="Code">The phase's identity as a concept, carried across versions and named by a transition.</param>
/// <param name="DisplayName">What an administrator, and eventually a tailor's screen, calls it.</param>
/// <param name="Ordinal">Display and processing order within the version, from zero.</param>
/// <param name="RequiredRoleKeys">Which role keys may work this phase.</param>
/// <param name="RequiresEvidence">Whether completing this phase demands recorded evidence.</param>
/// <param name="ExpectedDuration">How long the phase is expected to take, or null.</param>
/// <param name="Sla">The service-level window before the phase is overdue, or null.</param>
/// <param name="IsOptional">Whether a job may skip past this phase without entering it.</param>
/// <param name="IsSkippable">Whether an already-entered phase may be skipped without completing it.</param>
/// <param name="IsTerminal">Whether a job may finish here without a further transition.</param>
public sealed record WorkflowPhasePayload(
    string Code,
    string DisplayName,
    int Ordinal,
    IReadOnlyList<string> RequiredRoleKeys,
    bool RequiresEvidence,
    TimeSpan? ExpectedDuration,
    TimeSpan? Sla,
    bool IsOptional,
    bool IsSkippable,
    bool IsTerminal)
{
    /// <summary>Projects one phase.</summary>
    /// <param name="phase">The phase.</param>
    /// <returns>The payload.</returns>
    public static WorkflowPhasePayload From(WorkflowPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        return new WorkflowPhasePayload(
            phase.Code,
            phase.DisplayName,
            phase.Ordinal,
            phase.RequiredRoleKeys,
            phase.RequiresEvidence,
            phase.ExpectedDuration,
            phase.Sla,
            phase.IsOptional,
            phase.IsSkippable,
            phase.IsTerminal);
    }
}

/// <summary>One directed edge of a version's phase graph.</summary>
/// <param name="FromPhaseCode">The phase work leaves.</param>
/// <param name="ToPhaseCode">The phase work enters.</param>
public sealed record WorkflowTransitionPayload(string FromPhaseCode, string ToPhaseCode)
{
    /// <summary>Projects one transition.</summary>
    /// <param name="transition">The transition.</param>
    /// <returns>The payload.</returns>
    public static WorkflowTransitionPayload From(PhaseTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return new WorkflowTransitionPayload(transition.FromPhaseCode, transition.ToPhaseCode);
    }
}

/// <summary>One thing <c>WorkflowGraph</c> found wrong with a version's phases and transitions.</summary>
/// <param name="Severity"><c>Error</c> or <c>Warning</c>. An error stops the save; a warning does not.</param>
/// <param name="Code">The stable dotted code naming which of the six checks fired.</param>
/// <param name="Message">What is wrong, in an administrator's words.</param>
/// <param name="Target">The phase code the finding is about, or null when it is about the graph as a whole.</param>
public sealed record WorkflowFindingPayload(string Severity, string Code, string Message, string? Target)
{
    /// <summary>Projects one finding.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The payload.</returns>
    public static WorkflowFindingPayload From(WorkflowFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new WorkflowFindingPayload(
            finding.Severity.ToString(), finding.Code, finding.Message, finding.Target);
    }
}
