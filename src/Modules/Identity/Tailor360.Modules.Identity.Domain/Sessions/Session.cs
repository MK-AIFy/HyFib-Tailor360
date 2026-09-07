using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Sessions;

/// <summary>
/// A server-side session ticket. The browser holds a random 256-bit value in the
/// <c>__Host-t360.session</c> cookie and nothing else; every fact about the session — who it belongs
/// to, which branch it is working in, whether the second factor has been satisfied, when it must end —
/// lives in this row, where it can be revoked in one statement.
/// </summary>
/// <remarks>
/// Three invariants make this type worth having rather than a bag of columns.
/// <list type="number">
/// <item>
/// <description>
/// Only the <em>hash</em> of the cookie value is stored, so a stolen database dump yields no usable
/// tickets. The type accepts nothing else.
/// </description>
/// </item>
/// <item>
/// <description>
/// Revocation is final. A revoked session cannot be touched, cannot record a strong authentication
/// and cannot be rotated into a new one. "Sign out everywhere" is worth nothing if a request already
/// in flight can bring a session back.
/// </description>
/// </item>
/// <item>
/// <description>
/// Sliding never crosses the absolute expiry, and rotation carries the original absolute expiry into
/// the replacement. Without the second half, rotating at each multi-factor challenge or step-up would
/// hand out a fresh twelve hours every time, and the absolute cap would quietly mean nothing.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class Session
{
    /// <summary>The longest device label the store accepts.</summary>
    public const int MaximumDeviceLabelLength = 100;

    /// <summary>The longest user-agent string kept. Anything longer is truncated, not rejected.</summary>
    public const int MaximumUserAgentLength = 400;

    /// <summary>The longest client address kept, sized for IPv6 with a scope identifier.</summary>
    public const int MaximumIpAddressLength = 64;

    private Session()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Session(
        Guid id,
        Guid userId,
        Guid organisationId,
        Guid? activeBranchId,
        string tokenHash,
        string deviceLabel,
        DateTimeOffset now,
        SessionLifetime lifetime,
        SessionPendingStep pendingStep)
    {
        Id = id;
        UserId = userId;
        OrganisationId = organisationId;
        ActiveBranchId = activeBranchId;
        TokenHash = tokenHash;
        DeviceLabel = deviceLabel;
        CreatedAt = now;
        LastSeenAt = now;
        AbsoluteExpiresAt = now + lifetime.AbsoluteLifetime;
        IdleExpiresAt = Earliest(now + lifetime.IdleTimeout, AbsoluteExpiresAt);
        PendingStep = pendingStep;
    }

    /// <summary>Identity of this session. Never the cookie value.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account the session belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The organisation the session is working in.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch the session is currently working in, when it is branch-scoped.</summary>
    public Guid? ActiveBranchId { get; private set; }

    /// <summary>The SHA-256 digest of the cookie value. Never the value itself.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// What the holder sees in the session inventory, such as "Chrome on Windows". Derived from the
    /// user agent at sign-in and safe to show; it carries no identifier of its own.
    /// </summary>
    public string DeviceLabel { get; private set; } = string.Empty;

    /// <summary>The client address the session was last seen from. Personal data; shown only to the holder.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>The user agent the session was created with.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>When the session started.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a request last used the session.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>When the session ends if nothing further uses it.</summary>
    public DateTimeOffset IdleExpiresAt { get; private set; }

    /// <summary>When the session ends however active it is.</summary>
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    /// <summary>
    /// When the holder last proved a strong factor. Step-up endpoints compare this against the
    /// configured freshness window rather than trusting a flag set once at sign-in.
    /// </summary>
    public DateTimeOffset? LastStrongAuthAt { get; private set; }

    /// <summary>True once a second factor has been satisfied on this session.</summary>
    public bool MfaSatisfied { get; private set; }

    /// <summary>
    /// What the holder still owes before this session has finished signing in. A session that owes
    /// anything is a half session and reaches only the endpoints that finish the sign-in or end it.
    /// </summary>
    public SessionPendingStep PendingStep { get; private set; }

    /// <summary>True when the holder has finished signing in under this session.</summary>
    public bool IsSignInComplete => PendingStep is SessionPendingStep.None;

    /// <summary>The remembered device this session was started from, if any.</summary>
    public Guid? TrustedDeviceId { get; private set; }

    /// <summary>When the session was revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Why the session ended.</summary>
    public SessionEndReason? EndReason { get; private set; }

    /// <summary>The session that replaced this one at a rotation.</summary>
    public Guid? SupersededBySessionId { get; private set; }

    /// <summary>True while the session may authenticate a request.</summary>
    public bool IsActive(DateTimeOffset now)
        => RevokedAt is null && now < IdleExpiresAt && now < AbsoluteExpiresAt;

    /// <summary>Starts a session.</summary>
    /// <param name="id">Identity of the session, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The account signing in.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="activeBranchId">The branch the session starts in, when it is branch-scoped.</param>
    /// <param name="tokenHash">SHA-256 digest of the cookie value.</param>
    /// <param name="deviceLabel">A human label for the session inventory.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="lifetime">The inactivity and absolute timeouts.</param>
    /// <param name="pendingStep">
    /// What the holder still owes. A session that starts owing a step is a half session until the step
    /// is answered, and the sign-in path decides that from the account rather than from the request.
    /// </param>
    public static Result<Session> Start(
        Guid id,
        Guid userId,
        Guid organisationId,
        Guid? activeBranchId,
        string? tokenHash,
        string? deviceLabel,
        DateTimeOffset now,
        SessionLifetime lifetime,
        SessionPendingStep pendingStep = SessionPendingStep.None)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        if (id == Guid.Empty)
        {
            return Result.Failure<Session>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<Session>(IdentityErrors.Required("userId"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Session>(IdentityErrors.Required("organisationId"));
        }

        if (!HashedSecret.IsWellFormed(tokenHash))
        {
            return Result.Failure<Session>(IdentityErrors.TokenNotHashed("sessionToken"));
        }

        var label = deviceLabel?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return Result.Failure<Session>(IdentityErrors.Required("deviceLabel"));
        }

        if (label.Length > MaximumDeviceLabelLength)
        {
            label = label[..MaximumDeviceLabelLength];
        }

        return new Session(
            id, userId, organisationId, activeBranchId, tokenHash!, label, now, lifetime, pendingStep);
    }

    /// <summary>Records where the session is being used from, for the holder's own device inventory.</summary>
    public void RecordClient(string? ipAddress, string? userAgent)
    {
        IpAddress = Truncate(ipAddress, MaximumIpAddressLength);
        UserAgent = Truncate(userAgent, MaximumUserAgentLength);
    }

    /// <summary>
    /// Slides the inactivity deadline forward after a request, never past the absolute expiry. Fails on
    /// a session that is no longer active, so an expired ticket cannot be revived by using it.
    /// </summary>
    public Result Touch(DateTimeOffset now, TimeSpan idleTimeout)
    {
        if (!IsActive(now))
        {
            return Result.Failure(IdentityErrors.SessionNotActive);
        }

        LastSeenAt = now;
        IdleExpiresAt = Earliest(now + idleTimeout, AbsoluteExpiresAt);

        return Result.Success();
    }

    /// <summary>
    /// Records that a strong factor was satisfied on this session just now, which also finishes the
    /// sign-in: answering the challenge is what a session owing <c>MultiFactorChallenge</c> owed, and
    /// confirming an enrolment with a live code is what a session owing <c>MultiFactorEnrolment</c>
    /// owed. Leaving the step outstanding here would strand the holder on the screen they just passed.
    /// </summary>
    public Result RecordStrongAuthentication(DateTimeOffset now)
    {
        if (!IsActive(now))
        {
            return Result.Failure(IdentityErrors.SessionNotActive);
        }

        MfaSatisfied = true;
        LastStrongAuthAt = now;
        PendingStep = SessionPendingStep.None;

        return Result.Success();
    }

    /// <summary>Notes the remembered device this session was started from.</summary>
    public void AttachTrustedDevice(Guid trustedDeviceId) => TrustedDeviceId = trustedDeviceId;

    /// <summary>Moves the session to another branch the holder is assigned to.</summary>
    public Result SwitchBranch(Guid? branchId, DateTimeOffset now)
    {
        if (!IsActive(now))
        {
            return Result.Failure(IdentityErrors.SessionNotActive);
        }

        ActiveBranchId = branchId;
        return Result.Success();
    }

    /// <summary>
    /// True when the last strong authentication is recent enough for a step-up endpoint. A session that
    /// has never carried one is never fresh.
    /// </summary>
    public bool IsStepUpFresh(DateTimeOffset now, TimeSpan freshness)
        => LastStrongAuthAt is { } last && now - last <= freshness && IsActive(now);

    /// <summary>
    /// Ends the session. Revoking an already-revoked session succeeds without altering the original
    /// reason or time, because signing out everywhere naturally revisits sessions that are already gone
    /// and the first reason is the true one.
    /// </summary>
    public Result Revoke(DateTimeOffset now, SessionEndReason reason)
    {
        if (RevokedAt is not null)
        {
            return Result.Success();
        }

        RevokedAt = now;
        EndReason = reason;

        return Result.Success();
    }

    /// <summary>
    /// Replaces this session with a new one carrying a new cookie value, which is what defeats session
    /// fixation: the ticket a caller held before signing in is revoked and is not the ticket they hold
    /// afterwards. The replacement inherits this session's absolute expiry, so repeated rotation cannot
    /// extend the total life.
    /// </summary>
    /// <param name="newSessionId">Identity of the replacement, from <c>IIdGenerator</c>.</param>
    /// <param name="newTokenHash">SHA-256 digest of the new cookie value.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="idleTimeout">The inactivity timeout applied to the replacement.</param>
    public Result<Session> RotateTo(
        Guid newSessionId,
        string? newTokenHash,
        DateTimeOffset now,
        TimeSpan idleTimeout)
    {
        if (!IsActive(now))
        {
            return Result.Failure<Session>(IdentityErrors.SessionNotActive);
        }

        if (newSessionId == Guid.Empty)
        {
            return Result.Failure<Session>(IdentityErrors.Required("id"));
        }

        if (!HashedSecret.IsWellFormed(newTokenHash))
        {
            return Result.Failure<Session>(IdentityErrors.TokenNotHashed("sessionToken"));
        }

        var replacement = new Session
        {
            Id = newSessionId,
            UserId = UserId,
            OrganisationId = OrganisationId,
            ActiveBranchId = ActiveBranchId,
            TokenHash = newTokenHash!,
            DeviceLabel = DeviceLabel,
            IpAddress = IpAddress,
            UserAgent = UserAgent,
            CreatedAt = now,
            LastSeenAt = now,
            AbsoluteExpiresAt = AbsoluteExpiresAt,
            IdleExpiresAt = Earliest(now + idleTimeout, AbsoluteExpiresAt),
            LastStrongAuthAt = LastStrongAuthAt,
            MfaSatisfied = MfaSatisfied,
            TrustedDeviceId = TrustedDeviceId,

            // Carried, not cleared. A rotation that forgot the outstanding step would turn every
            // rotation into a way of finishing a sign-in without answering anything.
            PendingStep = PendingStep,
        };

        RevokedAt = now;
        EndReason = SessionEndReason.Rotated;
        SupersededBySessionId = newSessionId;

        return replacement;
    }

    private static DateTimeOffset Earliest(DateTimeOffset left, DateTimeOffset right)
        => left < right ? left : right;

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
