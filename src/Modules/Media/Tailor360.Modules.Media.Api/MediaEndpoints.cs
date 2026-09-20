using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Tailor360.Modules.Media.Application.Upload;
using Tailor360.Modules.Media.Domain;
using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Media.Api;

/// <summary>
/// The Media module's HTTP surface, covering the upload pipeline, object storage, authorised delivery and retention of images.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class MediaEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/media";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Media";

    /// <summary>The audit action a successful upload is recorded under.</summary>
    public const string UploadedAction = "media.upload";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var media = endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag);

        media.MapPost("/", UploadAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<MediaObjectPayload>(StatusCodes.Status202Accepted)
            .WithName("UploadMedia")
            .WithSummary("Upload an image into quarantine.")
            .WithDescription(
                "Accepts the raw bytes and writes them straight to the quarantine bucket, undecoded "
                + "(ADR-0005 §4). 202, not 201: validation, the malware scan, metadata strip, "
                + "derivative generation and promotion to Ready all happen asynchronously in the "
                + "worker (issue #593) — nothing here is a finished, resolvable object yet.")
            .RequirePermission(MediaPermissions.Upload, BranchScope.CurrentBranch)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(UploadedAction)
            .RequireIdempotency()
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>
    /// Reads the multipart form directly rather than binding an <c>IFormFile</c> parameter, because the
    /// upload also carries plain fields (purpose, the links, alt text) that a single bound type would
    /// otherwise have to model as a second thing entirely.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpContext context,
        UploadMediaHandler handler,
        ICurrentUser caller,
        CancellationToken cancellationToken)
    {
        var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Problems.From(MediaErrors.Required("file"), context);
        }

        if (!TryParsePurpose(form["purpose"], out var purpose))
        {
            return Problems.From(MediaErrors.Required("purpose"), context);
        }

        await using var content = file.OpenReadStream();

        var command = new UploadMediaCommand(
            caller.Context.OrganisationId,
            caller.Context.BranchId!.Value,
            purpose,
            ParseGuid(form["customerId"]),
            ParseGuid(form["orderId"]),
            ParseGuid(form["jobId"]),
            content,
            file.ContentType,
            file.Length,
            ParseString(form["altText"]),
            caller.UserId);

        var result = await handler.UploadAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Problems.From(result.Error, context)
            : Results.Json(MediaObjectPayload.From(result.Value), statusCode: StatusCodes.Status202Accepted);
    }

    private static bool TryParsePurpose(StringValues raw, out MediaPurpose purpose)
        => Enum.TryParse(raw.ToString(), ignoreCase: true, out purpose) && Enum.IsDefined(purpose);

    private static Guid? ParseGuid(StringValues raw)
        => Guid.TryParse(raw.ToString(), out var value) ? value : null;

    private static string? ParseString(StringValues raw)
        => raw.Count == 0 || string.IsNullOrWhiteSpace(raw[0]) ? null : raw.ToString();
}
