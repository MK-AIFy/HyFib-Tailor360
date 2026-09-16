using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// A named production process — "stitching, standard" — and every version anyone has ever drafted, published or
/// retired of it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The definition is the aggregate root, not the version.</strong> <c>IWorkflowDefinitionStore</c> loads
/// one whole, with every version, because deciding a new version's number and refusing a second published
/// version both need every version in view at once — the same reasoning
/// <c>docs/architecture/module-ownership.md</c> line 407's three-table grouping already assumes. This is the one
/// place <see cref="WorkflowVersion"/>'s <see langword="internal"/> mutators may be called from.
/// </para>
/// <para>
/// <strong>This slice builds the record and nothing that reads it.</strong> No endpoint, no phase instance, no
/// assignment (issue #232's own scope). <c>GarmentJob.WorkflowDefinitionId</c> and
/// <c>GarmentJob.WorkflowVersionId</c> already exist and already refuse an empty or a repeated pin
/// (<c>GarmentJob.StartProduction</c>) — this type is simply the thing those columns finally point at something
/// real, with no foreign key between the two schema areas because none crosses a module boundary here either;
/// <c>garment_jobs</c> is untouched by this issue's migration.
/// </para>
/// </remarks>
public sealed class WorkflowDefinition
{
    /// <summary>The longest name the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest description the column holds.</summary>
    public const int MaximumDescriptionLength = 2000;

    /// <summary>The longest category key <see cref="WorkflowVersion.CategoryKeys"/> holds.</summary>
    public const int MaximumCategoryKeyLength = 40;

    private readonly List<WorkflowVersion> _versions = [];

    private WorkflowDefinition()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private WorkflowDefinition(
        Guid id,
        Guid organisationId,
        string code,
        string name,
        string? description,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the definition. A UUIDv7, and what a catalogue link and a version both refer to.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The definition's identity as a concept, for example <c>STITCH_STANDARD</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What an administrator calls the process.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Longer notes for the reviewer, where there are any.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether the definition is offered to a new catalogue link.
    /// </summary>
    /// <remarks>
    /// Deactivating a definition does not touch its versions or anything pinned to one — the flag governs only
    /// whether a future link (E06-F02-2's route, or the catalogue's own service-type editor) may choose it,
    /// exactly as <c>CustomerStatus.Deactivated</c> governs only whether a customer may be chosen for new work
    /// and not what already names them.
    /// </remarks>
    public bool IsActive { get; private set; }

    /// <summary>Every version, draft, published or retired. Never removed.</summary>
    public IReadOnlyList<WorkflowVersion> Versions => _versions;

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Creates a new, active definition with no versions yet.</summary>
    /// <param name="id">The new definition's identity, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="code">The definition's identity as a concept. Upper snake case; see <see cref="WorkflowCode"/>.</param>
    /// <param name="name">What an administrator calls it.</param>
    /// <param name="description">Longer notes, or null.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator, or null for a seeding run.</param>
    /// <returns>The definition, or the reason it could not be created.</returns>
    public static Result<WorkflowDefinition> Create(
        Guid id,
        Guid organisationId,
        string? code,
        string? name,
        string? description,
        DateTimeOffset now,
        Guid? by)
    {
        if (!WorkflowCode.IsWellFormed(code))
        {
            return Result.Failure<WorkflowDefinition>(OrdersErrors.WorkflowCodeNotWellFormed("code"));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<WorkflowDefinition>(OrdersErrors.Required("name"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure<WorkflowDefinition>(OrdersErrors.TooLong("name", MaximumNameLength));
        }

        if (description is { Length: > 0 } && description.Length > MaximumDescriptionLength)
        {
            return Result.Failure<WorkflowDefinition>(
                OrdersErrors.TooLong("description", MaximumDescriptionLength));
        }

        return Result.Success(
            new WorkflowDefinition(id, organisationId, code!, name.Trim(), description, now, by));
    }

    /// <summary>Marks the definition offered, or withdrawn, for a new catalogue link.</summary>
    /// <param name="isActive">The new state.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    public void SetActive(bool isActive, DateTimeOffset now, Guid? by)
    {
        IsActive = isActive;
        Touch(now, by);
    }

    /// <summary>Finds one of this definition's versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <returns>The version, or null when no version of this definition has that identity.</returns>
    public WorkflowVersion? FindVersion(Guid versionId)
        => _versions.FirstOrDefault(version => version.Id == versionId);

    /// <summary>The version that was published at a given instant, or null.</summary>
    /// <remarks>
    /// The partial unique index the migration adds means at most one version can ever answer true here for any
    /// one instant, so there is nothing to disambiguate between candidates the way
    /// <c>ICatalogAvailabilityQuery.GetServiceAsync</c> has to.
    /// </remarks>
    /// <param name="at">The instant to judge.</param>
    /// <returns>The version published at that instant, or null when none was.</returns>
    public WorkflowVersion? FindPublishedVersion(DateTimeOffset at)
        => _versions.FirstOrDefault(version => version.WasPublishedAt(at));

    /// <summary>Starts a new draft version, numbered after every version this definition already has.</summary>
    /// <param name="ids">The identifier generator, so no version is minted with <c>Guid.NewGuid</c>.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The new draft.</returns>
    public WorkflowVersion AddVersion(IIdGenerator ids, DateTimeOffset now, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var version = WorkflowVersion.Start(ids.NewId(), Id, OrganisationId, NextVersionNumber(), now, by);

        _versions.Add(version);
        Touch(now, by);

        return version;
    }

    /// <summary>Replaces every phase of one of this definition's versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="ids">The identifier generator for the new phase rows.</param>
    /// <param name="phases">The validated content for every phase.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be replaced.</returns>
    public Result ReplacePhases(
        Guid versionId,
        IIdGenerator ids,
        IReadOnlyList<WorkflowPhaseContent> phases,
        DateTimeOffset now,
        Guid? by)
        => FindVersion(versionId) is { } version
            ? version.ReplacePhases(ids, phases, now, by)
            : Result.Failure(OrdersErrors.WorkflowVersionNotFound);

    /// <summary>Replaces every transition of one of this definition's versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="transitions">The validated edges.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be replaced.</returns>
    public Result ReplaceTransitions(
        Guid versionId,
        IReadOnlyList<PhaseTransition> transitions,
        DateTimeOffset now,
        Guid? by)
        => FindVersion(versionId) is { } version
            ? version.ReplaceTransitions(transitions, now, by)
            : Result.Failure(OrdersErrors.WorkflowVersionNotFound);

    /// <summary>Replaces which catalogue categories use one of this definition's versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="categoryKeys">The category keys, each named once.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be replaced.</returns>
    public Result ReplaceCategoryMapping(
        Guid versionId,
        IReadOnlyCollection<string> categoryKeys,
        DateTimeOffset now,
        Guid? by)
        => FindVersion(versionId) is { } version
            ? version.ReplaceCategoryMapping(categoryKeys, now, by)
            : Result.Failure(OrdersErrors.WorkflowVersionNotFound);

    /// <summary>Publishes one of this definition's versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being published.</param>
    /// <returns>Success, or the reason it could not be published.</returns>
    public Result Publish(Guid versionId, DateTimeOffset now, Guid? by, string? reason)
        => FindVersion(versionId) is { } version
            ? version.Publish(now, by, reason)
            : Result.Failure(OrdersErrors.WorkflowVersionNotFound);

    /// <summary>Retires one of this definition's published versions.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why it is being retired.</param>
    /// <returns>Success, or the reason it could not be retired.</returns>
    public Result Retire(Guid versionId, DateTimeOffset now, Guid? by, string? reason)
        => FindVersion(versionId) is { } version
            ? version.Retire(now, by, reason)
            : Result.Failure(OrdersErrors.WorkflowVersionNotFound);

    private int NextVersionNumber() => _versions.Count == 0 ? 1 : _versions.Max(version => version.VersionNumber) + 1;

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
