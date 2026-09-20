using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Media.Application.Abstractions;
using Tailor360.Modules.Media.Domain;
using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Media.Application.Upload;

/// <summary>
/// Takes an uploaded file from the wire to a tracked, quarantined object. Everything after this —
/// decoding, validating, scanning, stripping, deriving and promoting — is issue #593's.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Validated before a byte reaches storage, where that is cheap to check.</strong> The size
/// cap is checked against the declared length before anything is read, so an oversized upload is
/// refused without buffering it. Everything <see cref="MediaObject.BeginUpload"/> can determine from
/// the declared metadata — content type, alt text, consent — is checked before the bytes are written
/// to the quarantine bucket, so a request that was always going to be refused never reaches storage at
/// all (ADR-0005 §4: the web host writes to quarantine, never decodes).
/// </para>
/// <para>
/// <strong>Consent is resolved, not required to already be known.</strong> A caller supplies a
/// customer id; this handler asks Customers whether that customer has granted <c>photo_capture</c>
/// consent and, if so, hands the record id it resolved to <see cref="MediaObject.BeginUpload"/>. If no
/// customer id was given, or consent was never granted, the id passed on is null — and the aggregate
/// itself is what actually refuses a <see cref="MediaPurpose.Material"/> or
/// <see cref="MediaPurpose.Reference"/> upload with none, which is why this method does not duplicate
/// that check.
/// </para>
/// </remarks>
/// <param name="store">Where the object and its quarantine entry are written.</param>
/// <param name="storage">Where the bytes are written.</param>
/// <param name="consent">Resolves the customer's standing consent, across the module boundary.</param>
/// <param name="ids">The identifier generator. Never <c>Guid.NewGuid()</c> (ARCH-015).</param>
/// <param name="clock">The clock. Never <c>DateTimeOffset.UtcNow</c> (ARCH-014).</param>
/// <param name="options">How large an upload this deployment accepts.</param>
public sealed class UploadMediaHandler(
    IMediaStore store,
    IObjectStorage storage,
    IConsentQuery consent,
    IIdGenerator ids,
    IClock clock,
    IOptions<MediaUploadOptions> options)
{
    /// <summary>Uploads a file, straight into <see cref="MediaStatus.Uploading"/>.</summary>
    /// <param name="command">What was uploaded and who is uploading it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tracked object, or the reason the upload was refused.</returns>
    public async Task<Result<MediaObject>> UploadAsync(
        UploadMediaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var maximumBytes = options.Value.MaximumSizeBytes;
        if (command.SizeBytes > maximumBytes)
        {
            return Result.Failure<MediaObject>(
                MediaErrors.SizeTooLarge(nameof(command.SizeBytes), maximumBytes));
        }

        Guid? consentRecordId = null;
        if (command.Purpose is MediaPurpose.Material or MediaPurpose.Reference
            && command.CustomerId is { } customerId)
        {
            var state = await consent
                .GetAsync(customerId, PublishedConsentPurposes.PhotoCapture, cancellationToken)
                .ConfigureAwait(false);
            if (state.IsGranted)
            {
                consentRecordId = state.RecordId;
            }
        }

        // Buffered rather than streamed straight through: the checksum has to be computed from the
        // whole file before IObjectStorage.PutAsync is called, and the size cap above already bounds
        // how much this ever holds in memory at once.
        var buffered = new MemoryStream();
        await using (buffered.ConfigureAwait(false))
        {
            await command.Content.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);

            if (buffered.Length != command.SizeBytes)
            {
                return Result.Failure<MediaObject>(MediaErrors.SizeMismatch(nameof(command.SizeBytes)));
            }

            buffered.Position = 0;
            var checksumBytes = await SHA256.HashDataAsync(buffered, cancellationToken)
                .ConfigureAwait(false);
            var checksum = Convert.ToHexStringLower(checksumBytes);

            var id = ids.NewId();
            var objectKey = $"quarantine/{id}";
            var now = clock.UtcNow;

            var uploadResult = MediaObject.BeginUpload(
                id,
                command.OrganisationId,
                command.BranchId,
                command.Purpose,
                command.CustomerId,
                command.OrderId,
                command.JobId,
                consentRecordId,
                objectKey,
                command.ContentType,
                command.SizeBytes,
                checksum,
                command.AltText,
                now,
                command.UploadedBy);

            if (uploadResult.IsFailure)
            {
                return uploadResult;
            }

            buffered.Position = 0;
            await storage.PutAsync(objectKey, buffered, command.ContentType, cancellationToken)
                .ConfigureAwait(false);

            store.Add(uploadResult.Value);
            store.Add(MediaQuarantineEntry.Create(id, objectKey, now));

            var saved = await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return saved.IsFailure ? Result.Failure<MediaObject>(saved.Error) : uploadResult;
        }
    }
}
