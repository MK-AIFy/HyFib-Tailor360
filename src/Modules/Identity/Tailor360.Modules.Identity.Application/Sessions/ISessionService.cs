using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Sessions;

/// <summary>
/// Creates, rotates, lists and revokes server-side sessions. Every endpoint that changes what a caller
/// is signed in as goes through here; nothing else writes the session table.
/// </summary>
/// <remarks>
/// <para>
/// The service returns the cookie value and never writes it. Placing it on the response is the
/// endpoint's job, through <c>SessionCookie.Issue</c>, which is the one place the cookie's attributes
/// are decided. Splitting it this way keeps the module free of <c>HttpContext</c> and keeps the
/// <c>__Host-</c> attributes out of reach of a call site that might be tempted to relax one.
/// </para>
/// <para>
/// <b>The contract every caller must honour.</b> After <see cref="StartAsync"/> or
/// <see cref="RotateAsync"/> succeeds, the returned token must be written to the session cookie on the
/// same response. Failing to do so leaves a live session nobody holds — harmless, but it fills the
/// inventory with entries the holder cannot explain. Failing the other way round — writing a cookie
/// without a session row — is impossible by construction, since the value only exists here.
/// </para>
/// </remarks>
public interface ISessionService
{
    /// <summary>
    /// Starts a session for an account that has just authenticated, revoking the ticket the caller
    /// presented beforehand.
    /// </summary>
    /// <remarks>
    /// Fails when the account cannot authenticate at all — deactivated, suspended, never activated or
    /// locked out. The sign-in handler has normally checked this already; checking again here means no
    /// future call site can create a session for an account that must not have one.
    /// </remarks>
    Task<Result<IssuedSession>> StartAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a live session with a new one carrying a new cookie value, keeping the original
    /// absolute expiry so that repeated rotation cannot extend the total life.
    /// </summary>
    /// <param name="sessionId">The session being replaced.</param>
    /// <param name="reason">Why it is being replaced; some reasons also record a strong authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IssuedSession>> RotateAsync(
        Guid sessionId,
        SessionRotationReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>Ends one session. Revoking an already-revoked session succeeds and changes nothing.</summary>
    Task<Result> RevokeAsync(
        Guid sessionId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every live session on an account, optionally sparing one — which is what "sign out
    /// everywhere else" means on the device the holder is currently using.
    /// </summary>
    /// <returns>How many sessions were ended.</returns>
    Task<Result<int>> RevokeAllForUserAsync(
        Guid userId,
        SessionEndReason reason,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the account's live sessions for the holder's own device inventory.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="currentSessionId">The session making the request, marked as "this device".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SessionSummary>> ListForUserAsync(
        Guid userId,
        Guid? currentSessionId = null,
        CancellationToken cancellationToken = default);
}
