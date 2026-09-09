using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// One coherent field set, drafted, reviewed, published and eventually retired as a whole.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The version is what a measurement is pinned to.</strong> A captured measurement records the version it
/// was taken under, and renders through that version's labels, units, precision and rules forever — which is why a
/// published version is immutable in the database as well as in this code, and why retirement stops new captures
/// without touching old ones (<c>docs/prd/measurement-templates.md</c> section 2, historic rendering).
/// </para>
/// <para>
/// Editing is a draft-only act. <c>docs/prd/measurement-templates.md</c> section 3 calls a label "editable at any
/// time", and issue #27's acceptance criteria say a published template cannot be edited in place; the two fit
/// together one way — a label is editable at any point in the template's life, in the draft that becomes the next
/// version. This class takes the acceptance criterion as the binding one, because it is the harder constraint and
/// the one a customer's stored measurements depend on.
/// </para>
/// </remarks>
public sealed class TemplateVersion
{
    /// <summary>The longest version name the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest note the column holds.</summary>
    public const int MaximumNotesLength = 2000;

    /// <summary>The longest reason the column holds.</summary>
    public const int MaximumReasonLength = 500;

    private readonly List<TemplateField> _fields = [];

    private TemplateVersion()
    {
        Name = null!;
    }

    /// <summary>The version's row identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The template this is a version of.</summary>
    public Guid MeasurementTemplateId { get; private set; }

    /// <summary>The organisation that owns it.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>Version 1, 2, 3 — unique within the template and never reused.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>What this version is called, for administrators reading a list of them.</summary>
    public string Name { get; private set; }

    /// <summary>What changed and why, for the next administrator.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where it sits in its life.</summary>
    public TemplateStatus Status { get; private set; }

    /// <summary>The unit the wizard opens in for this template (OD-MEA-06).</summary>
    public DisplayUnit DefaultDisplayUnit { get; private set; } = DisplayUnit.Inch;

    /// <summary>When it was submitted for review.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Who submitted it. The same person does not approve it.</summary>
    public Guid? SubmittedBy { get; private set; }

    /// <summary>When it was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Who approved it.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>When it became the version measurements are captured against.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who published it.</summary>
    public Guid? PublishedBy { get; private set; }

    /// <summary>Why it was published.</summary>
    public string? PublishReason { get; private set; }

    /// <summary>When it stopped taking new captures.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Who retired it.</summary>
    public Guid? RetiredBy { get; private set; }

    /// <summary>Why it was retired.</summary>
    public string? RetiredReason { get; private set; }

    /// <summary>When the version was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>The fields, in the order a tailor measures them.</summary>
    public IReadOnlyList<TemplateField> Fields => _fields;

    /// <summary>Whether this version still accepts edits.</summary>
    public bool IsEditable => Status == TemplateStatus.Draft;

    /// <summary>Creates an empty draft.</summary>
    /// <param name="id">The row identity.</param>
    /// <param name="templateId">The template.</param>
    /// <param name="organisationId">The owning organisation.</param>
    /// <param name="versionNumber">The next number for this template.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="notes">What is changing and why.</param>
    /// <param name="defaultDisplayUnit">The unit the wizard opens in.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<TemplateVersion> CreateDraft(
        Guid id,
        Guid templateId,
        Guid organisationId,
        int versionNumber,
        string name,
        string? notes,
        DisplayUnit defaultDisplayUnit,
        DateTimeOffset now,
        Guid? by)
    {
        var checkedName = CheckName(name, notes);

        if (checkedName.IsFailure)
        {
            return Result.Failure<TemplateVersion>(checkedName.Error);
        }

        if (defaultDisplayUnit == DisplayUnit.Count)
        {
            return Result.Failure<TemplateVersion>(MeasurementErrors.DefaultUnitNotEnterable);
        }

        return Result.Success(new TemplateVersion
        {
            Id = id,
            MeasurementTemplateId = templateId,
            OrganisationId = organisationId,
            VersionNumber = versionNumber,
            Name = name.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = TemplateStatus.Draft,
            DefaultDisplayUnit = defaultDisplayUnit,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        });
    }

    /// <summary>Copies this version's field set into a fresh draft.</summary>
    /// <param name="ids">The identifier generator.</param>
    /// <param name="versionNumber">The next number for this template.</param>
    /// <param name="name">What to call the draft.</param>
    /// <param name="notes">What is changing and why.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    /// <remarks>
    /// Cloning is how a published version is changed: the fields are copied with fresh row identities and the same
    /// keys, so the new draft describes the same measurements and can be edited freely without touching what the
    /// published version says.
    /// </remarks>
    public Result<TemplateVersion> CloneAsDraft(
        IIdGenerator ids,
        int versionNumber,
        string name,
        string? notes,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var draft = CreateDraft(
            ids.NewId(), MeasurementTemplateId, OrganisationId, versionNumber, name, notes,
            DefaultDisplayUnit, now, by);

        if (draft.IsFailure)
        {
            return draft;
        }

        foreach (var field in _fields)
        {
            var copy = TemplateField.Create(
                ids.NewId(), OrganisationId, draft.Value.Id, DefinitionOf(field));

            if (copy.IsFailure)
            {
                return Result.Failure<TemplateVersion>(copy.Error);
            }

            draft.Value._fields.Add(copy.Value);
        }

        return draft;
    }

    /// <summary>Adds a field to a draft.</summary>
    /// <param name="id">The row identity.</param>
    /// <param name="definition">The field.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The field, or the reason it was refused.</returns>
    public Result<TemplateField> AddField(
        Guid id,
        TemplateFieldDefinition definition,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!IsEditable)
        {
            return Result.Failure<TemplateField>(MeasurementErrors.VersionNotEditable);
        }

        if (_fields.Any(field => field.Key.Value == definition.Key.Trim()))
        {
            return Result.Failure<TemplateField>(MeasurementErrors.DuplicateFieldKey(definition.Key.Trim()));
        }

        var created = TemplateField.Create(id, OrganisationId, Id, definition);

        if (created.IsFailure)
        {
            return created;
        }

        _fields.Add(created.Value);
        Touch(now, by);

        return created;
    }

    /// <summary>Replaces a field of a draft, keeping its key.</summary>
    /// <param name="fieldId">The field.</param>
    /// <param name="definition">The new definition.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The field, or the reason the change was refused.</returns>
    public Result<TemplateField> EditField(
        Guid fieldId,
        TemplateFieldDefinition definition,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!IsEditable)
        {
            return Result.Failure<TemplateField>(MeasurementErrors.VersionNotEditable);
        }

        if (_fields.SingleOrDefault(field => field.Id == fieldId) is not { } existing)
        {
            return Result.Failure<TemplateField>(MeasurementErrors.FieldNotFound);
        }

        // The key is the field's identity, and an edit that changed it would silently orphan every value already
        // filed under the old one. Renaming is removing and adding, which is visible in the audit trail.
        var applied = existing.Apply(definition with { Key = existing.Key.Value });

        if (applied.IsFailure)
        {
            return Result.Failure<TemplateField>(applied.Error);
        }

        Touch(now, by);

        return Result.Success(existing);
    }

    /// <summary>Removes a field from a draft.</summary>
    /// <param name="fieldId">The field.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason the removal was refused.</returns>
    public Result RemoveField(Guid fieldId, DateTimeOffset now, Guid? by)
    {
        if (!IsEditable)
        {
            return Result.Failure(MeasurementErrors.VersionNotEditable);
        }

        if (_fields.SingleOrDefault(field => field.Id == fieldId) is not { } existing)
        {
            return Result.Failure(MeasurementErrors.FieldNotFound);
        }

        _fields.Remove(existing);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Submits the draft for a second administrator to review.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be submitted.</returns>
    public Result Submit(DateTimeOffset now, Guid? by)
    {
        if (Status != TemplateStatus.Draft)
        {
            return Result.Failure(MeasurementErrors.VersionNotSubmittable);
        }

        if (_fields.Count == 0)
        {
            return Result.Failure(MeasurementErrors.VersionHasNoFields);
        }

        Status = TemplateStatus.InReview;
        SubmittedAt = now;
        SubmittedBy = by;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Sends a version in review back to its author.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>Success, or the reason it could not be returned.</returns>
    /// <remarks>
    /// The one backwards transition, and it is backwards only in the sense that the version becomes editable again.
    /// Nothing has been captured against a version in review, so nothing can be invalidated by reopening it — which
    /// is exactly why the same move is impossible once it is published.
    /// </remarks>
    public Result ReturnToDraft(DateTimeOffset now, Guid? by)
    {
        if (Status != TemplateStatus.InReview)
        {
            return Result.Failure(MeasurementErrors.VersionNotApprovable);
        }

        Status = TemplateStatus.Draft;
        SubmittedAt = null;
        SubmittedBy = null;

        // The approval goes with it. A version that comes back for changes and is then resubmitted has not been
        // reviewed in the state it is now in, and leaving the approval standing would let it be published on the
        // strength of somebody having read an earlier draft.
        ApprovedAt = null;
        ApprovedBy = null;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Approves a reviewed version, so that it may be published.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The reviewing administrator.</param>
    /// <param name="soleAdministrator">
    /// True when this organisation has only one administrator, in which case the submitter reviews their own work
    /// because there is nobody else — the alternative is a shop that cannot publish a template at all.
    /// </param>
    /// <returns>Success, or the reason it could not be approved.</returns>
    public Result Approve(DateTimeOffset now, Guid? by, bool soleAdministrator)
    {
        if (Status != TemplateStatus.InReview)
        {
            return Result.Failure(MeasurementErrors.VersionNotApprovable);
        }

        if (ApprovedAt is not null)
        {
            return Result.Failure(MeasurementErrors.VersionAlreadyApproved);
        }

        if (!soleAdministrator && by is not null && by == SubmittedBy)
        {
            return Result.Failure(MeasurementErrors.SubmitterCannotPublish);
        }

        // Approval is recorded on the version in review rather than being a status of its own: the lifecycle in
        // docs/prd/measurement-templates.md has four states, and who approved and when is a fact about the review,
        // not a fifth place the version can sit.
        ApprovedAt = now;
        ApprovedBy = by;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Makes this the version measurements are captured against.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Success, or the reason it could not be published.</returns>
    public Result Publish(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != TemplateStatus.InReview || ApprovedAt is null)
        {
            return Result.Failure(MeasurementErrors.VersionNotPublishable);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = TemplateStatus.Published;
        PublishedAt = now;
        PublishedBy = by;
        PublishReason = reason.Trim();
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Stops new captures against this version, leaving everything captured under it readable.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why.</param>
    /// <returns>Success, or the reason it could not be retired.</returns>
    public Result Retire(DateTimeOffset now, Guid? by, string reason)
    {
        if (Status != TemplateStatus.Published)
        {
            return Result.Failure(MeasurementErrors.VersionNotRetirable);
        }

        var reasoned = CheckReason(reason);

        if (reasoned.IsFailure)
        {
            return reasoned;
        }

        Status = TemplateStatus.Retired;
        RetiredAt = now;
        RetiredBy = by;
        RetiredReason = reason.Trim();
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>The fields grouped and ordered as the wizard and the printed sheet show them.</summary>
    /// <returns>The groups, in order, each holding its fields in order.</returns>
    public IReadOnlyList<IGrouping<string, TemplateField>> InGroupOrder()
        =>
        [
            .. _fields
                .OrderBy(field => field.DisplayOrder)
                .ThenBy(field => field.Key.Value, StringComparer.Ordinal)
                .GroupBy(field => field.GroupName, StringComparer.Ordinal),
        ];

    /// <summary>Rebuilds the definition of an existing field, for cloning.</summary>
    /// <param name="field">The field to describe.</param>
    /// <returns>Its definition.</returns>
    public static TemplateFieldDefinition DefinitionOf(TemplateField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new TemplateFieldDefinition(
            field.Key.Value,
            field.Label,
            field.LabelTamil,
            field.GroupName,
            field.DisplayOrder,
            field.CanonicalUnit,
            field.Precision,
            field.Bands,
            field.IsRequired,
            field.HelpText,
            field.DiagramKey,
            field.DiagramMediaId,
            field.DiagramAlt,
            field.Rule,
            [.. field.Options]);
    }

    private static Result CheckName(string name, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(MeasurementErrors.Required("name"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure(MeasurementErrors.TooLong("name", MaximumNameLength));
        }

        return notes is { Length: > MaximumNotesLength }
            ? Result.Failure(MeasurementErrors.TooLong("notes", MaximumNotesLength))
            : Result.Success();
    }

    private static Result CheckReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(MeasurementErrors.Required("reason"));
        }

        return reason.Length > MaximumReasonLength
            ? Result.Failure(MeasurementErrors.TooLong("reason", MaximumReasonLength))
            : Result.Success();
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
