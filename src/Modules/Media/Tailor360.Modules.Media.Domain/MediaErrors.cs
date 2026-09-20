using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Media.Domain;

/// <summary>
/// Every failure the Media module's domain reports, in one place — the same convention
/// <c>CatalogErrors</c> follows, and for the same reason: a reader can see at a glance that no two
/// failures share a code.
/// </summary>
/// <remarks>
/// No message here ever names a customer, a filename or a byte of image content
/// (<c>docs/nfr/data-classification.md</c> §5.5 "In logs" — never).
/// </remarks>
public static class MediaErrors
{
    /// <summary>A required value was missing or blank.</summary>
    public static Error Required(string field) => Error.Validation(
        "media.value-required",
        "A required value was not supplied.",
        field);

    /// <summary>A value was longer than the column that holds it.</summary>
    public static Error TooLong(string field, int maximum) => Error.Validation(
        "media.value-too-long",
        $"That value is longer than the {maximum} characters this field holds.",
        field);

    /// <summary>The declared content type is not one this module accepts.</summary>
    public static Error ContentTypeNotAllowed(string field) => Error.Validation(
        "media.content-type-not-allowed",
        "That file type is not accepted. Upload a JPEG, PNG, WebP or HEIC image.",
        field);

    /// <summary>The size was zero, negative, or otherwise not a byte count a real upload can have.</summary>
    public static Error SizeNotPositive(string field) => Error.Validation(
        "media.size-not-positive",
        "The uploaded file has no content.",
        field);

    /// <summary>The checksum is not a well-formed SHA-256 hex digest.</summary>
    public static Error ChecksumNotWellFormed(string field) => Error.Validation(
        "media.checksum-not-well-formed",
        "The checksum is not a 64-character hexadecimal SHA-256 digest.",
        field);

    /// <summary>A diagram or illustration was supplied with no alt text.</summary>
    public static Error AltTextRequiredForDiagram(string field) => Error.Validation(
        "media.alt-text-required-for-diagram",
        "A diagram or illustration needs alt text so it is usable with a screen reader and on a printed sheet.",
        field);

    /// <summary>A material or reference image was supplied with no consent record.</summary>
    public static Error ConsentRequired(string field) => Error.Validation(
        "media.consent-required",
        "The customer has not granted photo-capture consent.",
        field);

    /// <summary>The upload was larger than this deployment accepts.</summary>
    public static Error SizeTooLarge(string field, long maximumBytes) => Error.Validation(
        "media.size-too-large",
        $"That file is larger than the {maximumBytes / (1024 * 1024)} MB this deployment accepts.",
        field);

    /// <summary>The bytes actually received did not match the declared size.</summary>
    public static Error SizeMismatch(string field) => Error.Validation(
        "media.size-mismatch",
        "The uploaded bytes did not match the declared size.",
        field);
}
