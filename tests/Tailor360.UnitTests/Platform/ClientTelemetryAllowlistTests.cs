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

    /// <summary>
    /// The property this allowlist exists to guarantee: whatever an attribute bag contains — any name,
    /// any shape, any count — the survivors for a given event type can never carry a name that type does
    /// not declare. The example-based tests above pin individual shapes; this one throws unstructured
    /// noise at <see cref="ClientTelemetryAllowlist.Filter"/> so a future attribute added to one type's
    /// dictionary without a matching shape check cannot silently leak into another type's output. The
    /// seed is fixed per event type so a failure reproduces deterministically instead of only sometimes,
    /// which matters because <c>CLAUDE.md</c> section 5 requires the suite to pass twice in a row.
    /// </summary>
    [Theory]
    [InlineData(ClientTelemetryAllowlist.UnhandledError, 19_837_001)]
    [InlineData(ClientTelemetryAllowlist.UnhandledRejection, 19_837_002)]
    [InlineData(ClientTelemetryAllowlist.ServiceWorkerFailure, 19_837_003)]
    [InlineData(ClientTelemetryAllowlist.CapabilityDetection, 19_837_004)]
    [InlineData(ClientTelemetryAllowlist.WebVital, 19_837_005)]
    [InlineData(ClientTelemetryAllowlist.NavigationTiming, 19_837_006)]
    public void AnArbitraryAttributeBagNeverSurvivesWithAnAttributeNameOutsideTheEventTypesDeclaredSet(
        string eventType,
        int seed)
    {
        var declaredNames = DeclaredAttributeNames[eventType];
        var random = new Random(seed);

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var bag = RandomBag(random, declaredNames);

            var survivors = ClientTelemetryAllowlist.Filter(eventType, bag);

            survivors.ShouldNotBeNull();
            foreach (var name in survivors.Keys)
            {
                declaredNames.ShouldContain(name, $"'{name}' survived for {eventType} from bag {Describe(bag)}");
            }
        }
    }

    /// <summary>
    /// The attribute names each event type declares, mirrored here from <c>ClientTelemetryAllowlist</c>'s
    /// own private dictionary so the property test can assert against the same closed sets without
    /// exposing them from production code purely for a test to read.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> DeclaredAttributeNames =
        new(StringComparer.Ordinal)
        {
            [ClientTelemetryAllowlist.UnhandledError] = ["messageCode", "stackHash", "routeName"],
            [ClientTelemetryAllowlist.UnhandledRejection] = ["messageCode", "stackHash", "routeName"],
            [ClientTelemetryAllowlist.ServiceWorkerFailure] = ["reasonCode"],
            [ClientTelemetryAllowlist.CapabilityDetection] = ["capability", "result"],
            [ClientTelemetryAllowlist.WebVital] = ["metric", "value"],
            [ClientTelemetryAllowlist.NavigationTiming] = ["metric", "value"],
        };

    /// <summary>
    /// A bag mixing genuine attribute names (paired with arbitrary values, to exercise shape rejection)
    /// with wholly random names (to exercise name rejection), so neither path alone can make the property
    /// test above pass by construction.
    /// </summary>
    private static Dictionary<string, JsonElement> RandomBag(Random random, IReadOnlyList<string> declaredNames)
    {
        var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        for (var i = 0; i < random.Next(0, 12); i++)
        {
            var name = declaredNames.Count > 0 && random.Next(2) == 0
                ? declaredNames[random.Next(declaredNames.Count)]
                : RandomString(random, random.Next(0, 24));

            bag[name] = RandomJsonElement(random);
        }

        return bag;
    }

    private static JsonElement RandomJsonElement(Random random) => random.Next(6) switch
    {
        0 => JsonSerializer.SerializeToElement(RandomString(random, random.Next(0, 400))),
        1 => JsonSerializer.SerializeToElement((random.NextDouble() * 2_000_000) - 1_000_000),
        2 => JsonSerializer.SerializeToElement(random.Next(-1000, 1000)),
        3 => JsonSerializer.SerializeToElement(random.Next(2) == 0),
        4 => JsonSerializer.SerializeToElement<object?>(null),
        _ => JsonSerializer.SerializeToElement(new[] { RandomString(random, 5), RandomString(random, 5) }),
    };

    private static string RandomString(Random random, int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:/_-@?=&+\n\t";

        var characters = new char[length];
        for (var i = 0; i < length; i++)
        {
            characters[i] = alphabet[random.Next(alphabet.Length)];
        }

        return new string(characters);
    }

    private static string Describe(Dictionary<string, JsonElement> bag)
        => string.Join(", ", bag.Select(entry => $"{entry.Key}={entry.Value}"));

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
