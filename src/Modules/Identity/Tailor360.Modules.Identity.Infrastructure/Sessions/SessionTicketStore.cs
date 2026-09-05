using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.Modules.Identity.Infrastructure.Sessions;

/// <summary>
/// Resolves the session cookie on every request: one indexed lookup on the digest of the presented
/// value, a revocation and expiry check, and a slide of the inactivity deadline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Revocation is checked here, which is the whole point.</b> "Sign out everywhere", an administrator
/// ending a session and a suspended account are all worth exactly as much as the next request's check.
/// Because the ticket is opaque and carries nothing, there is no cached copy to go stale: the row is
/// read on every request, so a revocation takes effect on the request after it, not at the next
/// sign-in.
/// </para>
/// <para>
/// <b>The account is checked as well as the session.</b> Suspending or deactivating an account is
/// expected to revoke its sessions (#25), but a session that survived that — a race, a partial failure,
/// a row written before the feature existed — must not keep working. Finding one here revokes it, so
/// the repair happens once rather than on every subsequent request.
/// </para>
/// <para>
/// <b>The presented value is never logged.</b> Diagnostics name the session identifier, which is
/// useless without the cookie, and never the cookie itself.
/// </para>
/// </remarks>
/// <param name="context">The Identity context.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Session lifetimes.</param>
/// <param name="logger">Logger.</param>
public sealed class SessionTicketStore(
    IdentityDbContext context,
    IClock clock,
    IOptions<SessionAuthenticationOptions> options,
    ILogger<SessionTicketStore> logger)
    : ISessionTicketStore
{
    private static readonly IReadOnlySet<string> NoPermissions =
        new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<SessionResolution> ResolveAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return SessionResolution.NoTicket;
        }

        var digest = SessionTokenFactory.Digest(presentedToken);

        // One round trip. The session and the facts about its account that the request pipeline needs
        // come back together, because this runs before every authenticated request in the system.
        var row = await context.Sessions
            .Where(session => session.TokenHash == digest)
            .Join(
                context.Users,
                session => session.UserId,
                user => user.Id,
                (session, user) => new SessionRow(session, user.DisplayName, user.Status, user.HomeBranchId))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return SessionResolution.Unknown;
        }

        var now = clock.UtcNow;
        var session = row.Session;

        if (session.RevokedAt is not null)
        {
            return SessionResolution.Revoked;
        }

        if (row.Status != UserStatus.Active)
        {
            logger.LogWarning(
                "Session {SessionId} belongs to an account that is {Status}; the session was revoked.",
                session.Id,
                row.Status);

            session.Revoke(now, SessionEndReason.AccountClosed);
            await context.SaveChangesAsync(cancellationToken);
            return SessionResolution.Revoked;
        }

        if (!session.IsActive(now))
        {
            return SessionResolution.Expired;
        }

        await SlideAsync(session, now, cancellationToken);

        return SessionResolution.Active(new SessionTicket(
            session.Id,
            session.UserId,
            row.DisplayName,
            session.OrganisationId,
            session.ActiveBranchId,
            AssignedBranches(row.HomeBranchId),
            NoPermissions,
            session.MfaSatisfied,
            session.IsSignInComplete,
            session.LastStrongAuthAt,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt));
    }

    /// <summary>
    /// Moves the inactivity deadline forward, but not on every request. The client issues several calls
    /// per interaction and the deadline only has to be accurate to within the write-back interval, so
    /// writing each time would make this the busiest table in the system to no effect.
    /// </summary>
    private async Task SlideAsync(Session session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now - session.LastSeenAt < options.Value.SlidingWriteInterval)
        {
            return;
        }

        var touched = session.Touch(now, options.Value.IdleTimeout);
        if (touched.IsFailure)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The branches the holder may act in. Until issue #24 introduces role and branch assignment, the
    /// only assignment the model records is the account's home branch, so that is what the ticket
    /// carries. The permission set stays empty until then, which means every endpoint that demands a
    /// permission denies — the fail-closed direction, and the reason branch reach cannot over-grant in
    /// the meantime.
    /// </summary>
    private static HashSet<Guid> AssignedBranches(Guid? homeBranchId)
        => homeBranchId is { } branchId
            ? new HashSet<Guid> { branchId }
            : new HashSet<Guid>();

    private sealed record SessionRow(
        Session Session,
        string DisplayName,
        UserStatus Status,
        Guid? HomeBranchId);
}
