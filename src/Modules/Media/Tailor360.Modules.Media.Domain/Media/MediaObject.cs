using System.Text.RegularExpressions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// One stored image, from the moment its bytes reach quarantine to the moment it is tombstoned.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The object is the aggregate; the module owns nothing above it.</strong> Unlike
/// <c>CatalogVersion</c>, there is no larger structure a media object belongs to — each upload stands
/// on its own, identified once and referred to by that one id for its whole life
/// (<c>docs/architecture/module-ownership.md</c> §5.4).
/// </para>
/// <para>
/// <strong>Classification is derived, never supplied.</strong> Whether a photograph shows a person is
/// not something bytes on a wire can answer, so <see cref="MediaClassification"/> is fixed by
/// <see cref="Purpose"/> at the strictest value the class can take rather than guessed down
/// (<c>docs/nfr/data-classification.md</c> §2.1 "the strictest wins", §5.5, §5.6).
/// </para>
/// <para>
/// <strong>This issue (#592) only ever creates a row in <see cref="MediaStatus.Uploading"/>.</strong>
/// Every later transition — <see cref="MediaStatus.Quarantined"/> through the worker picking the
/// object up, <see cref="MediaStatus.Ready"/> on promotion, <see cref="MediaStatus.Rejected"/> on a
/// failed scan or validation — belongs to issue #593, which is why those transition methods are not
/// on this type yet: adding a method nothing calls is exactly the kind of premature surface CLAUDE.md
/// asks not to build.
/// </para>
/// </remarks>
public sealed class MediaObject
{
    /// <summary>The longest object key the column holds.</summary>
    public const int MaximumObjectKeyLength = 500;

    /// <summary>The longest content type the column holds.</summary>
    public const int MaximumContentTypeLength = 100;

    /// <summary>The exact length of a SHA-256 hex digest.</summary>
    public const int ChecksumLength = 64;

    /// <summary>The longest alt text the column holds.</summary>
    public const int MaximumAltTextLength = 500;

    private static readonly Regex ChecksumPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private MediaObject()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private MediaObject(
        Guid id,
        Guid organisationId,
        Guid branchId,
        MediaPurpose purpose,
        MediaClassification classification,
        Guid? customerId,
        Guid? orderId,
        Guid? jobId,
        Guid? consentRecordId,
        string objectKey,
        string contentType,
        long sizeBytes,
        string checksum,
        string? altText,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        Purpose = purpose;
        Classification = classification;
        CustomerId = customerId;
        OrderId = orderId;
        JobId = jobId;
        ConsentRecordId = consentRecordId;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Checksum = checksum;
        AltText = altText;
        Status = MediaStatus.Uploading;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
    }

    /// <summary>Identity of the object. A UUIDv7, and what every reference — a job, a draft, a card — names.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the object belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch that captured or holds it.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>What the image was captured or supplied for.</summary>
    public MediaPurpose Purpose { get; private set; }

    /// <summary>The strictest data class the object's bytes carry, derived from <see cref="Purpose"/>.</summary>
    public MediaClassification Classification { get; private set; }

    /// <summary>The customer the object is attached to, where it is attached to one.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The order the object is attached to, where it is attached to one.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>The garment job the object is attached to, where it is attached to one.</summary>
    public Guid? JobId { get; private set; }

    /// <summary>
    /// The <c>photo_capture</c> consent record this object was captured under, resolved from Customers
    /// and stored — never copied — against the object it permitted (<c>docs/nfr/data-classification.md</c>
    /// §5.5 "Lawful basis or consent"). Set for <see cref="MediaPurpose.Material"/> and
    /// <see cref="MediaPurpose.Reference"/> only.
    /// </summary>
    public Guid? ConsentRecordId { get; private set; }

    /// <summary>
    /// The object's current storage key: the quarantine key while <see cref="MediaStatus.Uploading"/>
    /// or <see cref="MediaStatus.Quarantined"/>, replaced with the ready-prefix key on promotion
    /// (issue #593). Opaque and random — never a display number, a name or anything personal
    /// (<c>docs/architecture/conventions.md</c> §3.5, CLAUDE.md §4 rule 8).
    /// </summary>
    public string ObjectKey { get; private set; } = string.Empty;

    /// <summary>The declared content type at upload. Issue #593 re-checks it against the decoded bytes.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>The size of the uploaded bytes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The SHA-256 checksum of the uploaded bytes, as a lower-case hex digest.</summary>
    public string Checksum { get; private set; } = string.Empty;

    /// <summary>
    /// Describes the image in words. Required for <see cref="MediaPurpose.Diagram"/> and
    /// <see cref="MediaPurpose.Illustration"/> so the picker and job card are usable with a screen
    /// reader and on a printed sheet; optional otherwise.
    /// </summary>
    public string? AltText { get; private set; }

    /// <summary>Where the object is in the upload pipeline.</summary>
    public MediaStatus Status { get; private set; }

    /// <summary>
    /// When the object may be deleted by the retention job, or null while that is not yet decided
    /// (issue #192; the period itself is <strong>OD-08</strong>, an open decision this issue does not
    /// invent a number for).
    /// </summary>
    public DateOnly? RetentionDate { get; private set; }

    /// <summary>When the upload started.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who uploaded it.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When the row was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>
    /// Starts tracking an uploaded file, straight into <see cref="MediaStatus.Uploading"/>. The caller
    /// has already written the bytes to the quarantine bucket under <paramref name="objectKey"/> —
    /// this call records that it happened, nothing more; issue #593 is what looks at the bytes.
    /// </summary>
    /// <param name="id">A fresh id from <c>IIdGenerator</c>. Never <c>Guid.NewGuid()</c> (ARCH-015).</param>
    /// <param name="organisationId">The organisation the object belongs to.</param>
    /// <param name="branchId">The branch that captured or holds it.</param>
    /// <param name="purpose">What the image was captured or supplied for.</param>
    /// <param name="customerId">The customer it is attached to, where it is attached to one.</param>
    /// <param name="orderId">The order it is attached to, where it is attached to one.</param>
    /// <param name="jobId">The garment job it is attached to, where it is attached to one.</param>
    /// <param name="consentRecordId">
    /// The granted <c>photo_capture</c> consent record's id, already resolved by the caller through
    /// <c>IConsentQuery</c>. Required for <see cref="MediaPurpose.Material"/> and
    /// <see cref="MediaPurpose.Reference"/>; this factory refuses to create either without one, because
    /// the aggregate is the last place that can guarantee the rule holds regardless of caller
    /// discipline.
    /// </param>
    /// <param name="objectKey">The random quarantine-bucket key the bytes were written under.</param>
    /// <param name="contentType">The declared content type. Must be one <see cref="MediaContentTypes"/> accepts.</param>
    /// <param name="sizeBytes">The size of the uploaded bytes.</param>
    /// <param name="checksum">The SHA-256 checksum of the uploaded bytes, as 64 lower-case hex characters.</param>
    /// <param name="altText">
    /// Alt text for the image. Required for <see cref="MediaPurpose.Diagram"/> and
    /// <see cref="MediaPurpose.Illustration"/>.
    /// </param>
    /// <param name="now">The current instant.</param>
    /// <param name="by">Who uploaded it.</param>
    /// <returns>The tracked object, or the reason the upload could not be recorded.</returns>
    public static Result<MediaObject> BeginUpload(
        Guid id,
        Guid organisationId,
        Guid branchId,
        MediaPurpose purpose,
        Guid? customerId,
        Guid? orderId,
        Guid? jobId,
        Guid? consentRecordId,
        string objectKey,
        string contentType,
        long sizeBytes,
        string checksum,
        string? altText,
        DateTimeOffset now,
        Guid? by)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Result.Failure<MediaObject>(MediaErrors.Required(nameof(objectKey)));
        }

        if (objectKey.Length > MaximumObjectKeyLength)
        {
            return Result.Failure<MediaObject>(
                MediaErrors.TooLong(nameof(objectKey), MaximumObjectKeyLength));
        }

        if (!MediaContentTypes.IsAllowed(contentType))
        {
            return Result.Failure<MediaObject>(MediaErrors.ContentTypeNotAllowed(nameof(contentType)));
        }

        if (sizeBytes <= 0)
        {
            return Result.Failure<MediaObject>(MediaErrors.SizeNotPositive(nameof(sizeBytes)));
        }

        if (checksum is null || !ChecksumPattern.IsMatch(checksum))
        {
            return Result.Failure<MediaObject>(MediaErrors.ChecksumNotWellFormed(nameof(checksum)));
        }

        var classification = ClassificationFor(purpose);

        var altTextRequired = purpose is MediaPurpose.Diagram or MediaPurpose.Illustration;
        if (altTextRequired && string.IsNullOrWhiteSpace(altText))
        {
            return Result.Failure<MediaObject>(MediaErrors.AltTextRequiredForDiagram(nameof(altText)));
        }

        if (altText is { Length: > MaximumAltTextLength })
        {
            return Result.Failure<MediaObject>(MediaErrors.TooLong(nameof(altText), MaximumAltTextLength));
        }

        var consentRequired = purpose is MediaPurpose.Material or MediaPurpose.Reference;
        if (consentRequired && consentRecordId is null)
        {
            return Result.Failure<MediaObject>(MediaErrors.ConsentRequired(nameof(consentRecordId)));
        }

        return Result.Success(new MediaObject(
            id,
            organisationId,
            branchId,
            purpose,
            classification,
            customerId,
            orderId,
            jobId,
            consentRecordId,
            objectKey,
            contentType,
            sizeBytes,
            checksum,
            altText,
            now,
            by));
    }

    /// <summary>
    /// The strictest class a purpose's bytes can carry. See <see cref="MediaClassification"/>'s own
    /// remarks for why Material, Reference, QC and delivery evidence are never guessed down to
    /// Personal.
    /// </summary>
    private static MediaClassification ClassificationFor(MediaPurpose purpose) => purpose switch
    {
        MediaPurpose.Diagram or MediaPurpose.Illustration => MediaClassification.Public,
        MediaPurpose.Material or MediaPurpose.Reference
            or MediaPurpose.QcEvidence or MediaPurpose.DeliveryEvidence
            => MediaClassification.SensitivePersonal,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unrecognised media purpose."),
    };
}
