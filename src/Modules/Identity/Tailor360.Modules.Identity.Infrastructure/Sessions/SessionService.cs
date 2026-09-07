using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.Modules.Identity.Infrastructure.Sessions;

/// <summary>
/// The only writer of the session table. It creates a session at sign-in, replaces it at every step
/// that raises what the session can do, and ends it on sign-out or revocation.
/// </summary>
/// <param name="context">The Identity context.</param>
/// <param name="clock">The clock.</param>
/// <param name="idGenerator">The identifier generator.</param>
/// <param name="options">Session lifetimes.</param>
/// <param name="logger">Logger.</param>
public sealed class SessionService(
    IdentityDbContext context,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<SessionAuthenticationOptions> options,
    ILogger<SessionService> logger)
    : ISessionService
{
    /// <inheritdoc />
    public async Task<Result<IssuedSession>> StartAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<IssuedSession>(IdentityErrors.InvalidCredentials);
        }

        var authenticable = user.EnsureCanAuthenticate(now);
        if (authenticable.IsFailure)
        {
            return Result.Failure<IssuedSession>(authenticable.Error);
        }

        // Session fixation, closed here. Whatever ticket the caller was holding before they proved who
        // they are is revoked in the same unit of work as the new one is created, so a value planted in
        // their browser is worthless the instant it would have become valuable.
        if (request.SupersedesSessionId is { } supersededId)
        {
            var superseded = await context.Sessions
                .FirstOrDefaultAsync(session => session.Id == supersededId, cancellationToken);

            superseded?.Revoke(now, SessionEndReason.Rotated);
        }

        var lifetime = Lifetime();
        var token = SessionTokenFactory.CreateToken();

        var started = Session.Start(
            idGenerator.NewId(),
            user.Id,
            user.OrganisationId,
            user.HomeBranchId,
            SessionTokenFactory.Digest(token),
            request.DeviceLabel,
            now,
            lifetime,
            request.PendingStep);

        if (started.IsFailure)
        {
            return Result.Failure<IssuedSession>(started.Error);
        }

        var session = started.Value;
        session.RecordClient(request.IpAddress, request.UserAgent);

        if (request.TrustedDeviceId is { } trustedDeviceId)
        {
            session.AttachTrustedDevice(trustedDeviceId);
        }

        // A remembered device skips the challenge; it does not pass it. The session therefore starts
        // without a satisfied second factor. What that buys today is the self-service refusals — a
        // counter tablet cannot print recovery codes or change a factor. It will also gate the
        // administration and billing permissions that carry the multi-factor flag, but not yet:
        // PermissionAuthorisationHandler honours RequiresMfa, and the ticket's permission set is
        // empty until #24 fills it, so every permissioned endpoint denies for a different reason.
        if (request.MfaSatisfied && request.TrustedDeviceId is null)
        {
            session.RecordStrongAuthentication(now);
        }

        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Started session {SessionId} for user {UserId} (multi-factor satisfied: {MfaSatisfied}, "
                + "still owing: {PendingStep}).",
                session.Id,
                session.UserId,
                session.MfaSatisfied,
                session.PendingStep);
        }

        return Issued(session, token);
    }

    /// <inheritdoc />
    public async Task<Result<IssuedSession>> RotateAsync(
        Guid sessionId,
        SessionRotationReason reason,
        CancellationToken cancellationToken = default)
    {
        // The whole rotation is one retriable unit. The context is configured with the Npgsql retrying
        // execution strategy, which refuses a transaction the caller opened itself: on a transient
        // failure it would otherwise retry a statement inside a transaction it cannot re-open, so it
        // declines rather than doing that silently.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;

            // Read without tracking: the conditional revocation below is what retires the source row,
            // and a tracked copy would have the change tracker emit a second, unconditional UPDATE for
            // it — which is precisely the write that lets two rotations both succeed. Read inside the
            // delegate, because a retry has to see the row as it is now, not as it was on the attempt
            // that failed.
            var current = await context.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

            if (current is null)
            {
                return Result.Failure<IssuedSession>(IdentityErrors.SessionNotActive);
            }

            var token = SessionTokenFactory.CreateToken();

            var rotated = current.RotateTo(
                idGenerator.NewId(),
                SessionTokenFactory.Digest(token),
                now,
                options.Value.IdleTimeout);

            if (rotated.IsFailure)
            {
                return Result.Failure<IssuedSession>(rotated.Error);
            }

            var replacement = rotated.Value;

            if (reason.ProvesStrongAuthentication())
            {
                replacement.RecordStrongAuthentication(now);
            }

            // Rotation has exactly one winner. Two answers to the same multi-factor challenge, or a
            // challenge answered while another rotation is in flight, can both read this session as
            // active and both build a replacement; without a condition on the retirement both would
            // save, and one cookie would end up with two live successors — so gaining assurance would
            // stop meaning that exactly one credential replaced the one presented.
            //
            // The house pattern for this is conventions.md section 4.4: a row that may be consumed once
            // is consumed by a conditional update, and the request whose update affected a row is the
            // one that proceeds. Both statements share a transaction so a crash between them cannot
            // retire a session and leave no successor, which would sign somebody out mid-task.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var retired = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE identity.sessions
                    SET revoked_at = {now},
                        end_reason = {SessionEndReason.Rotated.ToString()},
                        superseded_by_session_id = {replacement.Id}
                  WHERE id = {sessionId}
                    AND revoked_at IS NULL
                 """,
                cancellationToken);

            if (retired == 0)
            {
                // Somebody else rotated it between the read and here. Theirs is the replacement that
                // exists, and this caller is holding a cookie that has already been superseded.
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<IssuedSession>(IdentityErrors.SessionNotActive);
            }

            context.Sessions.Add(replacement);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Rotated session {SessionId} into {ReplacementSessionId} because {Reason}.",
                    current.Id,
                    replacement.Id,
                    reason);
            }

            return Issued(replacement, token);
        });
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(
        Guid sessionId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default)
    {
        var session = await context.Sessions
            .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);

        // Revoking a session that is already gone is the normal case when two devices sign out at once,
        // so it succeeds rather than reporting a conflict the caller can do nothing about.
        if (session is null)
        {
            return Result.Success();
        }

        session.Revoke(clock.UtcNow, reason);
        await context.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Revoked session {SessionId} because {Reason}.", sessionId, reason);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> RevokeAllForUserAsync(
        Guid userId,
        SessionEndReason reason,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var sessions = await context.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var revoked = 0;
        foreach (var session in sessions)
        {
            if (session.Id == exceptSessionId)
            {
                continue;
            }

            session.Revoke(now, reason);
            revoked++;
        }

        if (revoked > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Revoked {Count} session(s) for user {UserId} because {Reason}.", revoked, userId, reason);
        }

        return Result.Success(revoked);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ListForUserAsync(
        Guid userId,
        Guid? currentSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var sessions = await context.Sessions
            .AsNoTracking()
            .Where(session => session.UserId == userId
                && session.RevokedAt == null
                && session.AbsoluteExpiresAt > now
                && session.IdleExpiresAt > now)
            .OrderByDescending(session => session.LastSeenAt)
            .ToListAsync(cancellationToken);

        return sessions.ConvertAll(session => new SessionSummary(
            session.Id,
            session.DeviceLabel,
            session.IpAddress,
            session.CreatedAt,
            session.LastSeenAt,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            session.MfaSatisfied,
            session.Id == currentSessionId));
    }

    private SessionLifetime Lifetime()
        => new(options.Value.IdleTimeout, options.Value.AbsoluteLifetime);

    private static Result<IssuedSession> Issued(Session session, string token)
        => Result.Success(new IssuedSession(
            session.Id,
            token,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            session.MfaSatisfied,
            session.IsSignInComplete));
}
