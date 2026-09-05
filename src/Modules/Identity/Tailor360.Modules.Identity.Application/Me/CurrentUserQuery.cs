using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Me;

/// <summary>
/// Everything <c>GET /me</c> tells the client about the account itself, as opposed to about the
/// session it is being asked under.
/// </summary>
/// <remarks>
/// The interface cannot render correctly without most of this. The locale decides which message
/// catalogue loads and which direction dates read in; the theme, density and reduced-motion settings
/// decide the shell before the first screen paints, which is why they come from the account and not
/// from browser storage — a person who moves between the counter tablet and the workroom desktop sets
/// them once. The enrolment state is what turns the "set up your authenticator" prompt on.
/// </remarks>
/// <param name="query">Reads the account.</param>
public sealed class CurrentUserQuery(ISignInDirectory query)
{
    /// <summary>Reads one account's own view of itself, or null when it no longer exists.</summary>
    public async Task<CurrentUserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await query.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var preferences = user.Preferences ?? UserPreferences.CreateDefault(user.Id, user.CreatedAt);

        return new CurrentUserProfile(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.Status,
            user.MfaEnrolment,
            user.MustChangePassword,
            user.Totp is { IsConfirmed: true },
            user.Passkeys.Count,
            user.UnusedRecoveryCodeCount,
            new CurrentUserPreferences(
                preferences.Locale,
                preferences.TimeZoneId,
                preferences.Theme,
                preferences.Density,
                preferences.ReducedMotion,
                preferences.LandingRoute));
    }
}

/// <summary>The account's own view of itself.</summary>
/// <param name="UserId">The account.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="Email">
/// The holder's own address. Personal data, returned only to the holder and to nobody else, and never
/// logged.
/// </param>
/// <param name="Status">Whether the account is invited, active or suspended.</param>
/// <param name="MfaEnrolment">How far the account has got with its second factor.</param>
/// <param name="MustChangePassword">True while the holder must set a new password before working.</param>
/// <param name="HasAuthenticator">True when a confirmed authenticator is enrolled.</param>
/// <param name="PasskeyCount">How many passkeys are registered.</param>
/// <param name="UnusedRecoveryCodes">How many unspent recovery codes remain.</param>
/// <param name="Preferences">How the holder wants the interface to behave.</param>
public sealed record CurrentUserProfile(
    Guid UserId,
    string UserName,
    string DisplayName,
    string Email,
    UserStatus Status,
    MfaEnrolmentState MfaEnrolment,
    bool MustChangePassword,
    bool HasAuthenticator,
    int PasskeyCount,
    int UnusedRecoveryCodes,
    CurrentUserPreferences Preferences);

/// <summary>The interface preferences the shell reads before it paints.</summary>
/// <param name="Locale">The BCP 47 language tag.</param>
/// <param name="TimeZoneId">The IANA timezone dates are shown in.</param>
/// <param name="Theme">Light, dark, high contrast or whatever the device asks for.</param>
/// <param name="Density">How tightly the interface packs information.</param>
/// <param name="ReducedMotion">True when animation is suppressed beyond what the device reports.</param>
/// <param name="LandingRoute">Where the holder lands after signing in, when they have chosen.</param>
public sealed record CurrentUserPreferences(
    string Locale,
    string TimeZoneId,
    InterfaceTheme Theme,
    InterfaceDensity Density,
    bool ReducedMotion,
    string? LandingRoute);
