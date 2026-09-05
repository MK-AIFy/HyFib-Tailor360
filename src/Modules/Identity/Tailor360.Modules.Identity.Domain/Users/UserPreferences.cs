using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Domain.Users;

/// <summary>How a person wants the interface to behave for them.</summary>
/// <remarks>
/// These live on the account rather than in browser storage on purpose: a tailor who moves between the
/// counter tablet and the workroom desktop should not have to set their language and text size twice,
/// and an administrator answering "why does this person see the interface in Tamil" needs somewhere to
/// look. <c>GET /me</c> returns them, the design system reads them (#50), and the administration
/// screens edit them (#25).
/// </remarks>
public sealed class UserPreferences
{
    /// <summary>The locales this deployment serves.</summary>
    public static IReadOnlySet<string> SupportedLocales { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en-IN", "ta-IN" };

    /// <summary>The locale applied when the holder has expressed no preference.</summary>
    public const string DefaultLocale = "en-IN";

    /// <summary>The longest landing route the store accepts.</summary>
    public const int MaximumLandingRouteLength = 200;

    private UserPreferences()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private UserPreferences(Guid userId, DateTimeOffset now)
    {
        UserId = userId;
        Locale = DefaultLocale;
        TimeZoneId = IndiaTimeZone.Id;
        Theme = InterfaceTheme.System;
        Density = InterfaceDensity.Comfortable;
        UpdatedAt = now;
    }

    /// <summary>The account these preferences belong to, and their key.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The BCP 47 language tag the interface renders in.</summary>
    public string Locale { get; private set; } = DefaultLocale;

    /// <summary>The IANA timezone dates and times are shown in.</summary>
    public string TimeZoneId { get; private set; } = IndiaTimeZone.Id;

    /// <summary>Light, dark, high contrast, or whatever the device asks for.</summary>
    public InterfaceTheme Theme { get; private set; }

    /// <summary>How tightly the interface packs information.</summary>
    public InterfaceDensity Density { get; private set; }

    /// <summary>True when animation should be suppressed beyond what the device already reports.</summary>
    public bool ReducedMotion { get; private set; }

    /// <summary>The route the holder lands on after signing in, when they have chosen one.</summary>
    public string? LandingRoute { get; private set; }

    /// <summary>When the preferences last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the default preferences for a new account.</summary>
    public static UserPreferences CreateDefault(Guid userId, DateTimeOffset now) => new(userId, now);

    /// <summary>Changes the language the interface renders in.</summary>
    public Result SetLocale(string? locale, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(locale) || !SupportedLocales.Contains(locale.Trim()))
        {
            return Result.Failure(IdentityErrors.LocaleNotSupported);
        }

        Locale = locale.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Changes the timezone dates and times are shown in.</summary>
    public Result SetTimeZone(string? timeZoneId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Result.Failure(IdentityErrors.TimeZoneNotRecognised);
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out _))
        {
            return Result.Failure(IdentityErrors.TimeZoneNotRecognised);
        }

        TimeZoneId = timeZoneId.Trim();
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Changes the presentation settings.</summary>
    public Result SetPresentation(
        InterfaceTheme theme,
        InterfaceDensity density,
        bool reducedMotion,
        string? landingRoute,
        DateTimeOffset now)
    {
        var route = landingRoute?.Trim();

        if (route is { Length: > 0 })
        {
            if (route.Length > MaximumLandingRouteLength)
            {
                return Result.Failure(
                    IdentityErrors.TooLong("landingRoute", MaximumLandingRouteLength));
            }

            // A landing route is a path inside this application. Accepting anything else would turn a
            // preference into an open redirect the moment the shell navigated to it after sign-in.
            if (!route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal))
            {
                return Result.Failure(IdentityErrors.Required("landingRoute"));
            }
        }

        Theme = theme;
        Density = density;
        ReducedMotion = reducedMotion;
        LandingRoute = route is { Length: > 0 } ? route : null;
        UpdatedAt = now;

        return Result.Success();
    }
}
