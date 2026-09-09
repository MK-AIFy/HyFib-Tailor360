using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// The field set for one kind of garment, across every version it has ever had.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The template is the aggregate, not the version.</strong> "At most one version is published at a time"
/// is a rule about the versions together, so the thing that has to be internally consistent is the whole set of
/// them. Making the version the aggregate would put that rule across a boundary where nothing could enforce it,
/// and publishing would become two writes that can disagree.
/// </para>
/// <para>
/// Publishing supersedes: the version being published retires the one it replaces, in the same transaction, so
/// there is never a moment with two published versions or none. Retiring the published version without a successor
/// is a separate, deliberate act — it stops new captures of this garment entirely, which is why it is refused while
/// a published catalogue version still points at it.
/// </para>
/// </remarks>
public sealed partial class MeasurementTemplate
{
    /// <summary>The shortest usable code.</summary>
    public const int MinimumCodeLength = 2;

    /// <summary>The longest code the column holds.</summary>
    public const int MaximumCodeLength = 40;

    /// <summary>The longest name the column holds.</summary>
    public const int MaximumNameLength = 120;

    /// <summary>The longest description the column holds.</summary>
    public const int MaximumDescriptionLength = 1000;

    private readonly List<TemplateVersion> _versions = [];

    private MeasurementTemplate()
    {
        Code = null!;
        Name = null!;
    }

    /// <summary>The template's row identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation that owns it.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The stable machine key, such as <c>MT_BLOUSE_PATTERN</c>.</summary>
    public string Code { get; private set; }

    /// <summary>What administrators read.</summary>
    public string Name { get; private set; }

    /// <summary>What this template is for.</summary>
    public string? Description { get; private set; }

    /// <summary>When the template was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Every version, in the order they were created.</summary>
    public IReadOnlyList<TemplateVersion> Versions => _versions;

    /// <summary>The version measurements are captured against, or null when none is published.</summary>
    public TemplateVersion? PublishedVersion
        => _versions.SingleOrDefault(version => version.Status == TemplateStatus.Published);

    /// <summary>The number the next version takes.</summary>
    public int NextVersionNumber => _versions.Count == 0 ? 1 : _versions.Max(version => version.VersionNumber) + 1;

    /// <summary>Creates a template with no versions.</summary>
    /// <param name="id">The row identity.</param>
    /// <param name="organisationId">The owning organisation.</param>
    /// <param name="code">The stable machine key.</param>
    /// <param name="name">What administrators read.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The template, or the reason it was refused.</returns>
    public static Result<MeasurementTemplate> Create(
        Guid id,
        Guid organisationId,
        string code,
        string name,
        string? description,
        DateTimeOffset now,
        Guid? by)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;

        if (trimmedCode.Length is < MinimumCodeLength or > MaximumCodeLength
            || !CodePattern().IsMatch(trimmedCode))
        {
            return Result.Failure<MeasurementTemplate>(MeasurementErrors.TemplateCodeMalformed(trimmedCode));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<MeasurementTemplate>(MeasurementErrors.Required("name"));
        }

        if (name.Length > MaximumNameLength)
        {
            return Result.Failure<MeasurementTemplate>(MeasurementErrors.TooLong("name", MaximumNameLength));
        }

        if (description is { Length: > MaximumDescriptionLength })
        {
            return Result.Failure<MeasurementTemplate>(
                MeasurementErrors.TooLong("description", MaximumDescriptionLength));
        }

        return Result.Success(new MeasurementTemplate
        {
            Id = id,
            OrganisationId = organisationId,
            Code = trimmedCode,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
            UpdatedBy = by,
        });
    }

    /// <summary>Starts a draft, empty or copied from an existing version.</summary>
    /// <param name="ids">The identifier generator.</param>
    /// <param name="name">What to call the draft.</param>
    /// <param name="notes">What is changing and why.</param>
    /// <param name="defaultDisplayUnit">The unit the wizard opens in.</param>
    /// <param name="cloneFromVersionId">The version to copy, or null for an empty draft.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public Result<TemplateVersion> StartDraft(
        IIdGenerator ids,
        string name,
        string? notes,
        DisplayUnit defaultDisplayUnit,
        Guid? cloneFromVersionId,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(ids);

        Result<TemplateVersion> draft;

        if (cloneFromVersionId is { } sourceId)
        {
            if (Find(sourceId) is not { } source)
            {
                return Result.Failure<TemplateVersion>(MeasurementErrors.VersionNotFound);
            }

            draft = source.CloneAsDraft(ids, NextVersionNumber, name, notes, now, by);
        }
        else
        {
            draft = TemplateVersion.CreateDraft(
                ids.NewId(), Id, OrganisationId, NextVersionNumber, name, notes, defaultDisplayUnit, now, by);
        }

        if (draft.IsFailure)
        {
            return draft;
        }

        _versions.Add(draft.Value);
        Touch(now, by);

        return draft;
    }

    /// <summary>One version of this template, or null.</summary>
    /// <param name="versionId">The version's row identity.</param>
    /// <returns>The version, or null when this template does not hold it.</returns>
    public TemplateVersion? Find(Guid versionId)
        => _versions.SingleOrDefault(version => version.Id == versionId);

    /// <summary>Publishes a version, retiring the one it supersedes in the same act.</summary>
    /// <param name="versionId">The version to publish.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The version it superseded, or null when this is the first; or the reason publication was refused.</returns>
    public Result<TemplateVersion?> PublishVersion(
        Guid versionId,
        DateTimeOffset now,
        Guid? by,
        string reason)
    {
        if (Find(versionId) is not { } version)
        {
            return Result.Failure<TemplateVersion?>(MeasurementErrors.VersionNotFound);
        }

        var outgoing = PublishedVersion;

        // Retire the outgoing version first. If publication is then refused the whole act is refused together,
        // because both happen inside one call and the caller commits them in one transaction.
        if (outgoing is not null && outgoing.Id != versionId)
        {
            var superseded = outgoing.Retire(
                now, by, $"Superseded by version {version.VersionNumber}: {reason}");

            if (superseded.IsFailure)
            {
                return Result.Failure<TemplateVersion?>(superseded.Error);
            }
        }

        var published = version.Publish(now, by, reason);

        if (published.IsFailure)
        {
            return Result.Failure<TemplateVersion?>(published.Error);
        }

        Touch(now, by);

        return Result.Success<TemplateVersion?>(outgoing?.Id == versionId ? null : outgoing);
    }

    /// <summary>Records that the template itself changed.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="by">The administrator.</param>
    public void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }

    // Upper snake case, as the seeded codes are: MT_BLOUSE_PATTERN, MT_SALWAR. The same shape as a catalogue code,
    // for the same reason — it is read aloud, typed into a support ticket and printed on a sheet.
    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
