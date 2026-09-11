using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// A garment being measured. Work in progress, and never evidence of anything.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A draft accepts what a version would refuse.</strong> That is the design, not a gap: INV-MSR-04 puts
/// validation at confirmation because a half-measured garment is a normal state and refusing an implausible
/// shoulder while somebody is still holding the tape teaches them to type a plausible lie and fix it later. What a
/// draft does enforce is only what can never be meant — a value for a field the version does not have.
/// </para>
/// <para>
/// <strong>A draft is shared within a branch.</strong> Two people measuring one customer between them is ordinary
/// in a shop, so saving is per section against a row version rather than last-writer-wins
/// (<c>docs/architecture/invariants.md</c> section 4.3). Last-writer-wins here loses half a garment's
/// measurements and nobody finds out until the tailor does.
/// </para>
/// <para>
/// <strong>It is consumed exactly once</strong> (INV-MSR-02), and consumption is what makes a confirmation
/// retried after a lost answer reach the version it already made rather than make a second one. The guard is here
/// and repeated as a conditional update in the store, because a domain check alone loses the race it exists for.
/// </para>
/// <para>
/// <strong>It expires.</strong> Measurements go stale, and INV-MSR-07 makes an expired draft one of the few things
/// this system hard-deletes. A confirmed version never is.
/// </para>
/// </remarks>
public sealed class MeasurementDraft
{
    private readonly List<MeasurementValue> _values = [];

    private MeasurementDraft()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private MeasurementDraft(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        Guid templateId,
        Guid templateVersionId,
        Guid? reusedFromVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        TemplateId = templateId;
        TemplateVersionId = templateVersionId;
        ReusedFromVersionId = reusedFromVersionId;
        StartedAt = now;
        StartedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
        ExpiresAt = expiresAt;
    }

    /// <summary>Identity of this draft.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation it belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch measuring. Drafts are shared within it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The customer being measured.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The template being measured against.</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>
    /// The template version pinned when the draft started.
    /// </summary>
    /// <remarks>
    /// Pinned, not looked up: a template published mid-measurement would otherwise change what is being asked for
    /// while somebody is halfway down the list. The draft finishes against the version it began with.
    /// </remarks>
    public Guid TemplateVersionId { get; private set; }

    /// <summary>The version these values were pre-filled from, or null when measured fresh.</summary>
    public Guid? ReusedFromVersionId { get; private set; }

    /// <summary>When measuring began, in UTC.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Who began it.</summary>
    public Guid? StartedBy { get; private set; }

    /// <summary>When it was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it stops being work in progress, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it became a confirmed version, or null while it is still work in progress.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>What has been measured so far.</summary>
    public IReadOnlyList<MeasurementValue> Values => _values;

    /// <summary>Whether it may still be written to and confirmed.</summary>
    public bool IsOpen => ConsumedAt is null;

    /// <summary>Starts measuring a garment against a published template version.</summary>
    /// <param name="id">Identity of the draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch measuring.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="version">The published template version to measure against.</param>
    /// <param name="reusedFromVersionId">The version being reused, or null when measured fresh.</param>
    /// <param name="now">The clock.</param>
    /// <param name="lifetime">How long a draft stays work in progress.</param>
    /// <param name="by">Who started it.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<MeasurementDraft> Start(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        TemplateVersion version,
        Guid? reusedFromVersionId,
        DateTimeOffset now,
        TimeSpan lifetime,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(version);

        // A draft against a draft template would capture measurements nobody has approved the meaning of, and
        // against a retired one would capture against a version the shop has stopped using.
        if (version.Status != TemplateStatus.Published)
        {
            return Result.Failure<MeasurementDraft>(MeasurementErrors.TemplateVersionNotPublished);
        }

        return Result.Success(new MeasurementDraft(
            id,
            organisationId,
            branchId,
            customerId,
            version.MeasurementTemplateId,
            version.Id,
            reusedFromVersionId,
            now,
            now.Add(lifetime),
            by));
    }

    /// <summary>
    /// Replaces the values of one wizard step, leaving every other step alone.
    /// </summary>
    /// <remarks>
    /// Per section, because that is the unit a person finishes: they measure the bodice, look up, and somebody
    /// else is on the lower body. Replacing rather than merging within the section is what lets a value be
    /// cleared — a merge has no way to say "this one is no longer answered".
    /// </remarks>
    /// <param name="version">The template version this draft is pinned to.</param>
    /// <param name="groupName">The step being saved.</param>
    /// <param name="values">Everything measured in that step.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">Who saved it.</param>
    /// <returns>Success, or the reason the section was refused.</returns>
    public Result SaveSection(
        TemplateVersion version,
        string groupName,
        IReadOnlyCollection<MeasurementValue> values,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        if (!IsOpen)
        {
            return Result.Failure(MeasurementErrors.DraftAlreadyConfirmed);
        }

        var known = version.Fields.ToDictionary(field => field.Key.Value, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!seen.Add(value.Key.Value))
            {
                return Result.Failure(MeasurementErrors.DuplicateValue(value.Key.Value));
            }

            // The one thing a draft does refuse. A value for a field this version does not have cannot become
            // anything at confirmation, so accepting it would only postpone the same refusal to a worse moment.
            if (!known.TryGetValue(value.Key.Value, out var field))
            {
                return Result.Failure(MeasurementErrors.UnknownField(value.Key.Value));
            }

            if (!string.Equals(field.GroupName, groupName, StringComparison.Ordinal))
            {
                return Result.Failure(MeasurementErrors.FieldNotInSection(value.Key.Value, groupName));
            }
        }

        var inSection = version.Fields
            .Where(field => string.Equals(field.GroupName, groupName, StringComparison.Ordinal))
            .Select(field => field.Key.Value)
            .ToHashSet(StringComparer.Ordinal);

        _values.RemoveAll(held => inSection.Contains(held.Key.Value));
        _values.AddRange(values);

        UpdatedAt = now;
        UpdatedBy = by;

        return Result.Success();
    }

    /// <summary>Marks the draft as spent, so it can never become a second version.</summary>
    /// <param name="now">The clock.</param>
    /// <returns>Success, or the reason it could not be consumed.</returns>
    /// <remarks>
    /// The expiry is checked here rather than only by the retention job, because a draft that outlived its
    /// lifetime and has not yet been swept is still too old to vouch for.
    /// </remarks>
    public Result Consume(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(MeasurementErrors.DraftAlreadyConfirmed);
        }

        if (now >= ExpiresAt)
        {
            return Result.Failure(MeasurementErrors.DraftExpired);
        }

        ConsumedAt = now;

        return Result.Success();
    }
}
