using System.Text.Json;
using Shouldly;

namespace Tailor360.ContractTests;

/// <summary>
/// The published contract of every integration event: its name, its version, the shape it serialises
/// to, and the schema and example that describe it.
/// </summary>
/// <remarks>
/// <para>
/// An integration event is a published API with no compiler behind it. A subscriber in another module
/// — and, through the relay of #56, outside the deployment entirely — binds to a name and a set of
/// field names, and nothing in the build tells it when either changed. These tests are that
/// compiler: they hold the name to
/// <c>docs/architecture/conventions.md</c> section 5.5, hold the schema to the type, hold the example
/// to the schema, and refuse a payload that has quietly started carrying personal data.
/// </para>
/// <para>
/// The chain matters more than any one link. Type → schema → example means a field added to a record
/// fails until the schema describes it and the example carries it, so the documentation cannot be
/// half-updated; and a schema written for a field that does not exist fails just as loudly, so it
/// cannot describe an event nobody publishes.
/// </para>
/// <para>
/// It closes the "candidate rule, not yet adopted" in
/// <c>docs/architecture/architecture-rules.md</c> section 5, which was waiting only for the first
/// integration event to exist.
/// </para>
/// </remarks>
[Trait("Category", "Contract")]
public sealed class IntegrationEventTests
{
    /// <summary>
    /// The events exist and are discovered. A suite that silently found none would pass every other
    /// test in this class.
    /// </summary>
    [Fact]
    public void TheSolutionPublishesIntegrationEvents()
    {
        var events = IntegrationEventInventory.All();

        events.ShouldNotBeEmpty(
            "No integration event was found. Either none is declared, or the inventory is no longer "
            + "loading the assemblies that hold them — and an empty inventory makes every other test "
            + "in this class vacuous.");

        events.Select(published => published.EventType)
            .ShouldBeUnique("Two events publishing under one wire name cannot both be routed to.");
    }

    /// <summary>
    /// Every wire name is <c>&lt;module&gt;.&lt;event-name&gt;.v&lt;major&gt;</c>, lower case and
    /// hyphenated.
    /// </summary>
    [Fact]
    public void EveryEventNameHasTheShapeTheConventionFixes()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            IntegrationEventInventory.WireName().IsMatch(published.EventType).ShouldBeTrue(
                $"'{published.EventType}' ({published.Type.Name}) is not "
                + "<module>.<event-name>.v<major> in lower case with a hyphenated event name, which "
                + "docs/architecture/conventions.md section 5.5 fixes. A name is the same string in a "
                + "schema file, a routing key and a log line, so it cannot vary by producer.");
        }
    }

    /// <summary>
    /// The major version in the name and the <c>SchemaVersion</c> property agree.
    /// </summary>
    /// <remarks>
    /// Two ways of saying one number, and a subscriber routes on the first while a producer might
    /// remember only the second. Letting them disagree means a payload announcing itself as version 2
    /// arriving at everybody subscribed to version 1.
    /// </remarks>
    [Fact]
    public void EveryEventVersionsItsNameAndItsPayloadTheSameWay()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            var named = IntegrationEventInventory.MajorInName().Match(published.EventType);

            named.Success.ShouldBeTrue(
                $"'{published.EventType}' names no major version, so nothing can agree with "
                + $"{published.Type.Name}.SchemaVersion.");

            int.Parse(named.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                .ShouldBe(
                    published.SchemaVersion,
                    $"'{published.EventType}' names one major version and "
                    + $"{published.Type.Name}.SchemaVersion declares another. A subscriber routes on "
                    + "the name; a producer that bumped only the property would deliver a new shape "
                    + "to everybody subscribed to the old one.");
        }
    }

    /// <summary>
    /// An event is declared in its own module's <c>Contracts</c> project, and its name says so.
    /// </summary>
    /// <remarks>
    /// <c>Contracts</c> is the module's published surface (ARCH-004), and an event is the most
    /// published thing a module has. One declared in <c>Application</c> or <c>Infrastructure</c> could
    /// not be consumed without breaking the boundary; one whose name claimed another module's segment
    /// would put a subscriber's routing on the wrong owner.
    /// </remarks>
    [Fact]
    public void EveryEventIsDeclaredInItsOwnModulesPublishedSurface()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            var assembly = published.Type.Assembly.GetName().Name!;

            assembly.ShouldEndWith(
                ".Contracts",
                Case.Sensitive,
                $"{published.Type.FullName} is an integration event declared in '{assembly}'. An "
                + "event is a module's published surface and belongs in its Contracts project, which "
                + "is the only project another module may reference (ARCH-004).");

            var expected = $"Tailor360.Modules.{Capitalise(published.Module)}.Contracts";

            assembly.ShouldBe(
                expected,
                $"'{published.EventType}' claims the '{published.Module}' module but is declared in "
                + $"'{assembly}'. The name's first segment is how a subscriber knows who owns the "
                + "event, so it has to be the module that publishes it.");
        }
    }

    /// <summary>
    /// Every event has a JSON Schema and an example under <c>docs/integration/events/</c>, named after
    /// its wire name.
    /// </summary>
    [Fact]
    public void EveryEventIsDocumentedByASchemaAndAnExample()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            File.Exists(published.SchemaPath).ShouldBeTrue(
                $"'{published.EventType}' has no JSON Schema. Add "
                + $"docs/integration/events/{published.EventType}.schema.json and a row to that "
                + "directory's README — a subscriber in another repository has the schema and nothing "
                + "else.");

            File.Exists(published.ExamplePath).ShouldBeTrue(
                $"'{published.EventType}' has no example. Add "
                + $"docs/integration/events/{published.EventType}.example.json: a schema says what is "
                + "permitted and an example says what one actually looks like.");
        }
    }

    /// <summary>
    /// The schema describes exactly the properties the event serialises — no more and no fewer — and
    /// stays compatible with the additive changes a major version is allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exact-set match is the link that keeps the documentation honest. A field added to the
    /// record fails until the schema describes it; a schema written for a field nobody publishes fails
    /// just as loudly. The property names come from the real encoder rather than from a convention
    /// restated here, so changing the encoder fails this test rather than silently invalidating every
    /// schema.
    /// </para>
    /// <para>
    /// <strong><c>additionalProperties</c> must be <c>true</c>, and the reason is
    /// <c>conventions.md</c> section 5.5:</strong> within a major version only additive changes are
    /// permitted, which is a promise that adding an optional field does <em>not</em> break a
    /// subscriber. A published schema that closed the object would break exactly that — every
    /// subscriber validating against the schema it fetched last year would reject the first payload
    /// carrying a new field, and the additive path the convention grants would be unusable. Closing
    /// the object also buys this suite nothing: the exact-set match above is what catches a field
    /// added to the record without being documented.
    /// </para>
    /// <para>
    /// <strong><c>required</c> is a subset, not the whole set</strong>, for the mirror of the same
    /// reason. A dead-lettered message replayed months later (<c>OutboxAdministration.ReplayAsync</c>)
    /// carries the payload as it was written, so a schema that required a field added after it was
    /// written would reject a message this system really does emit. What belongs in <c>required</c> is
    /// the set of fields present when the major version was published; a field added within it is
    /// optional forever.
    /// </para>
    /// <para>
    /// This test cannot check that last rule, because a type says nothing about which of its fields
    /// existed a year ago. What it does check is the case that is always wrong — requiring a field the
    /// event does not publish — and that something is required, so a schema cannot quietly become a
    /// shape that accepts an empty object. The rest is a reviewer reading the diff, and
    /// <c>docs/integration/events/README.md</c> section 2 states the rule they are reading against.
    /// </para>
    /// </remarks>
    [Fact]
    public void EverySchemaDescribesExactlyWhatTheEventPutsOnTheWire()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            var schema = ReadJson(published.SchemaPath);

            schema.TryGetProperty("properties", out var properties).ShouldBeTrue(
                $"{published.EventType}.schema.json declares no 'properties'.");

            var described = properties.EnumerateObject().Select(property => property.Name).ToList();

            described.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(
                published.SerialisedProperties.OrderBy(name => name, StringComparer.Ordinal),
                $"{published.EventType}.schema.json describes a different set of properties from the "
                + $"one {published.Type.Name} serialises. The schema is what a subscriber outside "
                + "this repository has; it cannot be a subset or a superset of the payload.");

            schema.TryGetProperty("required", out var required).ShouldBeTrue(
                $"{published.EventType}.schema.json declares no 'required'.");

            var mandated = required.EnumerateArray().Select(element => element.GetString()!).ToList();

            mandated.ShouldNotBeEmpty(
                $"{published.EventType}.schema.json requires nothing, so it would accept an empty "
                + "object as a valid event.");

            mandated.Except(published.SerialisedProperties, StringComparer.Ordinal).ShouldBeEmpty(
                $"{published.EventType}.schema.json requires a property {published.Type.Name} does "
                + "not publish, so no payload this system emits can satisfy it.");

            schema.TryGetProperty("additionalProperties", out var additional).ShouldBeTrue(
                $"{published.EventType}.schema.json does not say whether extra properties are "
                + "allowed.");

            additional.GetBoolean().ShouldBeTrue(
                $"{published.EventType}.schema.json closes the object. conventions.md section 5.5 "
                + "permits additive changes within a major version, and a closed schema makes every "
                + "one of them breaking: a subscriber validating against the schema it fetched before "
                + "the field was added would reject the first payload that carries it.");
        }
    }

    /// <summary>
    /// The example carries every property the schema describes, and the two constants the schema
    /// pins — the wire name and the major version — match the event.
    /// </summary>
    /// <remarks>
    /// Not full JSON Schema validation, which would need a validator this solution does not carry.
    /// It checks the two things an example actually gets wrong: a field that was added to the record
    /// and the schema but never to the example, and an example copied from a neighbouring event whose
    /// <c>eventType</c> was never changed.
    /// </remarks>
    [Fact]
    public void EveryExampleMatchesItsSchemaAndNamesItsOwnEvent()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            var example = ReadJson(published.ExamplePath);

            example.ValueKind.ShouldBe(
                JsonValueKind.Object,
                $"{published.EventType}.example.json is not a JSON object.");

            example.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ShouldBe(
                    published.SerialisedProperties.OrderBy(name => name, StringComparer.Ordinal),
                    $"{published.EventType}.example.json carries a different set of properties from "
                    + "the payload. An example that omits a field is the one a reader copies.");

            example.GetProperty("eventType").GetString().ShouldBe(
                published.EventType,
                $"{published.EventType}.example.json names another event in its eventType, which is "
                + "what copying a neighbouring example and forgetting one line looks like.");

            example.GetProperty("schemaVersion").GetInt32().ShouldBe(
                published.SchemaVersion,
                $"{published.EventType}.example.json declares a schemaVersion the event does not.");
        }
    }

    /// <summary>
    /// No payload carries a field whose name says it holds personal data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/architecture/conventions.md</c> section 5.5 admits "identifiers, codes, statuses,
    /// timestamps, amounts and branch codes only" to a payload unless the event is classified personal
    /// and the subscriber approved for it. An outbox row fans out to every registered handler and
    /// outlives the moment it described, so a name or a telephone number on one is a copy of personal
    /// data in a place nobody thinks of as holding any.
    /// </para>
    /// <para>
    /// The scan is blunt on purpose. The judgement about a particular field belongs to a reviewer
    /// reading the schema; what this catches is the field added without that conversation happening at
    /// all. A payload that genuinely needs one is an approval under
    /// <c>docs/nfr/data-classification.md</c> section 5.3, recorded as an exemption in the same pull
    /// request.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPayloadCarriesAFieldNamedLikePersonalData()
    {
        foreach (var published in IntegrationEventInventory.All())
        {
            if (IntegrationEventInventory.PersonalFieldExemptions.ContainsKey(published.EventType))
            {
                continue;
            }

            foreach (var property in published.SerialisedProperties)
            {
                foreach (var word in IntegrationEventInventory.Words(property))
                {
                    IntegrationEventInventory.PersonalFieldWords.Contains(word).ShouldBeFalse(
                        $"'{published.EventType}' publishes '{property}', whose name says it carries "
                        + "personal data. conventions.md section 5.5 admits identifiers, codes, "
                        + "statuses, timestamps, amounts and branch codes to a payload; anything else "
                        + "needs the approval data-classification.md section 5.3 describes, recorded "
                        + "in IntegrationEventInventory.PersonalFieldExemptions.");
                }
            }
        }
    }

    /// <summary>
    /// Negative control: the name pattern rejects the shapes it is there to reject.
    /// </summary>
    /// <remarks>
    /// Every event in the solution matches today, so the checks above would pass just as happily
    /// against a pattern that matched anything. These are the mistakes actually available to somebody
    /// naming an event — including <c>orders.order_confirmed</c>, which was the example on
    /// <c>IIntegrationEvent</c> until the first real event arrived and is exactly what a reader would
    /// have copied.
    /// </remarks>
    /// <param name="name">A name the pattern must not accept.</param>
    [Theory]
    [InlineData("orders.order_confirmed")]
    [InlineData("customers.consent_recorded.v1")]
    [InlineData("Customers.consent-recorded.v1")]
    [InlineData("customers.ConsentRecorded.v1")]
    [InlineData("customers.consent-recorded")]
    [InlineData("customers.consent-recorded.v0")]
    [InlineData("customers.consent-recorded.v01")]
    [InlineData("customers.consent-recorded.V1")]
    [InlineData("consent-recorded.v1")]
    [InlineData("customers.consent-recorded.v1 ")]
    public void TheNameCheckRejectsAMisnamedEvent(string name)
        => IntegrationEventInventory.WireName().IsMatch(name).ShouldBeFalse(
            $"'{name}' is not <module>.<event-name>.v<major> and the pattern accepted it, so the "
            + "naming check would accept a misnamed event too.");

    /// <summary>
    /// Negative control: the personal-data scan reads the words a reviewer would.
    /// </summary>
    /// <remarks>
    /// The scan passes today because no payload carries such a field. These assertions are what say it
    /// would notice one — and that it does not fire on the identifier-shaped names the payloads
    /// legitimately use, since a check that rejected <c>purposeKey</c> would be turned off within a
    /// week.
    /// </remarks>
    [Fact]
    public void ThePersonalDataScanReadsFieldNamesTheWayAReviewerWould()
    {
        foreach (var caught in new[]
        {
            "name", "customerName", "nativeName", "phone", "customerPhone", "emailAddress",
            "postalAddress", "measurementSheet", "photoKey", "internalNotes",
        })
        {
            IntegrationEventInventory.Words(caught)
                .Any(IntegrationEventInventory.PersonalFieldWords.Contains)
                .ShouldBeTrue(
                    $"'{caught}' names personal data and the scan did not see it, so a payload could "
                    + "start carrying it without anybody being asked.");
        }

        foreach (var allowed in new[]
        {
            "eventId", "occurredAt", "aggregateId", "organisationId", "recordId", "purposeKey",
            "status", "wordingVersion", "branchId", "eventType", "schemaVersion", "wasFirstRecorded",
        })
        {
            IntegrationEventInventory.Words(allowed)
                .Any(IntegrationEventInventory.PersonalFieldWords.Contains)
                .ShouldBeFalse(
                    $"'{allowed}' is an identifier, a code, a status or a timestamp — exactly what "
                    + "conventions.md section 5.5 admits — and the scan rejected it. A check that "
                    + "fires on legitimate names is a check somebody turns off.");
        }
    }

    private static JsonElement ReadJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static string Capitalise(string module)
        => string.Concat(char.ToUpperInvariant(module[0]), module[1..]);
}
