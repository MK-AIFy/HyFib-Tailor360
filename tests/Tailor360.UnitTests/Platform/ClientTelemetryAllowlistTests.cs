using System.Text.Json;
using Shouldly;
using Tailor360.Web.Telemetry;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The allowlist that decides what a client telemetry event may carry — an allowlist, not a denylist
/// (<c>docs/nfr/data-classification.md</c> section 5.18), so the tests that matter are the ones proving
/// nothing outside the closed set survives, not the ones proving the declared shapes do.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClientTelemetryAllowlistTests
{
    [Fact]
    public void AnUnknownEventTypeIsNotFiltered()
        => ClientTelemetryAllowlist.Filter("not_a_real_type", Bag(("anything", Json("\"value\"")))).ShouldBeNull();

    [Fact]
    public void ANullEventTypeIsNotFiltered()
        => ClientTelemetryAllowlist.Filter(null, Bag(("anything", Json("\"value\"")))).ShouldBeNull();

    [Fact]
    public void AKnownTypeWithNoAttributesSurvivesAsAnEmptyBag()
    {
        var survivors = ClientTelemetryAllowlist.Filter(ClientTelemetryAllowlist.WebVital, null);

        survivors.ShouldNotBeNull();
        survivors.ShouldBeEmpty();
    }

    [Fact]
    public void AnAttributeNameNotDeclaredForTheEventTypeIsDropped()
    {
        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.WebVital,
            Bag(
                ("metric", Json("\"LCP\"")),
                ("value", Json("120.5")),
                ("userEmail", Json("\"person@example.com\""))));

        survivors.ShouldNotBeNull();
        survivors.Keys.ShouldBe(["metric", "value"], ignoreOrder: true);
    }

    [Fact]
    public void AnAttributeOfTheWrongShapeIsDropped()
    {
        // "metric" is declared for web_vital but only as a member of WebVitalMetrics; a value outside
        // that closed set must not survive just because the name matches.
        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.WebVital,
            Bag(("metric", Json("\"NOT_A_METRIC\"")), ("value", Json("10"))));

        survivors.ShouldNotBeNull();
        survivors.Keys.ShouldBe(["value"]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(300_001)]
    public void ATimingValueOutsideTheDeclaredRangeIsDropped(double value)
    {
        // NaN and Infinity are excluded here: standard JSON has no token for either, so a real client
        // can never produce a JsonElement holding one — IsNumberInRange's finiteness check is a second
        // line of defence, not a reachable path through this parser.
        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.NavigationTiming,
            Bag(("metric", Json("\"loadEvent\"")), ("value", Json(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));

        survivors.ShouldNotBeNull();
        survivors.Keys.ShouldBe(["metric"]);
    }

    [Fact]
    public void AMessageCodeIsAcceptedOnlyWhenItLooksLikeADottedCode()
    {
        var accepted = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,
            Bag(("messageCode", Json("\"orders.draft.validationfailed\""))));

        accepted.ShouldNotBeNull();
        accepted.Keys.ShouldBe(["messageCode"]);

        var rejected = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,

            // Free text a person typed, exactly what messageCode exists to exclude — never the
            // exception's own message.
            Bag(("messageCode", Json("\"Cannot read properties of undefined (reading 'foo')\""))));

        rejected.ShouldNotBeNull();
        rejected.ShouldBeEmpty();
    }

    [Fact]
    public void AStackHashIsAcceptedOnlyAsHexNeverAsTheStackItself()
    {
        var accepted = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledRejection,
            Bag(("stackHash", Json($"\"{new string('a', 64)}\""))));

        accepted.ShouldNotBeNull();
        accepted.Keys.ShouldBe(["stackHash"]);

        var rejected = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledRejection,
            Bag(("stackHash", Json("\"at Object.foo (bundle.js:42:11)\""))));

        rejected.ShouldNotBeNull();
        rejected.ShouldBeEmpty();
    }

    [Fact]
    public void ARouteNameIsAcceptedOnlyAsANameNeverAsAUrl()
    {
        var accepted = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,
            Bag(("routeName", Json("\"orders/:orderId/draft\""))));

        accepted.ShouldNotBeNull();
        accepted.Keys.ShouldBe(["routeName"]);

        var rejected = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,

            // A URL can carry an identifier, and an identifier can be personal — routeName exists so a
            // full URL is never mistaken for the closed set of route names.
            Bag(("routeName", Json("\"https://app.example.com/orders/019c1f4c-1c9f-4e0e-ab0d-0d9c9b1a2c3d\""))));

        rejected.ShouldNotBeNull();
        rejected.ShouldBeEmpty();
    }

    [Fact]
    public void ARouteNameSegmentThatLooksLikeAResolvedValueIsRejectedEvenWhenEveryCharacterIsOtherwiseAllowed()
    {
        // "9876543210" is built entirely from characters the old pattern allowed — it is what a resolved
        // route parameter (a phone number standing in for :orderId) looks like once the template's own
        // ":" is gone. The pattern must reject it precisely because it is indistinguishable, character by
        // character, from a legitimate route name unless segments are checked individually.
        var rejected = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,
            Bag(("routeName", Json("\"orders/9876543210/draft\""))));

        rejected.ShouldNotBeNull();
        rejected.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("orders/draft")]
    [InlineData("orders/:orderId/draft")]
    [InlineData("not-found")]
    public void SanitizeRouteNameReturnsARouteNameShapedValueUnchanged(string routeName)
        => ClientTelemetryAllowlist.SanitizeRouteName(routeName).ShouldBe(routeName);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orders/9876543210/draft")]
    [InlineData("https://app.example.com/orders/019c1f4c-1c9f-4e0e-ab0d-0d9c9b1a2c3d")]
    public void SanitizeRouteNameReturnsNullForAnythingNotRouteNameShaped(string? routeName)
        => ClientTelemetryAllowlist.SanitizeRouteName(routeName).ShouldBeNull();

    [Fact]
    public void SanitizeRouteNameRejectsAnOverlongValue()
        => ClientTelemetryAllowlist.SanitizeRouteName(new string('a', 201)).ShouldBeNull();

    [Fact]
    public void AnOverlongStringAttributeIsDropped()
    {
        var tooLong = new string('a', 201);

        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.UnhandledError,
            Bag(("routeName", Json($"\"{tooLong}\""))));

        survivors.ShouldNotBeNull();
        survivors.ShouldBeEmpty();
    }

    [Fact]
    public void ACapabilityDetectionAcceptsOnlyDeclaredCapabilitiesAndResults()
    {
        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.CapabilityDetection,
            Bag(("capability", Json("\"cameraScanning\"")), ("result", Json("\"fallback\""))));

        survivors.ShouldNotBeNull();
        survivors.Keys.ShouldBe(["capability", "result"], ignoreOrder: true);
    }

    [Fact]
    public void AServiceWorkerFailureAcceptsOnlyADeclaredReasonCode()
    {
        var survivors = ClientTelemetryAllowlist.Filter(
            ClientTelemetryAllowlist.ServiceWorkerFailure,
            Bag(("reasonCode", Json("\"install_failed\""))));

        survivors.ShouldNotBeNull();
        survivors.Keys.ShouldBe(["reasonCode"]);
    }

    [Fact]
    public void EventTypeNamesListsExactlyTheSixGenericTypes()
        => ClientTelemetryAllowlist.EventTypeNames.ShouldBe(
            [
                ClientTelemetryAllowlist.UnhandledError,
                ClientTelemetryAllowlist.UnhandledRejection,
                ClientTelemetryAllowlist.ServiceWorkerFailure,
                ClientTelemetryAllowlist.CapabilityDetection,
                ClientTelemetryAllowlist.WebVital,
                ClientTelemetryAllowlist.NavigationTiming,
            ],
            ignoreOrder: true);

    private static JsonElement Json(string literal) => JsonDocument.Parse(literal).RootElement;

    private static Dictionary<string, JsonElement> Bag(params (string Name, JsonElement Value)[] members)
    {
        var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in members)
        {
            bag[name] = value;
        }

        return bag;
    }
}
