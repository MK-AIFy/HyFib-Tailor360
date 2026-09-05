using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Authentication;

/// <summary>
/// Enrolling an authenticator and printing recovery codes.
/// </summary>
/// <remarks>
/// Enrolment is two requests rather than one because a secret that has never produced a working code is
/// not a second factor — it is a way of locking somebody out of their own account. The first request
/// stores the secret and changes nothing else; only a code read off the holder's own authenticator
/// turns it into a factor, and only then are recovery codes issued.
/// </remarks>
public static class MultiFactorEnrolmentEndpoints
{
    private const string SelfServiceReview = "#23, docs/security/threat-models/authentication.md";

    private const string EnrolmentJustification =
        "Enrolling a second factor acts on the caller's own account, taken from the session and never "
        + "from the request. No permission could gate it correctly: the accounts that most need a second "
        + "factor are the ones whose permissions are still being decided, and an account that could be "
        + "refused permission to protect itself would be an account nobody could secure. It is reachable "
        + "by a caller who has not finished signing in because that is exactly who needs it: an account "
        + "whose permissions require a factor it does not have is told to enrol one, and can do nothing "
        + "else until it has. What stops that from being a way in is applied where the account is known "
        + "rather than here — the handler refuses to add or replace a factor on an account that already "
        + "holds one unless the session making the request has satisfied it.";

    private const string RecoveryCodeJustification =
        "Printing recovery codes acts on the caller's own account, taken from the session and never from "
        + "the request, and no permission could gate it correctly for the same reason enrolment cannot. "
        + "It demands a satisfied second factor: the response body is a working set of second factors "
        + "and printing destroys the previous sheet, so a caller holding only a stolen password could "
        + "otherwise print themselves a factor, answer the challenge with it, and lock the holder out on "
        + "the way past. Demanding that the factor also be recent is better still and is recorded as an "
        + "open residual risk, because the endpoint that lets somebody prove one again without signing "
        + "out arrives with #24.";

    /// <summary>Maps the enrolment endpoints.</summary>
    /// <param name="auth">The <c>/api/v1/auth</c> group.</param>
    public static RouteGroupBuilder MapMultiFactorEnrolmentEndpoints(this RouteGroupBuilder auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        auth.MapPost("/mfa/enrol", async (
                HttpContext context,
                TotpEnrolmentHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.BeginAsync(
                    RequestFacts.Caller(caller, session), cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                // The secret is returned once, over the body of an authenticated request, and never
                // written anywhere else. The response is marked no-store so that a shared browser or an
                // intermediary cache cannot hold a copy of it.
                context.Response.Headers.CacheControl = "no-store";

                var started = result.Value;
                return Results.Ok(new MfaEnrolmentStartedPayload(
                    started.OtpAuthUri,
                    started.ManualEntryKey,
                    started.Issuer,
                    started.AccountName,
                    started.Digits,
                    started.PeriodSeconds));
            })
            .AllowPendingSignIn(EnrolmentJustification, SelfServiceReview)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TotpEnrolmentHandler.EnrolmentStartedAction)
            .WithName("BeginMultiFactorEnrolment")
            .WithSummary("Start enrolling an authenticator and return the QR link and manual key.")
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/mfa/enrol/confirm", async (
                MfaEnrolmentConfirmationPayload request,
                HttpContext context,
                TotpEnrolmentHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ConfirmAsync(
                    RequestFacts.Caller(caller, session), request.Code, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Codes(context, result.Value);
            })
            .AllowPendingSignIn(EnrolmentJustification, SelfServiceReview)
            .RequireRateLimiting(RateLimitPolicyNames.MultiFactorChallenge)
            .Audited(TotpEnrolmentHandler.EnrolmentConfirmedAction)
            .WithName("ConfirmMultiFactorEnrolment")
            .WithSummary("Confirm an authenticator with a code and issue the recovery codes.")
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/mfa/recovery-codes", async (
                HttpContext context,
                TotpEnrolmentHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.ReissueRecoveryCodesAsync(
                    RequestFacts.Caller(caller, session), cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Codes(context, result.Value);
            })
            .RequireSatisfiedSecondFactor(RecoveryCodeJustification, SelfServiceReview)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(TotpEnrolmentHandler.RecoveryCodesIssuedAction)
            .WithName("ReissueRecoveryCodes")
            .WithSummary("Print a fresh sheet of recovery codes, destroying the previous sheet.")
            .WithTags(IdentityRoutes.AuthTag);

        return auth;
    }

    /// <summary>
    /// Returns the codes, once. This is the only moment they exist in a readable form anywhere, so the
    /// response must not be cached by anything between here and the screen.
    /// </summary>
    /// <remarks>
    /// Confirming an enrolment also replaces the session, because a correct code off the holder's own
    /// authenticator is a second factor satisfied. The cookie is rewritten here; failing to would leave
    /// the browser holding the ticket the confirmation just revoked, which reads to the person as being
    /// signed out by succeeding.
    /// </remarks>
    private static IResult Codes(HttpContext context, TotpEnrolmentConfirmed confirmed)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (confirmed.Session is { } rotated)
        {
            SessionCookie.Issue(context.Response, rotated.Token);
        }

        return Results.Ok(new RecoveryCodesPayload(confirmed.RecoveryCodes));
    }
}
