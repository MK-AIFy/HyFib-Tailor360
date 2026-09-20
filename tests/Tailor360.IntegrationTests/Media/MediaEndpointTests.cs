using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.IntegrationTests.Customers;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Modules.Media.Application.Upload;
using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Modules.Media.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Media;

/// <summary>
/// Uploading a file over <c>POST /api/v1/media</c>: what lands in quarantine, what is refused before a
/// byte reaches storage, and what a retried request does.
/// </summary>
/// <remarks>
/// <para>
/// These run against a real PostgreSQL and a real MinIO because what is under test is the composition —
/// the size cap read from configuration, the cross-module consent resolution against Customers, the
/// checksum actually matching what a caller sent, and the bytes actually landing in the quarantine
/// bucket — and any one of those could be right in isolation while the request still did the wrong
/// thing. <see cref="Tailor360.UnitTests.Media.MediaObjectTests"/> already owns every permutation of
/// <see cref="MediaObject.BeginUpload"/>'s own validation, so this file does not repeat them.
/// </para>
/// <para>
/// No "other branch" refusal here: <c>BranchScope.CurrentBranch</c> is satisfied by the caller's own
/// current branch (<c>BranchScopeAuthorisationHandler</c>), and this endpoint creates a new object
/// tagged with that branch rather than reading or writing one an id in the route already names — there
/// is no route parameter for a resource scope to be about (ARCH-023 only applies to one), and no
/// reachable way to place a properly-provisioned caller's session branch outside their own reach.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class MediaEndpointTests(WebApplicationFixture fixture)
{
    private const string UploadPath = "/api/v1/media";

    [Fact]
    public async Task UploadsAReferenceImageIntoQuarantineAndWritesTheBytesToStorage()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var uploader = await UploaderAsync("media-upload", "203.0.113.42");
        var customerId = await CustomerWithGrantedConsentAsync(uploader);
        var bytes = RandomBytes(2048);

        var response = await uploader.PostMultipartAsync(
            UploadPath, UploadForm(bytes, "Reference", customerId: customerId), Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var payload = await AuthenticationClient.ReadAsync<MediaBody>(response);
        payload.ShouldNotBeNull();
        payload.Id.ShouldNotBe(Guid.Empty);
        payload.Purpose.ShouldBe("Reference");
        payload.Status.ShouldBe("Uploading");
        payload.ContentType.ShouldBe("image/jpeg");
        payload.SizeBytes.ShouldBe(bytes.LongLength);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var stored = await context.MediaObjects.AsNoTracking()
            .SingleAsync(media => media.Id == payload.Id, Token);

        stored.CustomerId.ShouldBe(customerId);
        stored.ConsentRecordId.ShouldNotBeNull();
        stored.Status.ShouldBe(MediaStatus.Uploading);
        stored.ObjectKey.ShouldBe($"quarantine/{payload.Id}");
        stored.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));

        var quarantine = await context.QuarantineEntries.AsNoTracking()
            .SingleAsync(entry => entry.MediaObjectId == payload.Id, Token);
        quarantine.AttemptCount.ShouldBe(0);
        quarantine.ScanOutcome.ShouldBeNull();

        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await using var written = (await storage.OpenReadAsync(stored.ObjectKey, Token)).ShouldNotBeNull();
        using var writtenBuffer = new MemoryStream();
        await written.CopyToAsync(writtenBuffer, Token);
        writtenBuffer.ToArray().ShouldBe(bytes, "the bytes a caller sent are the bytes the quarantine bucket holds");
    }

    [Fact]
    public async Task RefusesADisallowedContentTypeBeforeAnythingIsWritten()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var uploader = await UploaderAsync("media-badtype", "203.0.113.43");
        var customerId = await CustomerWithGrantedConsentAsync(uploader);

        var response = await uploader.PostMultipartAsync(
            UploadPath,
            UploadForm(RandomBytes(64), "Reference", contentType: "application/pdf", customerId: customerId),
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("media.content-type-not-allowed");
    }

    /// <summary>
    /// The cap actually read from <c>Media:Upload:MaximumSizeBytes</c>, not a number this test invents —
    /// so it stays true if a deployment ever raises or lowers it.
    /// </summary>
    [Fact]
    public async Task RefusesAFileLargerThanTheConfiguredCap()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var uploader = await UploaderAsync("media-toobig", "203.0.113.44");

        using var scope = fixture.Services.CreateScope();
        var cap = scope.ServiceProvider.GetRequiredService<IOptions<MediaUploadOptions>>().Value.MaximumSizeBytes;
        var oversized = RandomBytes(checked((int)cap + 1));

        var response = await uploader.PostMultipartAsync(
            UploadPath,
            UploadForm(oversized, "Diagram", altText: "A line drawing."),
            Key());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("media.size-too-large");
    }

    [Fact]
    public async Task RefusesAMaterialUploadWithNoConsentOnRecord()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var uploader = await UploaderAsync("media-noconsent", "203.0.113.45");
        var customerId = await CustomerHarness.CustomerAsync(fixture, SessionTestData.HomeBranchId);

        var response = await uploader.PostMultipartAsync(
            UploadPath, UploadForm(RandomBytes(64), "Material", customerId: customerId), Key());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("media.consent-required");
    }

    [Fact]
    public async Task IdempotencyKeyReplayReturnsTheFirstOutcomeRatherThanASecondObject()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var uploader = await UploaderAsync("media-replay", "203.0.113.46");
        var customerId = await CustomerWithGrantedConsentAsync(uploader);
        var bytes = RandomBytes(512);
        var headers = new (string Name, string Value)[] { ("Idempotency-Key", Guid.CreateVersion7().ToString()) };

        // A transport retry resends the exact same request bytes. Building UploadForm(...) twice would
        // not do that — each MultipartFormDataContent draws its own random boundary, so two logically
        // identical forms serialise to two different bodies, which RequireIdempotency correctly treats
        // as "same key, different body" rather than a replay. Capturing the body once and resending that
        // snapshot is what makes this test the retry it claims to be.
        using var form = UploadForm(bytes, "Reference", customerId: customerId);
        var rawBody = await form.ReadAsByteArrayAsync(Token);
        var contentType = form.Headers.ContentType;

        var first = await uploader.PostMultipartAsync(UploadPath, Snapshot(rawBody, contentType), headers);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var firstPayload = await AuthenticationClient.ReadAsync<MediaBody>(first);
        firstPayload.ShouldNotBeNull();
        var firstBody = await first.Content.ReadAsStringAsync(Token);

        var replay = await uploader.PostMultipartAsync(UploadPath, Snapshot(rawBody, contentType), headers);
        replay.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await replay.Content.ReadAsStringAsync(Token)).ShouldBe(firstBody);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        (await context.MediaObjects.CountAsync(media => media.Id == firstPayload.Id, Token)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesTheUploadRouteToACallerHoldingNothing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var nobody = await AdministrationHarness.AdministratorAsync(
            fixture, "media-none", "203.0.113.47", grantPermission: null);

        var response = await nobody.PostMultipartAsync(
            UploadPath, UploadForm(RandomBytes(64), "Diagram", altText: "A line drawing."), Key());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static (string Name, string Value)[] Key() => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private static MultipartFormDataContent UploadForm(
        byte[] bytes,
        string purpose,
        string contentType = "image/jpeg",
        Guid? customerId = null,
        string? altText = null)
    {
        var content = new MultipartFormDataContent { { new StringContent(purpose), "purpose" } };

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", "upload.bin");

        if (customerId is { } id)
        {
            content.Add(new StringContent(id.ToString()), "customerId");
        }

        if (altText is not null)
        {
            content.Add(new StringContent(altText), "altText");
        }

        return content;
    }

    private static ByteArrayContent Snapshot(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = contentType;
        return content;
    }

    private Task<AdministrationHarness.AdministratorClient> UploaderAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture,
            prefix,
            address,
            MediaPermissions.Upload,
            CustomersPermissions.Create,
            CustomersPermissions.Update,
            CustomersPermissions.ReadContact,
            CustomersPermissions.ReadConsent);

    /// <summary>
    /// A customer with a granted <c>photo_capture</c> consent record, through the real Customers
    /// endpoint — the point of this test file is that Media's cross-module resolution against a
    /// genuinely recorded answer works, not that a plausible-looking identifier was invented.
    /// </summary>
    private async Task<Guid> CustomerWithGrantedConsentAsync(AdministrationHarness.AdministratorClient uploader)
    {
        await EnsurePhotoCaptureWordingAsync();
        var customerId = await CustomerHarness.CustomerAsync(fixture, SessionTestData.HomeBranchId);

        (await uploader.PostAsync(
                $"/api/v1/customers/{customerId}/consent",
                new
                {
                    purposeKey = PublishedConsentPurposes.PhotoCapture,
                    decision = "Granted",
                    source = "counter, verbal",
                },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return customerId;
    }

    /// <summary>
    /// Serialises <see cref="EnsurePhotoCaptureWordingAsync"/>'s check-then-write across this class's own
    /// tests. They share one xUnit collection, but the platform still runs several of this class's own
    /// <c>[Fact]</c>s concurrently — proven the hard way, by <c>ux_consent_wordings_purpose_version</c>
    /// firing when three tests each read "no wording yet" before any of them had committed one.
    /// </summary>
    private static readonly SemaphoreSlim WordingLock = new(1, 1);

    /// <summary>
    /// The <c>photo_capture</c> purpose is one of the five <c>init-reference-data</c> seeds, but that
    /// command deliberately publishes no wording (DC-01: it never invents the words a customer is read).
    /// Without one, an answer cannot be recorded at all — so this test file publishes a synthetic wording
    /// once, guarded by <see cref="WordingLock"/> rather than assumed safe by test ordering.
    /// </summary>
    private async Task EnsurePhotoCaptureWordingAsync()
    {
        await WordingLock.WaitAsync(Token);
        try
        {
            using var scope = fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

            // CurrentWordingVersion reads the private _wordings backing field, so an existing purpose
            // fetched without its Wordings navigation always reports zero — never omit this Include, or
            // every call after the first "publishes" a second, colliding version 1.
            var purpose = await context.ConsentPurposes.Include(candidate => candidate.Wordings)
                .SingleOrDefaultAsync(
                    candidate => candidate.OrganisationId == SessionTestData.OrganisationId
                        && candidate.Key == PublishedConsentPurposes.PhotoCapture,
                    Token);

            if (purpose is null)
            {
                purpose = ConsentPurpose.Define(
                    ids.NewId(),
                    SessionTestData.OrganisationId,
                    PublishedConsentPurposes.PhotoCapture,
                    "Photo capture",
                    "Seeded by the Media integration tests so a Reference or Material upload has a real "
                    + "consent record to resolve.",
                    clock.UtcNow).Value;

                context.ConsentPurposes.Add(purpose);
            }

            if (purpose.CurrentWordingVersion == 0)
            {
                purpose.PublishWording(ids.NewId(), "Synthetic wording for an integration test.", clock.UtcNow)
                    .IsSuccess.ShouldBeTrue();
            }

            await context.SaveChangesAsync(Token);
        }
        finally
        {
            WordingLock.Release();
        }
    }

    private sealed record MediaBody(Guid Id, string Purpose, string Status, string ContentType, long SizeBytes);
}
