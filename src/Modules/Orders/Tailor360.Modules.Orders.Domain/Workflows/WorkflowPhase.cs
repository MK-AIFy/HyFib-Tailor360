namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// One phase of a workflow version's own row, as the schema keeps it: its own table, its own <c>xmin</c>.
/// </summary>
/// <remarks>
/// <para>
/// A row rather than a JSON entry, unlike the transitions and the category mapping it sits beside on the same
/// version — because a phase carries enough fields (<see cref="WorkflowPhaseContent"/>'s own remarks) that a
/// future screen editing one phase at a time needs it addressable by its own identity, and because
/// <c>src/Modules/CLAUDE.md</c> section 5's baseline columns (<c>id</c>, <c>organisation_id</c>,
/// <c>created_at/by</c>, <c>updated_at/by</c>, <c>xmin</c>) are what this issue's own scope asks every one of
/// its three new tables to carry.
/// </para>
/// <para>
/// <strong>Replaced wholesale, never edited in place.</strong> <see cref="WorkflowDefinition.ReplacePhases"/>
/// removes every existing phase of a version and adds the new set, rather than diffing one against the other —
/// the same choice <c>OrderDraftGarmentContent</c>'s remarks explain for a garment section: a phase's identity
/// is its <see cref="Code"/>, carried into <see cref="PhaseTransition"/>, and a replace-in-place edit would have
/// to decide whether a phase with a changed code is a rename or a different phase. Nothing in this slice needs
/// that decision made, so it is not made.
/// </para>
/// </remarks>
public sealed class WorkflowPhase
{
    private readonly List<string> _requiredRoleKeys = [];

    private WorkflowPhase()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private WorkflowPhase(
        Guid id,
        Guid workflowVersionId,
        Guid organisationId,
        WorkflowPhaseContent content,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        WorkflowVersionId = workflowVersionId;
        OrganisationId = organisationId;
        Code = content.Code;
        DisplayName = content.DisplayName;
        Ordinal = content.Ordinal;
        _requiredRoleKeys.AddRange(content.RequiredRoleKeys);
        RequiresEvidence = content.RequiresEvidence;
        ExpectedDuration = content.ExpectedDuration;
        Sla = content.Sla;
        IsOptional = content.IsOptional;
        IsSkippable = content.IsSkippable;
        IsTerminal = content.IsTerminal;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the row.</summary>
    public Guid Id { get; private set; }

    /// <summary>The version this phase belongs to.</summary>
    public Guid WorkflowVersionId { get; private set; }

    /// <summary>The organisation. Denormalised from the definition, per this issue's own scope.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The phase's identity as a concept, carried across versions and named by a transition.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What an administrator, and eventually a tailor's screen, calls it.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Display and processing order within the version, from zero.</summary>
    public int Ordinal { get; private set; }

    /// <summary>Which role keys may work this phase.</summary>
    public IReadOnlyList<string> RequiredRoleKeys => _requiredRoleKeys;

    /// <summary>Whether completing this phase demands recorded evidence.</summary>
    public bool RequiresEvidence { get; private set; }

    /// <summary>How long the phase is expected to take, or null when no figure is configured.</summary>
    public TimeSpan? ExpectedDuration { get; private set; }

    /// <summary>The service-level window before the phase is overdue, or null when none is configured.</summary>
    public TimeSpan? Sla { get; private set; }

    /// <summary>Whether a job may skip past this phase without entering it.</summary>
    public bool IsOptional { get; private set; }

    /// <summary>Whether an already-entered phase may be skipped without completing it.</summary>
    public bool IsSkippable { get; private set; }

    /// <summary>Whether a job may finish here without a further transition.</summary>
    public bool IsTerminal { get; private set; }

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When the row was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Builds one phase row from validated content.</summary>
    /// <param name="id">The new row's identity, from <c>IIdGenerator</c>.</param>
    /// <param name="workflowVersionId">The version it belongs to.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="content">The validated content.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor replacing the version's phases.</param>
    /// <returns>The phase.</returns>
    internal static WorkflowPhase Add(
        Guid id,
        Guid workflowVersionId,
        Guid organisationId,
        WorkflowPhaseContent content,
        DateTimeOffset now,
        Guid? by)
        => new(id, workflowVersionId, organisationId, content, now, by);
}
