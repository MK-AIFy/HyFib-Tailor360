using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Access;
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
/// <param name="access">The caller's effective roles, permissions and branch assignments.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Session lifetimes.</param>
/// <param name="logger">Logger.</param>
public sealed class SessionTicketStore(
    IdentityDbContext context,
    IUserAccessQuery access,
    IClock clock,
    IOptions<SessionAuthenticationOptions> options,
    ILogger<SessionTicketStore> logger)
    : ISessionTicketStore
{
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

        // The session and the facts about its account that the request pipeline needs come back
        // together, because this runs before every authenticated request in the system. The roles,
        // permissions and branch assignments are read separately, by IUserAccessQuery — see there for
        // why they are read rather than cached.
        var row = await context.Sessions
            .Where(session => session.TokenHash == digest)
            .Join(
                context.Users,
                session => session.UserId,
                user => user.Id,
                (session, user) => new SessionRow(session, user.DisplayName, user.Status))
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

        var effective = await access.ResolveAsync(session.UserId, cancellationToken);

        return SessionResolution.Active(new SessionTicket(
            session.Id,
            session.UserId,
            row.DisplayName,
            session.OrganisationId,
            session.ActiveBranchId,

            // The assignment rows alone. See SessionRow for why the home branch is not unioned in.
            effective.BranchIds,
            effective.Permissions,
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
    /// The row this store reads per request.
    /// </summary>
    /// <remarks>
    /// <b>The home branch is deliberately absent.</b> An earlier form read it and unioned it into the
    /// assigned set, on the reading that it is where a session starts. That made removing somebody from
    /// a branch stop short of removing their reach into it — the branch-transfer case the product
    /// describes — because assignments and <c>users.home_branch_id</c> are edited by two different
    /// administration actions. It also gave two answers to one question: <c>IUserAccessQuery</c>, which
    /// every background job reads, never unioned it, so the same person reached one set of branches over
    /// HTTP and a smaller set in a worker. The home branch is what <c>SessionService</c> opens a new
    /// session onto — a default, not a grant — and reach is the assignment rows alone.
    /// </remarks>
    private sealed record SessionRow(
        Session Session,
        string DisplayName,
        UserStatus Status);
}
