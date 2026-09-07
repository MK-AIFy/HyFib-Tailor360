using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// Reading the audit trail, and exporting it.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of "every administrative mutation has reviewable audit evidence" that a person
/// actually uses. Every command on this surface already writes a before-and-after entry; without a way
/// to read them the evidence exists and nobody can see it.
/// </para>
/// <para>
/// <b>The read does not declare step-up, and that is deliberate.</b> <c>admin.audit.read</c> is
/// catalogued as demanding a second factor and not a recent re-authentication, so declaring step-up
/// here would be a control the catalogue does not approve — and ARCH-018 refuses it in that direction
/// as firmly as in the other. An auditor reading the trail is doing their job, not changing anything.
/// </para>
/// <para>
/// <b>The export is audited even though it changes nothing.</b> Reading is normally not worth an
/// entry, but taking a copy of the trail out of the system is the act itself: it is how the record of
/// what everybody did leaves the building, so it demands its own permission, its own reason, and a
/// line in the trail saying who took it.
/// </para>
/// </remarks>
public static class AuditAdminEndpoints
{
    /// <summary>The audit action recorded when the trail is exported.</summary>
    public const string ExportedAction = "platform.audit.exported";

    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "The route names no row. It takes a query over the trail, and the trail spans every branch by "
        + "design — an auditor asking what happened cannot be answered only about the branch they "
        + "happen to be sitting in.";

    /// <summary>Maps the audit endpoints.</summary>
    /// <param name="audit">The <c>/api/v1/admin/audit</c> group.</param>
    public static RouteGroupBuilder MapAuditAdminEndpoints(this RouteGroupBuilder audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        audit.MapGet("/", async Task<IResult> (
                HttpContext context,
                IAuditReader reader,
                CancellationToken cancellationToken,
                string? entityType = null,
                Guid? entityId = null,
                Guid? actorId = null,
                string? action = null,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null,
                string? cursor = null,
                int limit = AuditQuery.DefaultLimit) =>
            {
                if (entityId is not null && string.IsNullOrWhiteSpace(entityType))
                {
                    return Problems.From(IdentityApiErrors.AuditSubjectIncomplete, context);
                }

                var page = await reader.SearchAsync(
                    new AuditQuery(
                        entityType,
                        entityId,
                        actorId,
                        action,
                        from,
                        to,
                        cursor,
                        Math.Clamp(limit, 1, AuditQuery.MaximumLimit)),
                    cancellationToken);

                // The trail names people and what they did. It is read on a screen and nowhere else.
                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(AuditPagePayload.From(page));
            })
            .Produces<AuditPagePayload>(StatusCodes.Status200OK)
            .WithName("ReadAuditTrail")
            .WithSummary("Read the audit trail, filtered by subject, actor, action and time.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.AuditRead, BranchScope.Organisation)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        audit.MapPost("/export", async Task<IResult> (
                ExportAuditPayload request,
                HttpContext context,
                IAuditReader reader,
                IAuditWriter writer,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (request?.Reason is not { } given || string.IsNullOrWhiteSpace(given))
                {
                    return Problems.From(IdentityApiErrors.ReasonRequired, context);
                }

                var reason = given.Trim();

                if (reason.Length > AdminRequests.MaximumReasonLength)
                {
                    return Problems.From(IdentityApiErrors.ReasonTooLong, context);
                }

                var page = await reader.SearchAsync(
                    new AuditQuery(
                        request.EntityType,
                        request.EntityId,
                        request.ActorId,
                        request.Action,
                        request.From,
                        request.To,
                        request.Cursor,
                        Math.Clamp(request.Limit ?? AuditQuery.MaximumExportLimit, 1, AuditQuery.MaximumExportLimit)),
                    cancellationToken);

                // Recorded before the body is handed over, and recorded whatever the export contained:
                // an export that returned nothing is still somebody having asked.
                await writer.WriteAsync(
                    new AuditEntry(
                        ExportedAction,
                        "AuditTrail",
                        caller.UserId,
                        $"{page.Entries.Count} audit entr(ies) were exported.",
                        reason,
                        Before: null,
                        After: new
                        {
                            request.EntityType,
                            request.EntityId,
                            request.ActorId,
                            request.Action,
                            From = request.From?.ToString("O", CultureInfo.InvariantCulture),
                            To = request.To?.ToString("O", CultureInfo.InvariantCulture),
                            page.Entries.Count,
                        }),
                    cancellationToken);

                await writer.SaveAsync(cancellationToken);

                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(AuditPagePayload.From(page));
            })
            .Produces<AuditPagePayload>(StatusCodes.Status200OK)
            .WithName("ExportAuditTrail")
            .WithSummary("Export a slice of the audit trail for an external review.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.AuditExport, BranchScope.Organisation)
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.ExportHeavy)
            .Audited(ExportedAction, reasonRequired: true)
            .WithRequestTimeout(RequestTimeoutPolicies.Export);

        return audit;
    }
}
