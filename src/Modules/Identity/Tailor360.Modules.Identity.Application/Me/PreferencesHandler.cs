using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Me;

/// <summary>
/// Replaces the caller's own interface preferences.
/// </summary>
/// <remarks>
/// The account is always taken from the session and never from the request — every handler behind the
/// self-service endpoints does this, and it is what keeps a body carrying somebody else's identifier
/// from changing anything but the caller's own row. The write is last-write-wins: <c>user_preferences</c>
/// carries a row version, but two of a person's own devices both setting their own text size is not a
/// race either has to be told about, so this goes through <see cref="IIdentityStore.SaveChangesAsync"/>
/// rather than the try-and-report variant.
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="clock">The clock.</param>
public sealed class PreferencesHandler(IIdentityStore store, IClock clock)
{
    /// <summary>The audit action recorded when the caller changes their own preferences.</summary>
    public const string ChangedAction = "identity.preferences.changed";

    /// <summary>
    /// Replaces every preference in one request: a full replacement, never a partial update.
    /// </summary>
    /// <param name="userId">The caller's own account, from the session.</param>
    /// <param name="locale">The BCP 47 language tag.</param>
    /// <param name="timeZoneId">The IANA timezone identifier.</param>
    /// <param name="theme">Light, dark, high contrast or system.</param>
    /// <param name="textSize">100%, 125% or 150%.</param>
    /// <param name="density">Comfortable or compact.</param>
    /// <param name="reducedMotion">True to suppress animation beyond what the device already reports.</param>
    /// <param name="landingRoute">Where the holder lands after signing in, or null to clear it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<CurrentUserPreferences>> UpdateAsync(
        Guid userId,
        string? locale,
        string? timeZoneId,
        InterfaceTheme theme,
        InterfaceTextSize textSize,
        InterfaceDensity density,
        bool reducedMotion,
        string? landingRoute,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            // The session resolved a moment ago and the account has gone: it was deactivated between
            // the authentication handler and here. There is no caller any more to hold preferences for.
            return Result.Failure<CurrentUserPreferences>(IdentityErrors.UserNotFound);
        }

        var now = clock.UtcNow;
        var preferences = user.EnsurePreferences(now);

        var locales = preferences.SetLocale(locale, now);
        if (locales.IsFailure)
        {
            return Result.Failure<CurrentUserPreferences>(locales.Error);
        }

        var timeZones = preferences.SetTimeZone(timeZoneId, now);
        if (timeZones.IsFailure)
        {
            return Result.Failure<CurrentUserPreferences>(timeZones.Error);
        }

        var presentation = preferences.SetPresentation(theme, textSize, density, reducedMotion, landingRoute, now);
        if (presentation.IsFailure)
        {
            return Result.Failure<CurrentUserPreferences>(presentation.Error);
        }

        await store.SaveChangesAsync(cancellationToken);

        return Result.Success(new CurrentUserPreferences(
            preferences.Locale,
            preferences.TimeZoneId,
            preferences.Theme,
            preferences.TextSize,
            preferences.Density,
            preferences.ReducedMotion,
            preferences.LandingRoute));
    }
}
