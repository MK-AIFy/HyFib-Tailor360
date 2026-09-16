using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Orders.Application.Workflows;

/// <summary>
/// Drafts and edits workflow definitions and versions (E06-F02-2): listing and creating a definition,
/// reading one version, replacing a draft version's whole graph, and starting a new draft by cloning a
/// published one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The graph is checked whole, on every replace, not deferred to publication.</strong>
/// <see cref="WorkflowVersion.ReplacePhases"/> and <see cref="WorkflowVersion.ReplaceTransitions"/>
/// validate each phase and each edge on its own terms but not the shape of the graph they form together
/// — the aggregate's own remarks are explicit that a draft may briefly hold an inconsistent graph while
/// it is being built up one domain call at a time. <see cref="ReplaceGraphAsync"/> does not take
/// advantage of that: it replaces phases, transitions and the category mapping together from one
/// payload and then asks <see cref="WorkflowVersion.ValidateForPublication"/> the same six-check
/// question <see cref="WorkflowVersion.Publish"/> asks, refusing to save when it finds an error. That is
/// a choice this route makes, not a domain rule — the whole graph arrives in one request, so there is no
/// reason to let a caller save something instantly wrong when the very next reasonable thing to do is
/// tell them together, keyed by phase code so the editor can focus the offending row.
/// </para>
/// <para>
/// <strong>Cloning is built here, not on the aggregate.</strong> <see cref="WorkflowDefinition.AddVersion"/>
/// starts an empty draft; there is no clone primitive on <see cref="WorkflowVersion"/>; the shape of a
/// source version's phases is read back into fresh <see cref="WorkflowPhaseContent"/> values and replayed
/// through the same <see cref="WorkflowDefinition.ReplacePhases"/> a manual edit would use, and its
/// transitions and category keys — both plain values already — are reused directly.
/// </para>
/// </remarks>
/// <param name="store">The workflow definition store.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class WorkflowDefinitionHandler(
    IWorkflowDefinitionStore store,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A workflow definition was created.</summary>
    public const string DefinitionCreatedAction = "orders.workflow_definition_created";

    /// <summary>A draft workflow version was started, empty or cloned.</summary>
    public const string VersionDraftedAction = "orders.workflow_version_drafted";

    /// <summary>A draft workflow version's graph was replaced.</summary>
    public const string VersionEditedAction = "orders.workflow_version_edited";

    /// <summary>Lists every definition the organisation has, each with every version.</summary>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The definitions, newest first.</returns>
    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await store.ListAsync(organisationId, cancellationToken);

    /// <summary>Creates a new, empty workflow definition.</summary>
    /// <param name="command">What to call it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The definition, or the reason it could not be created.</returns>
    public async Task<Result<AdministeredWorkflowDefinition>> CreateDefinitionAsync(
        CreateWorkflowDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;
        var created = WorkflowDefinition.Create(
            ids.NewId(), command.OrganisationId, command.Code, command.Name, command.Description, now, command.By);

        if (created.IsFailure)
        {
            return Result.Failure<AdministeredWorkflowDefinition>(created.Error);
        }

        var definition = created.Value;
        store.Add(definition);

        var saved = await store.SaveAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredWorkflowDefinition>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit,
            DefinitionCreatedAction,
            OrdersAudit.WorkflowDefinitionEntity,
            definition.Id,
            $"Workflow definition '{definition.Code}' created.",
            cancellationToken);

        return Result.Success(new AdministeredWorkflowDefinition(definition, store.EntityTagOf(definition)));
    }

    /// <summary>Reads one version of one definition.</summary>
    /// <param name="workflowDefinitionId">The definition.</param>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be read.</returns>
    public async Task<Result<AdministeredWorkflowVersion>> ReadVersionAsync(
        Guid workflowDefinitionId,
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var found = await LoadVersionAsync(workflowDefinitionId, versionId, organisationId, cancellationToken);

        return found.IsFailure
            ? Result.Failure<AdministeredWorkflowVersion>(found.Error)
            : Result.Success(Administered(found.Value.Version));
    }

    /// <summary>Starts a new draft version, empty or cloned from an existing one of the same definition.</summary>
    /// <param name="command">Which definition, and what to start it from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<AdministeredWorkflowVersion>> CreateVersionAsync(
        CreateWorkflowVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var definition = await store.FindAsync(
            command.WorkflowDefinitionId, command.OrganisationId, cancellationToken);

        if (definition is null)
        {
            return Result.Failure<AdministeredWorkflowVersion>(OrdersErrors.WorkflowDefinitionNotFound);
        }

        var now = clock.UtcNow;
        var draft = definition.AddVersion(ids, now, command.By);

        if (command.CloneFromVersionId is { } sourceId)
        {
            var cloned = CloneInto(definition, draft, sourceId, ids, now, command.By);

            if (cloned.IsFailure)
            {
                return Result.Failure<AdministeredWorkflowVersion>(cloned.Error);
            }
        }

        var saved = await store.SaveAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredWorkflowVersion>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit,
            VersionDraftedAction,
            OrdersAudit.WorkflowVersionEntity,
            draft.Id,
            command.CloneFromVersionId is null
                ? $"Workflow version {draft.VersionNumber} of '{definition.Code}' started empty."
                : $"Workflow version {draft.VersionNumber} of '{definition.Code}' started as a copy of "
                  + "an existing version.",
            cancellationToken);

        return Result.Success(Administered(draft));
    }

    /// <summary>
    /// Replaces a draft version's whole graph — its phases, its transitions and its category mapping —
    /// from one payload.
    /// </summary>
    /// <param name="command">Which version, its expected tag, and the whole graph it should now hold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The saved version when the graph passed every check, the findings without a save when it did
    /// not, or the reason the replacement could not even be attempted.
    /// </returns>
    public async Task<Result<WorkflowGraphReplacement>> ReplaceGraphAsync(
        ReplaceWorkflowVersionGraphCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadVersionForChangeAsync(
            command.WorkflowDefinitionId,
            command.VersionId,
            command.OrganisationId,
            command.ExpectedVersion,
            cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<WorkflowGraphReplacement>(found.Error);
        }

        var (definition, version) = found.Value;

        var phaseContents = new List<WorkflowPhaseContent>();

        foreach (var phase in command.Phases)
        {
            var content = WorkflowPhaseContent.Create(
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

            if (content.IsFailure)
            {
                return Result.Failure<WorkflowGraphReplacement>(content.Error);
            }

            phaseContents.Add(content.Value);
        }

        var transitions = new List<PhaseTransition>();

        foreach (var transition in command.Transitions)
        {
            var created = PhaseTransition.Create(transition.FromPhaseCode, transition.ToPhaseCode);

            if (created.IsFailure)
            {
                return Result.Failure<WorkflowGraphReplacement>(created.Error);
            }

            transitions.Add(created.Value);
        }

        var now = clock.UtcNow;

        var replacedPhases = definition.ReplacePhases(version.Id, ids, phaseContents, now, command.By);

        if (replacedPhases.IsFailure)
        {
            return Result.Failure<WorkflowGraphReplacement>(replacedPhases.Error);
        }

        var replacedTransitions = definition.ReplaceTransitions(version.Id, transitions, now, command.By);

        if (replacedTransitions.IsFailure)
        {
            return Result.Failure<WorkflowGraphReplacement>(replacedTransitions.Error);
        }

        var replacedCategories = definition.ReplaceCategoryMapping(
            version.Id, command.CategoryKeys, now, command.By);

        if (replacedCategories.IsFailure)
        {
            return Result.Failure<WorkflowGraphReplacement>(replacedCategories.Error);
        }

        var findings = version.ValidateForPublication();

        if (findings.Any(finding => finding.Severity == WorkflowFindingSeverity.Error))
        {
            // Deliberately not saved: nothing below this calls SaveAsync, so the pending edits the three
            // replace calls above made to the tracked entities are discarded with the request scope and
            // the stored row is untouched.
            return Result.Success(new WorkflowGraphReplacement(null, findings));
        }

        var saved = await store.SaveAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<WorkflowGraphReplacement>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit,
            VersionEditedAction,
            OrdersAudit.WorkflowVersionEntity,
            version.Id,
            $"Workflow version {version.VersionNumber} of '{definition.Code}' edited: "
            + $"{version.Phases.Count} phase(s), {version.Transitions.Count} transition(s).",
            cancellationToken);

        return Result.Success(new WorkflowGraphReplacement(Administered(version), findings));
    }

    private static Result CloneInto(
        WorkflowDefinition definition,
        WorkflowVersion draft,
        Guid sourceVersionId,
        IIdGenerator ids,
        DateTimeOffset now,
        Guid? by)
    {
        var source = definition.FindVersion(sourceVersionId);

        if (source is null || source.Id == draft.Id)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotFound);
        }

        var phaseContents = new List<WorkflowPhaseContent>();

        foreach (var phase in source.Phases)
        {
            var content = WorkflowPhaseContent.Create(
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

            // The source phases were already validated when they were written, so this cannot fail in
            // practice — it is still answered as a Result rather than asserted, matching how every other
            // command in this handler reports what it is told rather than throwing.
            if (content.IsFailure)
            {
                return content;
            }

            phaseContents.Add(content.Value);
        }

        var replacedPhases = definition.ReplacePhases(draft.Id, ids, phaseContents, now, by);

        if (replacedPhases.IsFailure)
        {
            return replacedPhases;
        }

        var replacedTransitions = definition.ReplaceTransitions(draft.Id, source.Transitions, now, by);

        return replacedTransitions.IsFailure
            ? replacedTransitions
            : definition.ReplaceCategoryMapping(draft.Id, source.CategoryKeys, now, by);
    }

    private async Task<Result<(WorkflowDefinition Definition, WorkflowVersion Version)>> LoadVersionAsync(
        Guid workflowDefinitionId,
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var definition = await store.FindAsync(workflowDefinitionId, organisationId, cancellationToken);

        if (definition is null)
        {
            return Result.Failure<(WorkflowDefinition, WorkflowVersion)>(OrdersErrors.WorkflowDefinitionNotFound);
        }

        var version = definition.FindVersion(versionId);

        return version is null
            ? Result.Failure<(WorkflowDefinition, WorkflowVersion)>(OrdersErrors.WorkflowVersionNotFound)
            : Result.Success((definition, version));
    }

    private async Task<Result<(WorkflowDefinition Definition, WorkflowVersion Version)>> LoadVersionForChangeAsync(
        Guid workflowDefinitionId,
        Guid versionId,
        Guid organisationId,
        EntityTag expectedVersion,
        CancellationToken cancellationToken)
    {
        var found = await LoadVersionAsync(workflowDefinitionId, versionId, organisationId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value.Version;

        // The version's own row carries its own xmin (IWorkflowDefinitionStore.EntityTagOf(WorkflowVersion)),
        // distinct from the definition's — an edit to one version never moves the tag of another, or of
        // the definition it belongs to.
        return expectedVersion.Matches(store.EntityTagOf(version))
            ? found
            : Result.Failure<(WorkflowDefinition, WorkflowVersion)>(OrdersErrors.ConcurrentChange);
    }

    /// <summary>The concurrency token an edit to one of a definition's versions must be made against.</summary>
    /// <remarks>
    /// A thin passthrough so the <c>Api</c> project can build a version summary's own tag — the store
    /// interface lives in <c>Application.Abstractions</c> and an endpoint reaches it through the handler
    /// rather than taking a second dependency on the store itself.
    /// </remarks>
    /// <param name="version">A version returned by this handler.</param>
    /// <returns>The entity tag.</returns>
    public EntityTag EntityTagOf(WorkflowVersion version) => store.EntityTagOf(version);

    private AdministeredWorkflowVersion Administered(WorkflowVersion version)
        => new(version, store.EntityTagOf(version));
}

/// <summary>A workflow definition and the token an edit to it must be made against.</summary>
/// <param name="Definition">The definition.</param>
/// <param name="Tag">Its <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record AdministeredWorkflowDefinition(WorkflowDefinition Definition, EntityTag Tag);

/// <summary>A workflow version and the token an edit to it must be made against.</summary>
/// <param name="Version">The version.</param>
/// <param name="Tag">Its own <c>xmin</c>, distinct from its definition's.</param>
public sealed record AdministeredWorkflowVersion(WorkflowVersion Version, EntityTag Tag);

/// <summary>What replacing a version's graph produced.</summary>
/// <param name="Version">
/// The saved version, or null when the graph carried an error-severity finding and was not saved.
/// </param>
/// <param name="Findings">Every finding the six checks made, errors and warnings alike.</param>
public sealed record WorkflowGraphReplacement(AdministeredWorkflowVersion? Version, IReadOnlyList<WorkflowFinding> Findings)
{
    /// <summary>Whether the graph carried an error-severity finding and was refused.</summary>
    public bool WasRefused => Version is null;
}

/// <summary>Creates a workflow definition.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Code">The definition's machine key, for example <c>STITCH_STANDARD</c>.</param>
/// <param name="Name">What an administrator calls it.</param>
/// <param name="Description">Longer notes, or null.</param>
/// <param name="By">The administrator.</param>
public sealed record CreateWorkflowDefinitionCommand(
    Guid OrganisationId,
    string? Code,
    string? Name,
    string? Description,
    Guid? By);

/// <summary>Starts a new draft version of a definition.</summary>
/// <param name="WorkflowDefinitionId">The definition.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="CloneFromVersionId">
/// A version of the same definition to copy the graph from, or null to start empty.
/// </param>
/// <param name="By">The administrator.</param>
public sealed record CreateWorkflowVersionCommand(
    Guid WorkflowDefinitionId,
    Guid OrganisationId,
    Guid? CloneFromVersionId,
    Guid? By);

/// <summary>Replaces a draft version's whole graph.</summary>
/// <param name="WorkflowDefinitionId">The definition.</param>
/// <param name="VersionId">The version.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token the administrator last read the version with.</param>
/// <param name="Phases">Every phase the version should now hold.</param>
/// <param name="Transitions">Every transition the version should now hold.</param>
/// <param name="CategoryKeys">Every catalogue category key that should now use this version.</param>
/// <param name="By">The administrator.</param>
public sealed record ReplaceWorkflowVersionGraphCommand(
    Guid WorkflowDefinitionId,
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    IReadOnlyList<WorkflowPhaseInput> Phases,
    IReadOnlyList<WorkflowTransitionInput> Transitions,
    IReadOnlyList<string> CategoryKeys,
    Guid? By);

/// <summary>The unvalidated content of one phase, as a caller submitted it.</summary>
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
public sealed record WorkflowPhaseInput(
    string? Code,
    string? DisplayName,
    int Ordinal,
    IReadOnlyCollection<string>? RequiredRoleKeys,
    bool RequiresEvidence,
    TimeSpan? ExpectedDuration,
    TimeSpan? Sla,
    bool IsOptional,
    bool IsSkippable,
    bool IsTerminal);

/// <summary>The unvalidated content of one transition, as a caller submitted it.</summary>
/// <param name="FromPhaseCode">The phase work leaves.</param>
/// <param name="ToPhaseCode">The phase work enters.</param>
public sealed record WorkflowTransitionInput(string? FromPhaseCode, string? ToPhaseCode);
