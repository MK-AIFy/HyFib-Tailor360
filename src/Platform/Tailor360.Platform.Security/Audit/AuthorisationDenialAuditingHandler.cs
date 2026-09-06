using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Audit;

/// <summary>
/// Records a refused attempt at a state-changing or step-up endpoint in the audit trail, then hands the
/// refusal to the handler that writes the problem document.
/// </summary>
/// <remarks>
/// <para>
/// It wraps rather than replaces <see cref="AuthorisationProblemResultHandler"/>. The authorisation
/// middleware resolves exactly one result handler, and the two jobs — telling the caller what happened
/// and telling the audit trail — are not the same job; composing them here keeps the problem document's
/// wording in one place and makes it impossible to record a refusal that the caller was not actually
/// given.
/// </para>
/// <para>
/// <b>What is recorded, and what is deliberately not.</b> The entry names the actor, the endpoint, the
/// class of refusal and the correlation. It carries no route values, no query string, no body and no
/// header: a denial is written down so that a pattern of attempts is visible, and nothing about the
/// attempt's contents is needed for that. Recording payloads would put unvalidated input — which is
/// exactly what a rejected request contains — into an append-only table nobody can edit afterwards.
/// </para>
/// <para>
/// <b>Which refusals.</b> Reads are not recorded. The client asks what it may do by asking, and a
/// work queue that refuses to show a tailor another branch's jobs would otherwise write an entry every
/// time somebody opened a screen — which is both noise and, in aggregate, a record of what people
/// looked at. What is recorded is an attempt to <em>change</em> something, and an attempt at an endpoint
/// that demands a fresh re-authentication, because both are the shape of an attack worth seeing.
/// </para>
/// <para>
/// <b>An unauthenticated flood is one actor.</b> A caller with no session is the anonymous principal, so
/// every refusal in a flood shares one key and becomes one entry per endpoint per minute. That is the
/// right trade: the entry records that the endpoint was being pushed at, the rate limiter is what
/// answers the pushing, and writing a row per attempt would let anybody with a script fill an
/// append-only table. What must never be coalesced away is one <em>identified</em> person's attempt,
/// and it never is: they have a key of their own.
/// </para>
/// <para>
/// <b>An audit failure never becomes a pass.</b> If the entry cannot be written the refusal still
/// stands and the caller is still refused; the failure is logged at error, because a denial trail that
/// is quietly not being written is worse than one that is obviously broken.
/// </para>
/// </remarks>
/// <param name="inner">The handler that writes the problem document.</param>
/// <param name="coalescer">Decides whether this refusal repeats one already recorded this minute.</param>
/// <param name="logger">Records the failure to record.</param>
public sealed class AuthorisationDenialAuditingHandler(
    AuthorisationProblemResultHandler inner,
    AuthorisationDenialCoalescer coalescer,
    ILogger<AuthorisationDenialAuditingHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The audit action every refusal is recorded under.</summary>
    public const string Action = "authz.denied";

    /// <summary>The entity type recorded, since the subject of a refusal is the endpoint.</summary>
    public const string EntityType = "Endpoint";

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged || authorizeResult.Forbidden)
        {
            await RecordAsync(context, authorizeResult);
        }

        await inner.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// True when a refusal at this endpoint is worth an audit entry: it changes state, or it demands a
    /// fresh re-authentication.
    /// </summary>
    /// <param name="endpoint">The endpoint the request routed to.</param>
    /// <param name="catalogue">The permission catalogue, for the endpoint's step-up flag.</param>
    public static bool IsAuditable(Endpoint? endpoint, PermissionCatalogue? catalogue)
    {
        if (endpoint is null)
        {
            return false;
        }

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        if (methods.Any(method => !HttpMethods.IsGet(method)
                                  && !HttpMethods.IsHead(method)
                                  && !HttpMethods.IsOptions(method)))
        {
            return true;
        }

        if (endpoint.Metadata.GetMetadata<StepUpMetadata>() is not null)
        {
            return true;
        }

        var key = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.PermissionKey;
        return key is not null && catalogue?.Find(key)?.RequiresStepUp == true;
    }

    /// <summary>
    /// A stable identifier for one endpoint, so that "every refusal at this route" is one query rather
    /// than a text search.
    /// </summary>
    /// <remarks>
    /// Derived from the signature by hash rather than generated, so the same route has the same
    /// identifier in every deployment and across restarts. It names a route template, never a resource
    /// and never a person.
    /// </remarks>
    /// <param name="signature">The endpoint signature, for example <c>POST /api/v1/orders</c>.</param>
    public static Guid IdentifierFor(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return new Guid(digest.AsSpan(0, 16));
    }

    private async Task RecordAsync(HttpContext context, PolicyAuthorizationResult authorizeResult)
    {
        var services = context.RequestServices;
        var endpoint = context.GetEndpoint();
        var catalogue = services.GetService<PermissionCatalogue>();

        if (!IsAuditable(endpoint, catalogue))
        {
            return;
        }

        var signature = Signature(context, endpoint);

        try
        {
            var clock = services.GetRequiredService<IClock>();
            var currentUser = services.GetRequiredService<ICurrentUser>();

            if (!coalescer.ShouldRecord(currentUser.PrincipalId, signature, clock.UtcNow))
            {
                return;
            }

            var writer = services.GetService<IAuditWriter>();
            if (writer is null)
            {
                logger.LogError(
                    "A refused attempt at {Endpoint} could not be recorded: no audit writer is "
                    + "registered in this host.",
                    signature);

                return;
            }

            await writer.WriteAsync(new AuditEntry(
                Action,
                EntityType,
                IdentifierFor(signature),
                Summary(signature, authorizeResult),

                // The actor is named here rather than left to the audit context, because a refusal on a
                // request that never authenticated has no actor for the context to have resolved, and an
                // entry attributed to "system" would put an attacker's attempts in the same bucket as
                // the scheduler's.
                ActorId: currentUser.IsAuthenticated ? currentUser.UserId : null,
                ActorDisplayName: currentUser.DisplayName));

            await writer.SaveAsync(context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The refusal stands whatever happens here. What must not happen is that it stands silently.
            logger.LogError(
                exception,
                "A refused attempt at {Endpoint} could not be recorded in the audit trail.",
                signature);
        }
    }

    /// <summary>
    /// The sentence recorded: what was refused, every requirement that refused it, and the status the
    /// caller was given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every failed requirement rather than the first, and ordered, because several can fail at once —
    /// a caller may lack the permission <em>and</em> be naming another branch's record — and the order
    /// the handlers happened to run in is not a fact about the request. The response tells the caller
    /// only the coarsest of them, deliberately; the audit trail is read by somebody who is allowed to
    /// know the rest.
    /// </para>
    /// <para>
    /// <b>The status recorded is the status served.</b> It is classified through the same method the
    /// problem writer uses rather than derived from "challenged or forbidden", because a cross-branch
    /// attempt is answered 404 on purpose — and an append-only trail that recorded 403 for it would
    /// disagree with the wire on exactly the requests an investigation reads first.
    /// </para>
    /// </remarks>
    private static string Summary(string signature, PolicyAuthorizationResult authorizeResult)
    {
        var classified = authorizeResult.Challenged
            ? AuthorisationRefusal.NotAuthenticated
            : AuthorisationProblemResultHandler.Classify(authorizeResult.AuthorizationFailure);

        var refusals = authorizeResult.Challenged
            ? [classified.ToString()]
            : authorizeResult.AuthorizationFailure?.FailureReasons
                .OfType<RefusalReason>()
                .Select(reason => reason.Refusal.ToString())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
              ?? [];

        var named = refusals.Length > 0
            ? string.Join(", ", refusals)
            : classified.ToString();

        var status = AuthorisationProblemResultHandler.StatusFor(classified);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Refused {signature} ({named}, {status}).");
    }

    private static string Signature(HttpContext context, Endpoint? endpoint)
    {
        var route = endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "/"
            : endpoint?.DisplayName ?? "(unrouted)";

        // The template, never the request path. A path carries identifiers the caller chose, and this
        // string is a key that a query groups by.
        return $"{context.Request.Method} {route}";
    }
}
