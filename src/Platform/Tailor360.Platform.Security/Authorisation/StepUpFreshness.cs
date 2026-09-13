using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The one reading of "recently re-authenticated": a session is fresh while its last strong
/// re-authentication is within the configured window. The two authorisation handlers apply it to an
/// endpoint's declared permission; a service that exercises a step-up permission on the strength of a
/// request body — an override inside a pricing request — applies the same reading, so the catalogue's
/// <c>RequiresStepUp</c> is honoured wherever the permission is used, not only where it is declared.
/// </summary>
public static class StepUpFreshness
{
    /// <summary>Whether the caller's last strong re-authentication is within the window.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The window.</param>
    /// <returns>True when fresh. A caller with no session has nothing to be fresh.</returns>
    public static bool IsFresh(ICurrentUser user, IClock clock, StepUpOptions options)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        return user.IsAuthenticated
               && user.LastReauthenticatedAt is { } last
               && clock.UtcNow - last <= options.Freshness;
    }
}
