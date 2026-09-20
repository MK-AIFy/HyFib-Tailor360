using System.Globalization;
using Shouldly;
using Tailor360.Modules.Media.Domain.Media;

namespace Tailor360.UnitTests.Media;

/// <summary>
/// <see cref="MediaObject.BeginUpload"/>: what it lets a caller record, and when it refuses.
/// </summary>
/// <remarks>
/// This issue (#592) only ever produces a row in <see cref="MediaStatus.Uploading"/>, so there is
/// nothing here about promotion, rejection or the worker's pipeline — those transitions do not exist
/// on the type yet, and #593 is where their own tests belong.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MediaObjectTests
{
    private static readonly Guid Organisation = Guid.Parse("0199c2f0-0000-7000-8000-000000000001");
    private static readonly Guid Branch = Guid.Parse("0199c2f0-0000-7000-8000-000000000002");
    private static readonly Guid Customer = Guid.Parse("0199c2f0-0000-7000-8000-000000000003");
    private static readonly Guid Consent = Guid.Parse("0199c2f0-0000-7000-8000-000000000004");
    private static readonly Guid UploadedBy = Guid.Parse("0199c2f0-0000-7000-8000-000000000005");
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-20T06:00:00+00:00", CultureInfo.InvariantCulture);
    private const string ValidChecksum =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void StartsInUploadingWithTheFieldsItWasGiven()
    {
        var result = BeginUpload(purpose: MediaPurpose.Reference, consentRecordId: Consent);

        result.IsSuccess.ShouldBeTrue();
        var media = result.Value;
        media.Status.ShouldBe(MediaStatus.Uploading);
        media.OrganisationId.ShouldBe(Organisation);
        media.BranchId.ShouldBe(Branch);
        media.CustomerId.ShouldBe(Customer);
        media.ConsentRecordId.ShouldBe(Consent);
        media.ObjectKey.ShouldBe("quarantine/test-key");
        media.ContentType.ShouldBe("image/jpeg");
        media.SizeBytes.ShouldBe(1024L);
        media.Checksum.ShouldBe(ValidChecksum);
        media.CreatedAt.ShouldBe(Now);
        media.CreatedBy.ShouldBe(UploadedBy);
    }

    [Theory]
    [InlineData(MediaPurpose.Material)]
    [InlineData(MediaPurpose.Reference)]
    [InlineData(MediaPurpose.QcEvidence)]
    [InlineData(MediaPurpose.DeliveryEvidence)]
    public void ClassifiesPhotographicPurposesAsSensitivePersonal(MediaPurpose purpose)
    {
        // Never guessed down to Personal: whether a person appears in the frame is not something the
        // system can tell from the bytes, so every purpose that could show one is classified at the
        // strictest value the class can take (data-classification.md §2.1, §5.5, §5.6).
        var result = BeginUpload(purpose, consentRecordId: purpose is MediaPurpose.Material or MediaPurpose.Reference ? Consent : null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Classification.ShouldBe(MediaClassification.SensitivePersonal);
    }

    [Theory]
    [InlineData(MediaPurpose.Diagram)]
    [InlineData(MediaPurpose.Illustration)]
    public void ClassifiesBundledArtworkAsPublic(MediaPurpose purpose)
    {
        var result = BeginUpload(purpose, altText: "A line drawing.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Classification.ShouldBe(MediaClassification.Public);
    }

    [Fact]
    public void RefusesAContentTypeThisModuleDoesNotAccept()
    {
        var result = BeginUpload(MediaPurpose.Reference, contentType: "application/pdf", consentRecordId: Consent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.content-type-not-allowed");
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/heic")]
    public void AcceptsEveryDeclaredContentType(string contentType)
    {
        var result = BeginUpload(MediaPurpose.Reference, contentType: contentType, consentRecordId: Consent);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesASizeThatIsNotPositive(long size)
    {
        var result = BeginUpload(MediaPurpose.Reference, sizeBytes: size, consentRecordId: Consent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.size-not-positive");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex-at-all")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b8")] // 63 characters
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B8")] // upper case
    public void RefusesAChecksumThatIsNotAWellFormedSha256Digest(string checksum)
    {
        var result = BeginUpload(MediaPurpose.Reference, checksum: checksum, consentRecordId: Consent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.checksum-not-well-formed");
    }

    [Theory]
    [InlineData(MediaPurpose.Diagram)]
    [InlineData(MediaPurpose.Illustration)]
    public void RefusesADiagramOrIllustrationWithNoAltText(MediaPurpose purpose)
    {
        var result = BeginUpload(purpose, altText: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.alt-text-required-for-diagram");
    }

    [Theory]
    [InlineData(MediaPurpose.Material)]
    [InlineData(MediaPurpose.Reference)]
    public void RefusesMaterialOrReferenceWithNoConsentRecord(MediaPurpose purpose)
    {
        var result = BeginUpload(purpose, consentRecordId: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.consent-required");
    }

    [Theory]
    [InlineData(MediaPurpose.QcEvidence)]
    [InlineData(MediaPurpose.DeliveryEvidence)]
    [InlineData(MediaPurpose.Diagram)]
    public void DoesNotRequireConsentForPurposesThatAreNotMaterialOrReference(MediaPurpose purpose)
    {
        var altText = purpose == MediaPurpose.Diagram ? "A line drawing." : null;

        var result = BeginUpload(purpose, consentRecordId: null, altText: altText);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RefusesAnObjectKeyThatIsBlank()
    {
        var result = BeginUpload(MediaPurpose.Reference, objectKey: "  ", consentRecordId: Consent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.value-required");
    }

    [Fact]
    public void RefusesAnObjectKeyLongerThanTheColumnHolds()
    {
        var result = BeginUpload(
            MediaPurpose.Reference,
            objectKey: "quarantine/" + new string('a', MediaObject.MaximumObjectKeyLength),
            consentRecordId: Consent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("media.value-too-long");
    }

    private static Tailor360.Platform.Abstractions.Results.Result<MediaObject> BeginUpload(
        MediaPurpose purpose,
        Guid? consentRecordId = null,
        string? altText = null,
        string objectKey = "quarantine/test-key",
        string contentType = "image/jpeg",
        long sizeBytes = 1024,
        string checksum = ValidChecksum)
        => MediaObject.BeginUpload(
            Guid.Parse("0199c2f0-0000-7000-8000-0000000000ee"),
            Organisation,
            Branch,
            purpose,
            Customer,
            orderId: null,
            jobId: null,
            consentRecordId,
            objectKey,
            contentType,
            sizeBytes,
            checksum,
            altText,
            Now,
            UploadedBy);
}
