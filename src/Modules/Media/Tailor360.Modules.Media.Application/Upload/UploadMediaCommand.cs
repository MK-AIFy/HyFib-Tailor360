using Tailor360.Modules.Media.Domain.Media;

namespace Tailor360.Modules.Media.Application.Upload;

/// <summary>What the caller asks for when uploading an image.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The branch the upload is made from.</param>
/// <param name="Purpose">What the image was captured or supplied for.</param>
/// <param name="CustomerId">The customer it is attached to, where it is attached to one.</param>
/// <param name="OrderId">The order it is attached to, where it is attached to one.</param>
/// <param name="JobId">The garment job it is attached to, where it is attached to one.</param>
/// <param name="Content">The uploaded bytes, read from its current position to the end.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="SizeBytes">The declared size, checked against what <paramref name="Content"/> actually holds.</param>
/// <param name="AltText">Alt text for the image. Required for <see cref="MediaPurpose.Diagram"/> and <see cref="MediaPurpose.Illustration"/>.</param>
/// <param name="UploadedBy">Who is uploading it.</param>
public sealed record UploadMediaCommand(
    Guid OrganisationId,
    Guid BranchId,
    MediaPurpose Purpose,
    Guid? CustomerId,
    Guid? OrderId,
    Guid? JobId,
    Stream Content,
    string ContentType,
    long SizeBytes,
    string? AltText,
    Guid UploadedBy);
