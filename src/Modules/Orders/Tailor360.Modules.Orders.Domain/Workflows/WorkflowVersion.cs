using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// One edition of a workflow definition: its phases, its transitions, which catalogue categories use it, and
/// where it stands in its draft → published → retired life (plan decision D8).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reached only through <see cref="WorkflowDefinition"/>, never edited directly.</strong> Every mutator
/// here is <see langword="internal"/>, exactly as <c>OrderDraftGarment</c>'s are: the definition is the
/// aggregate root — <c>IWorkflowDefinitionStore</c> "loads a definition whole with its versions" — so version
/// numbering, which version may be published, and the partial unique index the migration adds all have to be
/// reasoned about from one place holding every version at once.
/// </para>
/// <para>
/// <strong>A published version is immutable but for retirement.</strong> Every mutator refuses once
/// <see cref="Status"/> leaves <see cref="WorkflowVersionStatus.Draft"/>, naming the status it found — the
/// acceptance criterion this issue states outright, and the reason <c>OrdersErrors.WorkflowVersionNotEditable</c>
/// takes the status as an argument rather than being one fixed message the way Catalog's own
/// <c>VersionNotEditable</c> is.
/// </para>
/// <para>
/// <strong>The phase graph is checked at publication, not while it is being built.</strong>
/// <see cref="ReplacePhases"/> and <see cref="ReplaceTransitions"/> validate each phase and each edge on its own
/// terms (<see cref="WorkflowPhaseContent"/>'s own checks; <see cref="PhaseTransition"/>'s own checks) but not
/// the shape of the whole graph together — a draft may briefly hold a duplicate code or an unreachable phase
/// while an administrator is still working, exactly as <c>WorkflowGraphTests</c>'s scenarios need to exist as
/// drafts before they can be asked about. <see cref="WorkflowGraph.Validate"/> is asked only by
/// <see cref="ValidateForPublication"/> and by <see cref="Publish"/> itself.
/// </para>
/// </remarks>
public sealed class WorkflowVersion
{
    /// <summary>The longest publish or retire reason the column holds.</summary>
    public const int MaximumReasonLength = 1000;

    private readonly List<WorkflowPhase> _phases = [];
    private readonly List<PhaseTransition> _transitions = [];
    private readonly List<string> _categoryKeys = [];

    private WorkflowVersion()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private WorkflowVersion(
        Guid id,
        Guid workflowDefinitionId,
        Guid organisationId,
        int versionNumber,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        WorkflowDefinitionId = workflowDefinitionId;
        OrganisationId = organisationId;
        VersionNumber = versionNumber;
        Status = WorkflowVersionStatus.Draft;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the version. What a job pins forever once it starts production against it.</summary>
    public Guid Id { get; private set; }

    /// <summary>The definition this is a version of.</summary>
    public Guid WorkflowDefinitionId { get; private set; }

    /// <summary>The organisation. Denormalised from the definition, per this issue's own scope.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>
    /// The version's number within its definition, counting from one. A label an administrator says out loud,
    /// never a key — every reference is <see cref="Id"/>, for the reason <c>CatalogVersion.VersionNumber</c>'s
    /// own remarks give.
    /// </summary>
    public int VersionNumber { get; private set; }

    /// <summary>Where this version stands in its life.</summary>
    public WorkflowVersionStatus Status { get; private set; }

    /// <summary>The phases, in no particular row order — a screen orders them by <see cref="WorkflowPhase.Ordinal"/>.</summary>
    public IReadOnlyList<WorkflowPhase> Phases => _phases;

    /// <summary>The transitions between phases.</summary>
    public IReadOnlyList<PhaseTransition> Transitions => _transitions;

    /// <summary>Which catalogue categories use this version, by their Catalog-owned key.</summary>
    /// <remarks>
    /// A bag of identifiers this module does not own the format of and cannot confirm are real — reading
    /// Catalog's tables to check would be exactly the boundary violation ARCH-005 exists to prevent. What is
    /// checked here is only what this module can check: that a key is not empty, not too long, and named once.
    /// </remarks>
    public IReadOnlyList<string> CategoryKeys => _categoryKeys;

    /// <summary>Whether the version may still be edited.</summary>
    public bool IsEditable => Status == WorkflowVersionStatus.Draft;

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it was published, or null while it is a draft.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who published it.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Why it was published. Publication demands a reason.</summary>
    public string? PublishReason { get; private set; }

    /// <summary>When it was retired, or null while it is not.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Who retired it.</summary>
    public Guid? RetiredBy { get; private set; }

    /// <summary>Why it was retired. Retirement demands a reason.</summary>
    public string? RetiredReason { get; private set; }

    /// <summary>Starts an empty draft.</summary>
    /// <param name="id">The new version's identity, from <c>IIdGenerator</c>.</param>
    /// <param name="workflowDefinitionId">The definition it belongs to.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="versionNumber">The next number in the definition's own sequence.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator, or null for a seeding run.</param>
    /// <returns>The draft.</returns>
    internal static WorkflowVersion Start(
        Guid id,
        Guid workflowDefinitionId,
        Guid organisationId,
        int versionNumber,
        DateTimeOffset now,
        Guid? by)
        => new(id, workflowDefinitionId, organisationId, versionNumber, now, by);

    /// <summary>Replaces every phase this version holds.</summary>
    /// <remarks>
    /// The whole set at once, never one phase at a time — <see cref="WorkflowPhase"/>'s own remarks give the
    /// reason. Validates each phase's own content but not the graph it forms with the current transitions;
    /// <see cref="ReplaceTransitions"/> may follow in either order, which is exactly why the two are not
    /// cross-checked here.
    /// </remarks>
    /// <param name="ids">The identifier generator, so no phase row is minted with <c>Guid.NewGuid</c>.</param>
    /// <param name="phases">The validated content for every phase.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason the replacement was refused.</returns>
    internal Result ReplacePhases(
        IIdGenerator ids,
        IReadOnlyList<WorkflowPhaseContent> phases,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(phases);

        if (!IsEditable)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotEditable(Status));
        }

        _phases.Clear();
        _phases.AddRange(
            phases.Select(content => WorkflowPhase.Add(ids.NewId(), Id, OrganisationId, content, now, by)));
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Replaces every transition this version holds.</summary>
    /// <param name="transitions">The validated edges.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason the replacement was refused.</returns>
    internal Result ReplaceTransitions(IReadOnlyList<PhaseTransition> transitions, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(transitions);

        if (!IsEditable)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotEditable(Status));
        }

        _transitions.Clear();
        _transitions.AddRange(transitions);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Replaces which catalogue categories use this version.</summary>
    /// <param name="categoryKeys">The category keys, each named once.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason the replacement was refused.</returns>
    internal Result ReplaceCategoryMapping(
        IReadOnlyCollection<string> categoryKeys,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(categoryKeys);

        if (!IsEditable)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotEditable(Status));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in categoryKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result.Failure(OrdersErrors.Required("categoryKeys"));
            }

            if (key.Length > WorkflowDefinition.MaximumCategoryKeyLength)
            {
                return Result.Failure(
                    OrdersErrors.TooLong("categoryKeys", WorkflowDefinition.MaximumCategoryKeyLength));
            }

            if (!seen.Add(key))
            {
                return Result.Failure(OrdersErrors.DuplicateWorkflowCategoryMapping);
            }
        }

        _categoryKeys.Clear();
        _categoryKeys.AddRange(categoryKeys);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>The findings publishing this version today would carry, without publishing it.</summary>
    /// <remarks>
    /// What an administration screen renders before committing to publish — Customers'
    /// <c>MeasurementCaptureHandler.CheckAsync</c> is the same shape for the same reason: a caller decides
    /// whether to try the command at all from a read that costs nothing to repeat.
    /// </remarks>
    /// <returns>Every finding <see cref="WorkflowGraph"/> has for this version's current phases and transitions.</returns>
    public IReadOnlyList<WorkflowFinding> ValidateForPublication() => WorkflowGraph.Validate(_phases, _transitions);

    /// <summary>Publishes the version, refusing if its phase graph does not pass every check.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being published. Required.</param>
    /// <returns>Success, or the reason it could not be published.</returns>
    public Result Publish(DateTimeOffset now, Guid? by, string? reason)
    {
        if (Status != WorkflowVersionStatus.Draft)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotPublishable(Status));
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        if (ValidateForPublication().Any(finding => finding.Severity == WorkflowFindingSeverity.Error))
        {
            return Result.Failure(OrdersErrors.WorkflowGraphInvalid);
        }

        Status = WorkflowVersionStatus.Published;
        PublishedAt = now;
        PublishedBy = by;
        PublishReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Retires the published version.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being retired. Required.</param>
    /// <returns>Success, or the reason it could not be retired.</returns>
    public Result Retire(DateTimeOffset now, Guid? by, string? reason)
    {
        if (Status != WorkflowVersionStatus.Published)
        {
            return Result.Failure(OrdersErrors.WorkflowVersionNotRetirable(Status));
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = WorkflowVersionStatus.Retired;
        RetiredAt = now;
        RetiredBy = by;
        RetiredReason = reason;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Whether this version was the published one at a given instant.</summary>
    /// <remarks>
    /// True from the moment it was published until the moment it was retired — and forever true of that window
    /// once it has closed, so a job pinned to a version years ago reads back which version was current when it
    /// started production, not only which version is current today. A version never published answers false at
    /// every instant.
    /// </remarks>
    /// <param name="at">The instant to judge.</param>
    /// <returns>True when this version was published and not yet retired at that instant.</returns>
    public bool WasPublishedAt(DateTimeOffset at)
        => PublishedAt is { } published && published <= at && (RetiredAt is not { } retired || retired > at);

    private static Result CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(OrdersErrors.ReasonRequired);
        }

        return reason.Length > MaximumReasonLength
            ? Result.Failure(OrdersErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
