using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// What Reception and a customer have chosen so far on the design picker for one garment (#30, issue
/// #140). Work in progress, and never a substitute for the rules being asked again at confirmation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Started for a service type of the published version and pinned to it for its life</strong>
/// (<c>docs/prd/design-options.md</c> section 7), the same way a measurement draft is pinned to a
/// template version (<c>docs/prd/measurement-templates.md</c> section 11): the version <see cref="Save"/>
/// and every later <c>Validate</c> call is asked against is <see cref="CatalogVersionId"/>, not whatever
/// happens to be published when the question is asked. Republishing the catalogue changes nothing here
/// until <see cref="MigrateTo"/> is called.
/// </para>
/// <para>
/// <strong>It is shared within the branch</strong> — saved whole against an <c>If-Match</c> rather than
/// per group, because the whole selection set is what one screen renders and what one person is looking
/// at when they press save.
/// </para>
/// <para>
/// <strong>It expires</strong>, by default after the same twenty-four hours the measurement draft uses.
/// Unlike a measurement draft's <c>Confirm</c>, nothing here consumes it directly: that happens once
/// Orders (#32a) pins the snapshot at order confirmation, through a mechanism this module publishes as a
/// capability (<see cref="Consume"/>) rather than as a route, because no caller exists yet.
/// </para>
/// <para>
/// <strong>A draft accepts only what could never be meant.</strong> <see cref="Save"/> refuses a value
/// for a group or an option its own service type does not link at all — the same line
/// <c>MeasurementDraft.SaveSection</c> draws, narrowed from the whole version to this service type's own
/// groups so a group only a sibling service offers is never accepted — and says nothing about whether an
/// option is offerable at a branch today, whether a rule is satisfied, or whether a required group is
/// still unset. Those are
/// <see cref="Catalogue.IDesignSelectionValidator"/>'s answer, asked as many times as the picker likes
/// and asked once more, authoritatively, wherever a selection is about to be relied on.
/// </para>
/// </remarks>
public sealed class DesignSelectionDraft
{
    /// <summary>The longest free-text instructions this draft accepts, matching the column's <c>varchar(2000)</c>.</summary>
    public const int MaximumInstructionsLength = 2000;

    private readonly List<DesignDraftSelection> _selections = [];

    private DesignSelectionDraft()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private DesignSelectionDraft(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid catalogVersionId,
        Guid serviceTypeId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CatalogVersionId = catalogVersionId;
        ServiceTypeId = serviceTypeId;
        StartedAt = now;
        StartedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
        ExpiresAt = expiresAt;
    }

    /// <summary>Identity of this draft. What the routes and the eventual <c>OrderDraftGarment</c> pin address.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the catalogue belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch choosing. Drafts are shared within it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>
    /// The catalogue version pinned when the draft started, or last migrated to. Published when pinned,
    /// though it may since have been retired — the pin holds regardless.
    /// </summary>
    public Guid CatalogVersionId { get; private set; }

    /// <summary>The service type being chosen for, as a row of <see cref="CatalogVersionId"/>.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>When the draft was started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Who started it.</summary>
    public Guid? StartedBy { get; private set; }

    /// <summary>When it was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it stops being work in progress, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was consumed at confirmation, or null while it is still work in progress.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>
    /// What Reception typed that is not any option — carried onto <c>GarmentDesignSnapshot</c> as
    /// craft instructions. Never priced (OD-DES-06 in <c>docs/prd/design-options.md</c>).
    /// </summary>
    public string? Instructions { get; private set; }

    /// <summary>What has been chosen so far, one entry per group; a group not listed is unset.</summary>
    public IReadOnlyList<DesignDraftSelection> Selections => _selections;

    /// <summary>Whether it may still be written to and is not yet spent.</summary>
    public bool IsOpen => ConsumedAt is null;

    /// <summary>Whether the draft has outlived its lifetime, as of a given instant.</summary>
    /// <param name="now">The instant to judge.</param>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Starts choosing a design for a service type of the published version.</summary>
    /// <param name="id">Identity of the draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch choosing.</param>
    /// <param name="version">The published version to pin to.</param>
    /// <param name="serviceType">The service type being chosen for, a row of <paramref name="version"/>.</param>
    /// <param name="now">The clock.</param>
    /// <param name="lifetime">How long a draft stays work in progress.</param>
    /// <param name="by">Who started it.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<DesignSelectionDraft> Start(
        Guid id,
        Guid organisationId,
        Guid branchId,
        CatalogVersion version,
        ServiceType serviceType,
        DateTimeOffset now,
        TimeSpan lifetime,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(serviceType);

        // A draft against a draft version would capture choices nobody has yet published, and against a
        // retired one would capture against a version the shop has stopped using — the same guard
        // MeasurementDraft.Start applies to a template version.
        if (version.Status != CatalogStatus.Published)
        {
            return Result.Failure<DesignSelectionDraft>(CatalogErrors.VersionNotPublishable);
        }

        if (serviceType.CatalogVersionId != version.Id)
        {
            return Result.Failure<DesignSelectionDraft>(CatalogErrors.ServiceTypeNotFound);
        }

        return Result.Success(new DesignSelectionDraft(
            id, organisationId, branchId, version.Id, serviceType.Id, now, now.Add(lifetime), by));
    }

    /// <summary>
    /// Replaces the whole selection set and the free-text instructions.
    /// </summary>
    /// <remarks>
    /// Whole rather than per group, because the picker renders and saves the whole garment's choices at
    /// once (<c>docs/prd/design-options.md</c> section 7) — there is no per-step wizard here the way
    /// there is for measurements. A group named twice in the input is refused rather than merged, the
    /// same way <c>DesignRuleEngine</c> refuses more than one value for a single-choice group.
    /// </remarks>
    /// <param name="version">The version this draft is pinned to.</param>
    /// <param name="serviceType">
    /// The service type this draft names, a row of <paramref name="version"/>. What may be saved is
    /// narrowed to exactly <see cref="ServiceType.DesignOptionGroupIds"/> rather than the whole
    /// category, so a group only a sibling service offers is refused the same way a group this version
    /// never had at all is (#140).
    /// </param>
    /// <param name="selections">What was chosen, one entry per group.</param>
    /// <param name="instructions">Free-text craft instructions, or null.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">Who saved it.</param>
    /// <returns>Success, or the reason the save was refused.</returns>
    public Result Save(
        CatalogVersion version,
        ServiceType serviceType,
        IReadOnlyCollection<DesignSelectionInput> selections,
        string? instructions,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(selections);

        if (!IsOpen)
        {
            return Result.Failure(CatalogErrors.DesignDraftAlreadyConsumed);
        }

        // An aged draft is refused here rather than only at whatever eventually consumes it: this
        // module has no confirmation step of its own to be the last word, so a write is the honest
        // place to say a garment left half-chosen for a day and a half is not being extended quietly.
        if (IsExpired(now))
        {
            return Result.Failure(CatalogErrors.DesignDraftExpired);
        }

        if (instructions is { Length: > MaximumInstructionsLength })
        {
            return Result.Failure(CatalogErrors.TooLong("instructions", MaximumInstructionsLength));
        }

        var groups = DesignSelectionMigration.GroupsOf(version, serviceType)
            .ToDictionary(group => group.Code, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var built = new List<DesignDraftSelection>(selections.Count);

        foreach (var selection in selections)
        {
            if (!seen.Add(selection.GroupCode))
            {
                return Result.Failure(CatalogErrors.CodeNotUnique("groupCode"));
            }

            if (!groups.TryGetValue(selection.GroupCode, out var group))
            {
                return Result.Failure(CatalogErrors.DesignGroupNotFound);
            }

            var codes = selection.OptionCodes.Distinct(StringComparer.Ordinal).ToArray();

            foreach (var code in codes)
            {
                if (group.FindOptionByCode(code) is null)
                {
                    return Result.Failure(CatalogErrors.DesignOptionNotFound);
                }
            }

            if (codes.Length > 0)
            {
                built.Add(DesignDraftSelection.Of(selection.GroupCode, codes));
            }
        }

        _selections.Clear();
        _selections.AddRange(built);
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }

    /// <summary>Re-pins the draft to a later version and replaces its selections with the migrated set.</summary>
    /// <remarks>
    /// Called with the plan <see cref="DesignSelectionMigration.Plan"/> produced: the caller has already
    /// decided what survives, and this only applies it. A selection the plan dropped — its option
    /// retired, or its group no longer offered — is simply absent from
    /// <paramref name="migratedSelections"/>, exactly as an unset group is.
    /// </remarks>
    /// <param name="catalogVersionId">The version to pin to. Must be the currently published one.</param>
    /// <param name="serviceTypeId">The equivalent service type in that version.</param>
    /// <param name="migratedSelections">What survives the migration.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">Who asked for the migration.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result MigrateTo(
        Guid catalogVersionId,
        Guid serviceTypeId,
        IReadOnlyCollection<DesignSelectionInput> migratedSelections,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(migratedSelections);

        if (!IsOpen)
        {
            return Result.Failure(CatalogErrors.DesignDraftAlreadyConsumed);
        }

        if (IsExpired(now))
        {
            return Result.Failure(CatalogErrors.DesignDraftExpired);
        }

        CatalogVersionId = catalogVersionId;
        ServiceTypeId = serviceTypeId;

        _selections.Clear();
        foreach (var selection in migratedSelections)
        {
            if (selection.OptionCodes.Count > 0)
            {
                _selections.Add(DesignDraftSelection.Of(selection.GroupCode, [.. selection.OptionCodes]));
            }
        }

        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }

    /// <summary>
    /// Marks the draft spent, so it can never be pinned by a second confirmation.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>MeasurementDraft.Consume</c> exactly, including checking expiry here rather than
    /// leaving it to a sweep: a draft that outlived its lifetime and has not yet been reaped is still
    /// too old to found a confirmation on. This is a capability the module publishes for whatever
    /// mechanism #32a's order confirmation uses to spend it — synchronously, through a confirmation hook,
    /// or from the delivery of an integration event — and is not itself reachable over HTTP, because no
    /// caller exists yet.
    /// </remarks>
    /// <param name="now">The clock.</param>
    /// <returns>Success, or the reason it could not be consumed.</returns>
    public Result Consume(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(CatalogErrors.DesignDraftAlreadyConsumed);
        }

        if (IsExpired(now))
        {
            return Result.Failure(CatalogErrors.DesignDraftExpired);
        }

        ConsumedAt = now;

        return Result.Success();
    }
}
