using Tailor360.Modules.Identity.Domain.Sessions;

namespace Tailor360.Modules.Identity.Application.Sessions;

/// <summary>What a caller supplies to start a session.</summary>
/// <remarks>
/// The organisation and the branch are deliberately absent: the service reads them from the account,
/// so a handler cannot start a session in an organisation the holder does not belong to by passing the
/// wrong value.
/// </remarks>
/// <param name="UserId">The account signing in.</param>
/// <param name="DeviceLabel">
/// What the holder will see in their session inventory, such as "Chrome on Windows". Derived from the
/// user agent by the caller; it is shown to the holder, so it must stay free of anything technical.
/// </param>
/// <param name="IpAddress">The client address, for the holder's own inventory. Personal data.</param>
/// <param name="UserAgent">The user agent, for the holder's own inventory. Personal data.</param>
/// <param name="MfaSatisfied">
/// True when a second factor has already been proved — a passkey assertion, which is both factors at
/// once, or a remembered device the owner's policy allows to skip the challenge.
/// </param>
/// <param name="TrustedDeviceId">The remembered device this sign-in came from, when there was one.</param>
/// <param name="SupersedesSessionId">
/// The session the caller was holding before this one, when it presented a cookie. The service revokes
/// it in the same unit of work, which is what makes a planted ticket worthless: it is not the ticket
/// the person leaves with, and it stops working the moment they arrive.
/// </param>
/// <param name="PendingStep">
/// What the sign-in still owes. A session that starts owing a step is a half session: it reaches the
/// endpoints that answer the step and the ones that end it, and nothing else. Recording it here rather
/// than leaving the client to follow the flow is what stops a caller who simply stops following it from
/// keeping a fully capable session.
/// </param>
public sealed record StartSessionRequest(
    Guid UserId,
    string DeviceLabel,
    string? IpAddress = null,
    string? UserAgent = null,
    bool MfaSatisfied = false,
    Guid? TrustedDeviceId = null,
    Guid? SupersedesSessionId = null,
    SessionPendingStep PendingStep = SessionPendingStep.None);

/// <summary>
/// A session that has just been created or rotated, together with the one-time value the caller must
/// place in the session cookie.
/// </summary>
/// <remarks>
/// <paramref name="Token"/> is a credential. It exists in exactly two places — this record and the
/// <c>Set-Cookie</c> header the caller writes from it — and appears in no log, no problem-details
/// response, no trace attribute and no response body. Only its digest is stored.
/// </remarks>
/// <param name="SessionId">Identity of the session row, safe to log and to audit.</param>
/// <param name="Token">The opaque cookie value. Never logged, never returned in a body.</param>
/// <param name="IdleExpiresAt">When the session ends if nothing further uses it.</param>
/// <param name="AbsoluteExpiresAt">When the session ends however active it is.</param>
/// <param name="MfaSatisfied">True when a second factor has been satisfied on this session.</param>
/// <param name="SignInComplete">True when the holder owes nothing further to finish signing in.</param>
public sealed record IssuedSession(
    Guid SessionId,
    string Token,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    bool MfaSatisfied,
    bool SignInComplete);

/// <summary>
/// One row of the holder's own session and device inventory. It carries nothing that could be used to
/// impersonate the session — no token, no digest — because the inventory is a screen, not a credential
/// store.
/// </summary>
/// <param name="SessionId">Identity of the session, which is what a revoke request names.</param>
/// <param name="DeviceLabel">What the holder recognises the device by.</param>
/// <param name="IpAddress">The address the session was last seen from. Shown only to the holder.</param>
/// <param name="CreatedAt">When the session started.</param>
/// <param name="LastSeenAt">When a request last used it.</param>
/// <param name="IdleExpiresAt">When it ends if nothing further uses it.</param>
/// <param name="AbsoluteExpiresAt">When it ends however active it is.</param>
/// <param name="MfaSatisfied">True when a second factor was satisfied on it.</param>
/// <param name="IsCurrent">True for the session making the request, so the screen can say "this device".</param>
public sealed record SessionSummary(
    Guid SessionId,
    string DeviceLabel,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    bool MfaSatisfied,
    bool IsCurrent);
