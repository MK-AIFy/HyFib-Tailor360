using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Outbox;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Api.Administration;

/// <summary>
/// The dead letter, and putting a message in it back on the queue.
/// </summary>
/// <remarks>
/// <para>
/// Until now this was a console operation only, because the permission model that would guard an
/// endpoint did not exist — <c>docs/platform/outbox.md</c> and <c>docs/architecture/failure-modes.md</c>
/// both say so and both name this issue as where it changes. The rows belong to Platform and are
/// reached through <see cref="IOutboxAdministration"/> rather than by mapping
/// <c>platform.outbox_messages</c> from this module, which is what module-ownership prescribes and what
/// keeps ARCH-005 true.
/// </para>
/// <para>
/// <b>A replay can duplicate an outbound effect.</b> A message that dead-lettered after the provider
/// had in fact accepted it will be sent again — a second message to a customer, a second push to an
/// accounting system. That is why the operation carries step-up as well as a second factor: it is not
/// a setting, it is an act with a consequence outside the building, and the person doing it should have
/// proved who they are within the last few minutes.
/// </para>
/// <para>
/// <b>There is no drain endpoint.</b> <see cref="IOutboxAdministration.ReplayAllAsync"/> exists and the
/// command-line tool uses it, but replaying a whole dead letter is a decision made with the logs open
/// after an outage has been diagnosed, and one operator's "everything" is another's duplicate-delivery
/// storm. A screen offers the messages one at a time, each with the error it failed on.
/// </para>
/// </remarks>
public static class OutboxAdminEndpoints
{
    private const string Review = "#25, docs/security/permission-matrix.md";

    private const string NoBranchResource =
        "An outbox message is a platform row: it carries the event some module published, and the "
        + "aggregate it belongs to may not be a branch-owned thing at all. The route parameter is the "
        + "message identifier, so there is no branch-owned row for a resource scope to be evaluated "
        + "against — the organisation reach the permission declares is the whole of the decision.";

    /// <summary>Maps the outbox endpoints.</summary>
    /// <param name="outbox">The <c>/api/v1/admin/outbox</c> group.</param>
    public static RouteGroupBuilder MapOutboxAdminEndpoints(this RouteGroupBuilder outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        outbox.MapGet("/dead-letters", async Task<IResult> (
                IOutboxAdministration administration,
                CancellationToken cancellationToken,
                int limit = DeadLetteredMessage.DefaultLimit) =>
                Results.Ok((await administration.ListDeadLettersAsync(limit, cancellationToken))
                    .Select(DeadLetteredMessagePayload.From).ToArray()))
            .Produces<IReadOnlyList<DeadLetteredMessagePayload>>(StatusCodes.Status200OK)
            .WithName("ListOutboxDeadLetters")
            .WithSummary("List the outbox messages that exhausted their delivery attempts.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.OutboxReplay, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser);

        outbox.MapPost("/{messageId:guid}/replay", async Task<IResult> (
                Guid messageId,
                ReasonPayload request,
                HttpContext context,
                IOutboxAdministration administration,
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

                var result = await administration.ReplayAsync(
                    messageId, reason, caller.UserId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(DeadLetteredMessagePayload.From(result.Value));
            })
            .Produces<DeadLetteredMessagePayload>(StatusCodes.Status200OK)
            .WithName("ReplayOutboxMessage")
            .WithSummary("Put one dead-lettered outbox message back on the queue.")
            .WithTags(IdentityRoutes.AdminTag)
            .RequirePermission(PlatformPermissions.OutboxReplay, BranchScope.Organisation)
            .RequireStepUp()
            .TouchesNoBranchOwnedResource(NoBranchResource, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(OutboxAdministrationActions.Replayed, reasonRequired: true)
            .RequireIdempotency()
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return outbox;
    }
}

/// <summary>
/// The audit actions this surface declares.
/// </summary>
/// <remarks>
/// Named here rather than taken from the persistence implementation because an <c>Api</c> project must
/// not reference <c>Platform.Persistence</c> (ARCH-004). The two are held together by
/// <c>OutboxAdministrationEndpointTests.TheEndpointAndTheImplementationAgreeOnTheAuditAction</c>, which is the check a
/// shared constant would only have looked like.
/// </remarks>
public static class OutboxAdministrationActions
{
    /// <summary>Recorded when a dead-lettered message is put back on the queue.</summary>
    public const string Replayed = "platform.outbox.replayed";
}
