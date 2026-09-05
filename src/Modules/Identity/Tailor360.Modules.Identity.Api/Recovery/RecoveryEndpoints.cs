using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Recovery;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Recovery;

/// <summary>
/// Asking for a recovery link, and spending one.
/// </summary>
/// <remarks>
/// <b>A request tells the caller nothing.</b> Whether or not the address belongs to an account, the
/// response is the same object with the same wording and the same status, and the handler holds it to
/// the same floor of elapsed time. Identical wording alone is not enough: the honest implementation is
/// fast when the address is unknown and slow when it is known, and that difference is a more reliable
/// account oracle than any message would have been.
/// <para>
/// <b>Completing a reset changes the password and nothing else.</b> The authenticator, the recovery
/// codes and the passkeys are untouched, and the sign-in that follows still asks for a second factor.
/// Anything else would turn control of one mailbox into full control of an account, which is precisely
/// what a second factor exists to prevent.
/// </para>
/// </remarks>
public static class RecoveryEndpoints
{
    /// <summary>
    /// The wording both branches return. It is a constant so that the two paths cannot drift into
    /// saying subtly different things, which is how an anti-enumeration measure quietly stops working.
    /// </summary>
    private const string AcceptedMessage =
        "If that address belongs to an account, a recovery link is on its way. The link can be used "
        + "once and expires shortly, so check the inbox now.";

    private const string Review = "#23, docs/security/threat-models/authentication.md";

    /// <summary>Maps the recovery endpoints.</summary>
    /// <param name="auth">The <c>/api/v1/auth</c> group.</param>
    public static RouteGroupBuilder MapRecoveryEndpoints(this RouteGroupBuilder auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        auth.MapPost("/recovery/request", async (
                RecoveryRequestPayload request,
                HttpContext context,
                PasswordRecoveryHandler handler,
                ICredentialThrottle throttle,
                CancellationToken cancellationToken) =>
            {
                var clientKey = RequestFacts.ClientAddress(context);

                // The throttle is keyed on the address that was typed, so hammering one mailbox is
                // bounded separately from hammering many. Both matter: each accepted request sends a
                // message to somebody who did not ask for it.
                var decision = throttle.Check(CredentialAction.Recovery, request.Email, clientKey);
                if (!decision.IsAllowed)
                {
                    return Problems.TooManyAttempts(
                        context,
                        decision.RetryAfter,
                        IdentityApiErrors.TooManyAttemptsCode,
                        "Too many recovery requests have been made. Wait and try again.");
                }

                throttle.RecordFailure(CredentialAction.Recovery, request.Email, clientKey);

                var result = await handler.RequestAsync(
                    new RequestPasswordRecovery(request.Email), cancellationToken);

                // Even a failure answers as an acceptance. There is no failure a caller could learn
                // anything safe from here, and a distinguishable one would undo the whole design.
                _ = result;

                context.Response.Headers.CacheControl = "no-store";
                return Results.Accepted(value: new RecoveryAcceptedPayload(AcceptedMessage));
            })
            .AllowAnonymousWithJustification(
                "Somebody who cannot sign in is exactly who needs this endpoint, so it cannot require a "
                + "session. It is bounded by an anti-forgery token, the origin check, a rate-limit "
                + "policy tighter than sign-in's because each accepted request sends a message, and a "
                + "throttle keyed on both the address typed and the caller's address. The response is "
                + "byte-identical and takes the same time whether or not the address is known, so it "
                + "cannot be used to discover who holds an account.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.RecoveryAnonymous)
            .Audited(PasswordRecoveryHandler.RequestedAction)
            .WithName("RequestPasswordRecovery")
            .WithSummary("Ask for a password recovery link.")
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/recovery/confirm", async (
                RecoveryConfirmationPayload request,
                HttpContext context,
                PasswordRecoveryHandler handler,
                ICredentialThrottle throttle,
                CancellationToken cancellationToken) =>
            {
                var clientKey = RequestFacts.ClientAddress(context);

                var decision = throttle.Check(CredentialAction.Recovery, accountKey: null, clientKey);
                if (!decision.IsAllowed)
                {
                    return Problems.TooManyAttempts(
                        context,
                        decision.RetryAfter,
                        IdentityApiErrors.TooManyAttemptsCode,
                        "Too many attempts have been made. Wait and try again.");
                }

                var result = await handler.ConfirmAsync(
                    new ConfirmPasswordRecovery(request.Token, request.NewPassword), cancellationToken);

                if (result.IsFailure)
                {
                    throttle.RecordFailure(CredentialAction.Recovery, accountKey: null, clientKey);
                    return Problems.From(result.Error, context);
                }

                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(new RecoveryCompletedPayload(
                    result.Value.MultiFactorStillRequired,
                    result.Value.SessionsRevoked,
                    result.Value.MustChangePassword));
            })
            .AllowAnonymousWithJustification(
                "Spending a recovery link is done by someone who by definition has no session. The link "
                + "itself is the credential: 256 bits of server entropy, stored only as a digest, "
                + "single-use, expiring within the hour, and withdrawn as soon as a replacement is "
                + "issued. Unknown, spent, expired, withdrawn and wrong-purpose tokens all report the "
                + "same failure. The anti-forgery token, the origin check and a rate-limit policy apply "
                + "as they do to sign-in, and completing a reset ends every session on the account "
                + "without touching its second factor.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.RecoveryAnonymous)
            .Audited(PasswordRecoveryHandler.CompletedAction)
            .WithName("ConfirmPasswordRecovery")
            .WithSummary("Spend a recovery link and set a new password.")
            .WithTags(IdentityRoutes.AuthTag);

        return auth;
    }
}
