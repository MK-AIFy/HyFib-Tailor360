using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Sessions;

/// <summary>
/// The two clocks a session runs against: an inactivity timeout that slides forward while the person
/// is working, and an absolute lifetime that never moves.
/// </summary>
/// <remarks>
/// The absolute lifetime is the one that matters. Sliding expiry alone means a session that is kept
/// warm — by a background poll, by a tab left open on a counter tablet overnight, by a stolen cookie
/// being used steadily — never ends. Capping the total life at twelve hours by default bounds how long
/// any single stolen ticket is worth having, and forces a fresh authentication once a working day.
/// </remarks>
/// <param name="IdleTimeout">How long a session may sit unused before it ends.</param>
/// <param name="AbsoluteLifetime">The longest a session may live, however active it is.</param>
public sealed record SessionLifetime(TimeSpan IdleTimeout, TimeSpan AbsoluteLifetime)
{
    /// <summary>The lifetimes issue #23 sets when configuration says nothing else.</summary>
    public static SessionLifetime Default { get; } =
        new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

    /// <summary>Builds a lifetime, refusing combinations that cannot behave as described.</summary>
    public static Result<SessionLifetime> Create(TimeSpan idleTimeout, TimeSpan absoluteLifetime)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return Result.Failure<SessionLifetime>(
                IdentityErrors.SessionLifetimeInvalid("The inactivity timeout must be positive."));
        }

        if (absoluteLifetime <= TimeSpan.Zero)
        {
            return Result.Failure<SessionLifetime>(
                IdentityErrors.SessionLifetimeInvalid("The absolute lifetime must be positive."));
        }

        if (idleTimeout > absoluteLifetime)
        {
            return Result.Failure<SessionLifetime>(IdentityErrors.SessionLifetimeInvalid(
                "The inactivity timeout cannot be longer than the absolute lifetime, or it would " +
                "never take effect."));
        }

        return new SessionLifetime(idleTimeout, absoluteLifetime);
    }
}
