using Shouldly;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.UnitTests.Identity;

/// <summary>
/// The interface preferences returned by <c>GET /me</c> and edited by the administration screens.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserPreferencesTests
{
    private static UserPreferences Preferences()
        => UserPreferences.CreateDefault(IdentityTestData.Id("user"), IdentityTestData.Now);

    [Fact]
    public void ANewAccountStartsInTheDeploymentsDefaultLanguageAndTimezone()
    {
        var preferences = Preferences();

        preferences.Locale.ShouldBe("en-IN");
        preferences.TimeZoneId.ShouldBe(IndiaTimeZone.Id);
        preferences.Theme.ShouldBe(InterfaceTheme.System);
        preferences.TextSize.ShouldBe(InterfaceTextSize.Standard);
        preferences.Density.ShouldBe(InterfaceDensity.Comfortable);
    }

    [Fact]
    public void OnlyTheLanguagesThisDeploymentServesAreAccepted()
    {
        var preferences = Preferences();

        preferences.SetLocale("ta-IN", IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        preferences.SetLocale("fr-FR", IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.LocaleNotSupported);
    }

    [Fact]
    public void AnUnknownTimezoneIdentifierIsRefused()
    {
        var preferences = Preferences();

        preferences.SetTimeZone("Asia/Kolkata", IdentityTestData.Now).IsSuccess.ShouldBeTrue();
        preferences.SetTimeZone("Middle/Earth", IdentityTestData.Now).Error
            .ShouldBe(IdentityErrors.TimeZoneNotRecognised);
    }

    [Theory]
    [InlineData("https://elsewhere.example/steal")]
    [InlineData("//elsewhere.example/steal")]
    [InlineData("orders")]
    public void ALandingRouteThatCouldLeaveTheApplicationIsRefused(string route)
    {
        // The shell navigates here straight after sign-in, so anything but an in-application path
        // would be an open redirect with a session cookie already set.
        var preferences = Preferences();

        preferences.SetPresentation(
            InterfaceTheme.Dark,
            InterfaceTextSize.Standard,
            InterfaceDensity.Compact,
            reducedMotion: true,
            route,
            IdentityTestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AnInApplicationPathIsAccepted()
    {
        var preferences = Preferences();

        preferences.SetPresentation(
            InterfaceTheme.HighContrast,
            InterfaceTextSize.Larger,
            InterfaceDensity.Compact,
            reducedMotion: true,
            "/orders/workboard",
            IdentityTestData.Now).IsSuccess.ShouldBeTrue();

        preferences.LandingRoute.ShouldBe("/orders/workboard");
        preferences.Theme.ShouldBe(InterfaceTheme.HighContrast);
        preferences.TextSize.ShouldBe(InterfaceTextSize.Larger);
        preferences.ReducedMotion.ShouldBeTrue();
    }

    [Theory]
    [InlineData(InterfaceTextSize.Standard)]
    [InlineData(InterfaceTextSize.Large)]
    [InlineData(InterfaceTextSize.Larger)]
    public void SetPresentationStoresEachOfTheThreeTextSizes(InterfaceTextSize textSize)
    {
        var preferences = Preferences();

        preferences.SetPresentation(
            InterfaceTheme.System,
            textSize,
            InterfaceDensity.Comfortable,
            reducedMotion: false,
            landingRoute: null,
            IdentityTestData.Now).IsSuccess.ShouldBeTrue();

        preferences.TextSize.ShouldBe(textSize);
    }
}
