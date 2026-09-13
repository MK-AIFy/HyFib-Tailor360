using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// Measuring a garment, over HTTP and against real PostgreSQL (issue #121).
/// </summary>
/// <remarks>
/// <para>
/// These run against a real database because most of what is under test is a property of one: a draft is consumed
/// exactly once by a conditional update (INV-MSR-02), a confirmed measurement is unwritable by a trigger
/// (INV-MSR-01), and two counters starting to measure the same garment at once is a race only the database can
/// settle. A test with an in-memory store would prove that the C# refuses what the C# refuses.
/// </para>
/// <para>
/// Every value here is synthetic. Nothing below is a real person's measurement, and the fixtures use round
/// numbers a tape would actually read.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class MeasurementCaptureEndpointTests(WebApplicationFixture fixture)
{
    private const string Drafts = "/api/v1/customers/measurement-drafts";

    [Fact]
    public async Task MeasuresAGarmentAndConfirmsItAsAnImmutableRecord()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-happy", "203.0.113.230");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("HAPPY");
        var draftId = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, draftId, inches: 36m);

        var check = await counter.GetAsync($"{Drafts}/{draftId}/check");
        check.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var body = JsonDocument.Parse(await check.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeTrue();
            body.RootElement.GetProperty("findings").GetArrayLength().ShouldBe(0);
        }

        var confirmed = await ConfirmAsync(counter, draftId);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var measurement = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync(Token));

        measurement.RootElement.GetProperty("versionNumber").GetInt32().ShouldBe(1);
        measurement.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customerId);

        // Stored in millimetres with the unit it was taken in, so a sheet reads back the way the tape did.
        var value = measurement.RootElement.GetProperty("values").EnumerateArray().Single();
        value.GetProperty("key").GetString().ShouldBe("chest_bust");
        value.GetProperty("millimetres").GetDecimal().ShouldBe(914.4m);
        value.GetProperty("enteredUnit").GetString().ShouldBe("Inch");
    }

    [Fact]
    public async Task ConfirmingTheSameDraftTwiceProducesOneMeasurementAndAConflict()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-MSR-02, and the case it exists for: a confirmation whose answer was lost is retried. It must reach
        // the measurement it already made and never make a second one — a customer with two "first" measurements
        // is a customer whose garment could be cut to either.
        using var counter = await CounterAsync("msr-twice", "203.0.113.231");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("TWICE");
        var draftId = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, draftId, inches: 34m);

        var first = await ConfirmAsync(counter, draftId);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await ConfirmAsync(counter, draftId);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("measurements.draft-already-confirmed");

        (await MeasurementCountAsync(customerId)).ShouldBe(1);
    }

    [Fact]
    public async Task RefusesToConfirmAValueOutsideWhatTheFieldCanHold()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-bounds", "203.0.113.232");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("BOUNDS");
        var draftId = await StartAsync(counter, customerId, templateId);

        // A draft accepts it: a half-measured garment is a normal state, and refusing while somebody holds the
        // tape teaches them to type a plausible lie.
        await SaveBodiceAsync(counter, draftId, inches: 300m);

        var check = await counter.GetAsync($"{Drafts}/{draftId}/check");

        using (var body = JsonDocument.Parse(await check.Content.ReadAsStringAsync(Token)))
        {
            body.RootElement.GetProperty("confirmable").GetBoolean().ShouldBeFalse();

            var finding = body.RootElement.GetProperty("findings").EnumerateArray().Single();

            finding.GetProperty("code").GetString().ShouldBe("measurements.value-out-of-bounds");
            finding.GetProperty("field").GetString().ShouldBe("chest_bust");

            // The field, never the value. A problem detail is logged, relayed and read by people the
            // measurement is not for.
            finding.GetProperty("message").GetString()!.ShouldNotContain("300");
        }

        var confirmed = await ConfirmAsync(counter, draftId);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MeasurementCountAsync(customerId)).ShouldBe(0);
    }

    [Fact]
    public async Task RefusesASectionSavedAgainstATagSomebodyElseHasMovedPast()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Drafts are shared within a branch: two people measuring one garment between them is ordinary. Last
        // writer wins would drop half of it, and nobody would find out until the tailor did.
        using var counter = await CounterAsync("msr-shared", "203.0.113.233");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("SHARED");
        var draftId = await StartAsync(counter, customerId, templateId);

        var stale = await TagAsync(counter, draftId);

        await SaveBodiceAsync(counter, draftId, inches: 36m);

        var refused = await counter.PostAsync(
            $"{Drafts}/{draftId}/sections",
            BodiceBody(38m),
            [.. Key(), ("If-Match", stale)]);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("measurements.draft-changed");
    }

    [Fact]
    public async Task RefusesEveryAttemptToChangeAConfirmedMeasurement()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // INV-MSR-01, asserted where it actually holds. The domain type publishes no mutator, so the only way to
        // test the other half is to reach the table the way a repair script would.
        using var counter = await CounterAsync("msr-frozen", "203.0.113.234");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("FROZEN");
        var draftId = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, draftId, inches: 36m);

        var confirmed = await ConfirmAsync(counter, draftId);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync(Token));
        var versionId = body.RootElement.GetProperty("measurementVersionId").GetGuid();

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        var update = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE customers.measurement_versions SET version_number = 99 WHERE id = {0}",
                [versionId],
                Token));

        update.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);

        // The values are the measurement. Freezing the row that owns them and leaving these writable would let
        // somebody change what was measured while the record of who took it stayed put.
        var values = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE customers.measurement_version_values SET millimetres = 1 "
                + "WHERE measurement_version_id = {0}",
                [versionId],
                Token));

        values.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task HandsBackTheMeasuringAlreadyUnderWayRatherThanStartingASecondSet()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-open", "203.0.113.235");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("OPEN");

        var first = await StartAsync(counter, customerId, templateId);
        var second = await StartAsync(counter, customerId, templateId);

        // A branch measures one garment at a time against one template. Two open drafts would leave two people
        // each filling in half of a different one.
        second.ShouldBe(first);
    }

    [Fact]
    public async Task ReadsTheTemplateVersionADraftIsPinnedToWithoutTheAdministrationKey()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The counter holds measurements.capture and none of the template keys, which is the whole point of the
        // route: the wizard renders the version the draft is pinned to, and the administration read would have
        // refused this caller.
        using var counter = await CounterAsync("msr-tmpl", "203.0.113.246");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("TMPL");
        var draftId = await StartAsync(counter, customerId, templateId);

        var administration = await counter.GetAsync($"/api/v1/customers/measurement-templates/{templateId}");
        administration.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var read = await counter.GetAsync($"{Drafts}/{draftId}/template");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("measurementDraftId").GetGuid().ShouldBe(draftId);
        body.RootElement.GetProperty("measurementTemplateId").GetGuid().ShouldBe(templateId);
        body.RootElement.GetProperty("code").GetString()!.ShouldStartWith("MT_CAP_TMPL_");

        var version = body.RootElement.GetProperty("version");
        version.GetProperty("status").GetString().ShouldBe("Published");

        var field = version.GetProperty("fields").EnumerateArray().Single();
        field.GetProperty("key").GetString().ShouldBe("chest_bust");
        field.GetProperty("groupName").GetString().ShouldBe("Bodice");
        field.GetProperty("minimumMillimetres").GetDecimal().ShouldBe(550m);

        // Nothing about the customer rides along: the draft already names them, and the version is a fact about
        // the template. A wizard that needed the customer's name asks the customer routes, which decide who may
        // read it.
        body.RootElement.TryGetProperty("customerId", out _).ShouldBeFalse();
        body.RootElement.TryGetProperty("customer", out _).ShouldBeFalse();

        var missing = await counter.GetAsync($"{Drafts}/{Guid.CreateVersion7()}/template");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReusesAnEarlierMeasurementAndSaysWhereTheNumbersCameFrom()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The acceptance criterion is that reuse is *visibly* confirmed against its source. A pre-filled form
        // that does not say where the numbers came from is the silent reuse #28 forbids.
        using var counter = await CounterAsync("msr-reuse", "203.0.113.236");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("REUSE");
        var first = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, first, inches: 36m);

        var confirmed = await ConfirmAsync(counter, first);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var measurement = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync(Token));
        var sourceId = measurement.RootElement.GetProperty("measurementVersionId").GetGuid();

        var reused = await counter.PostAsync(
            Drafts,
            new
            {
                customerId,
                measurementTemplateId = templateId,
                reuseFromVersionId = sourceId,
            },
            Key());

        reused.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var draft = JsonDocument.Parse(await reused.Content.ReadAsStringAsync(Token));

        draft.RootElement.GetProperty("reusedFromVersionId").GetGuid().ShouldBe(sourceId);

        // Pre-filled, so the tailor is checking numbers rather than re-measuring from scratch.
        var value = draft.RootElement.GetProperty("values").EnumerateArray().Single();
        value.GetProperty("key").GetString().ShouldBe("chest_bust");
        value.GetProperty("millimetres").GetDecimal().ShouldBe(914.4m);
    }

    [Fact]
    public async Task RefusesToReuseAnotherCustomerMeasurements()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Invisible on a screen showing only numbers, which is exactly why it is refused rather than filtered.
        using var counter = await CounterAsync("msr-wrongreuse", "203.0.113.237");

        var mine = await CustomerAsync();
        var theirs = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("WRONGREUSE");

        var draftId = await StartAsync(counter, theirs, templateId);
        await SaveBodiceAsync(counter, draftId, inches: 34m);

        var confirmed = await ConfirmAsync(counter, draftId);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var measurement = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync(Token));
        var theirVersionId = measurement.RootElement.GetProperty("measurementVersionId").GetGuid();

        var refused = await counter.PostAsync(
            Drafts,
            new
            {
                customerId = mine,
                measurementTemplateId = templateId,
                reuseFromVersionId = theirVersionId,
            },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString()
            .ShouldBe("measurements.reuse-source-does-not-match");
    }

    [Fact]
    public async Task CorrectsAMeasurementByMakingANewOneAndLeavesTheOldReadable()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-correct", "203.0.113.238");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("CORRECT");

        var firstDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, firstDraft, inches: 36m);

        using var firstBody = JsonDocument.Parse(
            await (await ConfirmAsync(counter, firstDraft)).Content.ReadAsStringAsync(Token));
        var originalId = firstBody.RootElement.GetProperty("measurementVersionId").GetGuid();

        var secondDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, secondDraft, inches: 37m);

        var corrected = await counter.PostAsync(
            $"{Drafts}/{secondDraft}/confirm",
            new
            {
                reason = "The first was taken over a jacket.",
                correctsVersionId = originalId,
            },
            [.. Key(), ("If-Match", await TagAsync(counter, secondDraft))]);

        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync(Token));

        body.RootElement.GetProperty("correctsVersionId").GetGuid().ShouldBe(originalId);
        body.RootElement.GetProperty("versionNumber").GetInt32().ShouldBe(2);

        // INV-MSR-01: the corrected one stays readable and still renders its own values. A garment cut to it
        // last week was cut to those numbers, whatever the correction says now.
        var original = await counter.GetAsync($"/api/v1/customers/measurements/{originalId}");
        original.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var kept = JsonDocument.Parse(await original.Content.ReadAsStringAsync(Token));
        kept.RootElement.GetProperty("values").EnumerateArray().Single()
            .GetProperty("millimetres").GetDecimal().ShouldBe(914.4m);
    }

    [Fact]
    public async Task RefusesToRecordACorrectionOfAnotherCustomersMeasurement()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-xcorr", "203.0.113.248");

        var templateId = await PublishedTemplateAsync("XCORR");

        // Somebody else's measurement, confirmed and real.
        var otherCustomerId = await CustomerAsync();
        var otherDraft = await StartAsync(counter, otherCustomerId, templateId);
        await SaveBodiceAsync(counter, otherDraft, inches: 34m);
        using var other = JsonDocument.Parse(
            await (await ConfirmAsync(counter, otherDraft)).Content.ReadAsStringAsync(Token));
        var otherVersionId = other.RootElement.GetProperty("measurementVersionId").GetGuid();

        // This customer's draft, confirmed as a "correction" of it: a client-supplied identifier is not trusted
        // to name this customer's measurement, because the version named goes on the record.
        var customerId = await CustomerAsync();
        var draftId = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, draftId, inches: 36m);

        var refused = await counter.PostAsync(
            $"{Drafts}/{draftId}/confirm",
            new { reason = "Re-measured.", correctsVersionId = otherVersionId },
            [.. Key(), ("If-Match", await TagAsync(counter, draftId))]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString()
            .ShouldBe("measurements.correction-subject-does-not-match");

        (await MeasurementCountAsync(customerId)).ShouldBe(0);
    }

    [Fact]
    public async Task RefusesToRecordACorrectionOfTheSameCustomersOtherGarment()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-tcorr", "203.0.113.249");

        var customerId = await CustomerAsync();

        // The same customer, measured for another garment: a real measurement, and still not one a
        // blouse can be a correction of.
        var otherTemplateId = await PublishedTemplateAsync("TCORRA");
        var otherDraft = await StartAsync(counter, customerId, otherTemplateId);
        await SaveBodiceAsync(counter, otherDraft, inches: 34m);
        using var other = JsonDocument.Parse(
            await (await ConfirmAsync(counter, otherDraft)).Content.ReadAsStringAsync(Token));
        var otherVersionId = other.RootElement.GetProperty("measurementVersionId").GetGuid();

        var templateId = await PublishedTemplateAsync("TCORRB");
        var draftId = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, draftId, inches: 36m);

        var refused = await counter.PostAsync(
            $"{Drafts}/{draftId}/confirm",
            new { reason = "Re-measured.", correctsVersionId = otherVersionId },
            [.. Key(), ("If-Match", await TagAsync(counter, draftId))]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString()
            .ShouldBe("measurements.correction-subject-does-not-match");

        (await MeasurementCountAsync(customerId)).ShouldBe(1, "the other garment's measurement stands alone");
    }

    [Fact]
    public async Task RefusesToRecordACorrectionOfAMeasurementThatDoesNotExist()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-ncorr", "203.0.113.250");

        var templateId = await PublishedTemplateAsync("NCORR");
        var customerId = await CustomerAsync();
        var draftId = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, draftId, inches: 36m);

        // A refusal of the request, not a missing resource: the draft exists and the route is right; what
        // is wrong is a value in the body, and it is answered as one so a client can place it on the field.
        var refused = await counter.PostAsync(
            $"{Drafts}/{draftId}/confirm",
            new { reason = "Re-measured.", correctsVersionId = Guid.CreateVersion7() },
            [.. Key(), ("If-Match", await TagAsync(counter, draftId))]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString()
            .ShouldBe("measurements.correction-subject-does-not-match");

        (await MeasurementCountAsync(customerId)).ShouldBe(0);
    }

    [Fact]
    public async Task RefusesACorrectionWithNoReason()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A record of a change to confirmed evidence without a reason is a record of nothing.
        using var counter = await CounterAsync("msr-noreason", "203.0.113.239");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("NOREASON");

        var firstDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, firstDraft, inches: 36m);

        using var firstBody = JsonDocument.Parse(
            await (await ConfirmAsync(counter, firstDraft)).Content.ReadAsStringAsync(Token));
        var originalId = firstBody.RootElement.GetProperty("measurementVersionId").GetGuid();

        var secondDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, secondDraft, inches: 37m);

        var refused = await counter.PostAsync(
            $"{Drafts}/{secondDraft}/confirm",
            new { reason = (string?)null, correctsVersionId = originalId },
            [.. Key(), ("If-Match", await TagAsync(counter, secondDraft))]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ComparesTwoMeasurementsAndListsThemWithoutTheirValues()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-compare", "203.0.113.240");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("COMPARE");

        var firstDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, firstDraft, inches: 36m);

        using var firstBody = JsonDocument.Parse(
            await (await ConfirmAsync(counter, firstDraft)).Content.ReadAsStringAsync(Token));
        var beforeId = firstBody.RootElement.GetProperty("measurementVersionId").GetGuid();

        var secondDraft = await StartAsync(counter, customerId, templateId);
        await SaveBodiceAsync(counter, secondDraft, inches: 37m);

        using var secondBody = JsonDocument.Parse(
            await (await ConfirmAsync(counter, secondDraft)).Content.ReadAsStringAsync(Token));
        var afterId = secondBody.RootElement.GetProperty("measurementVersionId").GetGuid();

        var compared = await counter.GetAsync(
            $"/api/v1/customers/measurements/{beforeId}/compare/{afterId}");

        compared.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var comparison = JsonDocument.Parse(await compared.Content.ReadAsStringAsync(Token));

        comparison.RootElement.GetProperty("changedCount").GetInt32().ShouldBe(1);

        var difference = comparison.RootElement.GetProperty("differences").EnumerateArray().Single();
        difference.GetProperty("key").GetString().ShouldBe("chest_bust");
        difference.GetProperty("change").GetString().ShouldBe("Changed");
        difference.GetProperty("before").GetProperty("millimetres").GetDecimal().ShouldBe(914.4m);
        difference.GetProperty("after").GetProperty("millimetres").GetDecimal().ShouldBe(939.8m);

        var listed = await counter.GetAsync(
            $"/api/v1/customers/{customerId}/measurements?templateId={templateId}");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(Token));
        var rows = list.RootElement.EnumerateArray().ToArray();

        rows.Length.ShouldBe(2);

        // Newest first, because that is the order somebody choosing what to reuse reads in.
        rows[0].GetProperty("versionNumber").GetInt32().ShouldBe(2);

        // And no values at all: a list is for choosing on the date and who took it.
        foreach (var row in rows)
        {
            row.TryGetProperty("values", out _).ShouldBeFalse();
            row.GetProperty("fieldCount").GetInt32().ShouldBe(1);
        }
    }

    [Fact]
    public async Task ReadsTheTemplateVersionAMeasurementRendersThroughAndNamesWhoTookIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-vtmpl", "203.0.113.247");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("VTMPL");
        var draftId = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, draftId, inches: 36m);

        using var confirmed = JsonDocument.Parse(
            await (await ConfirmAsync(counter, draftId)).Content.ReadAsStringAsync(Token));
        var versionId = confirmed.RootElement.GetProperty("measurementVersionId").GetGuid();

        // The comparison screen and a correction start from a confirmed measurement and need the version it was
        // captured under — by way of the measurement, since the administration read refuses this caller.
        var read = await counter.GetAsync($"/api/v1/customers/measurements/{versionId}/template");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("measurementVersionId").GetGuid().ShouldBe(versionId);
        body.RootElement.GetProperty("measurementTemplateId").GetGuid().ShouldBe(templateId);
        body.RootElement.GetProperty("version").GetProperty("status").GetString().ShouldBe("Published");
        body.RootElement.GetProperty("version").GetProperty("fields").EnumerateArray().Single()
            .GetProperty("key").GetString().ShouldBe("chest_bust");
        body.RootElement.TryGetProperty("customerId", out _).ShouldBeFalse();

        // A person choosing which measurement to reuse chooses on the date and on who took it, and an
        // identifier is not a who: the list carries the name the staff directory gives.
        var listed = await counter.GetAsync($"/api/v1/customers/{customerId}/measurements");
        using var list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(Token));
        var row = list.RootElement.EnumerateArray().Single();
        row.GetProperty("takenBy").GetGuid().ShouldNotBe(Guid.Empty);
        row.GetProperty("takenByName").GetString().ShouldNotBeNullOrEmpty();
        confirmed.RootElement.GetProperty("takenByName").GetString()
            .ShouldBe(row.GetProperty("takenByName").GetString());

        var missing = await counter.GetAsync($"/api/v1/customers/measurements/{Guid.CreateVersion7()}/template");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReadsASheetCarryingNothingAboutTheCustomerButHerMeasurements()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("msr-sheet", "203.0.113.241");

        var customerId = await CustomerAsync();
        var templateId = await PublishedTemplateAsync("SHEET");
        var draftId = await StartAsync(counter, customerId, templateId);

        await SaveBodiceAsync(counter, draftId, inches: 36m);

        using var confirmed = JsonDocument.Parse(
            await (await ConfirmAsync(counter, draftId)).Content.ReadAsStringAsync(Token));
        var versionId = confirmed.RootElement.GetProperty("measurementVersionId").GetGuid();

        // The person who took the measurement cannot print it. measurements.read_sheet is a narrower grant on
        // purpose: a sheet is the widest audience a measurement gets, so the right to produce one is held by
        // fewer people than the right to take one, and the two are separate keys rather than one.
        (await counter.GetAsync($"/api/v1/customers/measurements/{versionId}/sheet"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var printer = await AdministrationHarness.AdministratorAsync(
            fixture,
            "msr-print",
            "203.0.113.242",
            CustomersPermissions.ReadMeasurementSheet);

        var sheet = await printer.GetAsync($"/api/v1/customers/measurements/{versionId}/sheet");

        sheet.StatusCode.ShouldBe(HttpStatusCode.OK);

        var text = await sheet.Content.ReadAsStringAsync(Token);

        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("values").GetArrayLength().ShouldBe(1);

        // The version the values render through travels with them (#124): a person holding the sheet key and
        // not the capture key has no other way to the labels, groups and units, and a sheet is one read.
        body.RootElement.GetProperty("templateName").GetString().ShouldNotBeNullOrEmpty();
        var sheetField = body.RootElement.GetProperty("templateVersion").GetProperty("fields").EnumerateArray().Single();
        sheetField.GetProperty("key").GetString().ShouldBe("chest_bust");
        sheetField.GetProperty("label").GetString().ShouldBe("Chest (bust)");

        // And who took it is a name, resolved through the staff directory, not an identifier a tailor cannot read.
        body.RootElement.GetProperty("takenByName").GetString().ShouldNotBeNullOrEmpty();

        // A tailor holding a printed sheet is the widest audience any measurement gets. Asserted rather than
        // eyeballed: no contact field of any name reaches this payload.
        foreach (var forbidden in new[]
                 {
                     "displayName", "nativeName", "phoneE164", "alternatePhoneE164", "email", "addressLine",
                     "city", "postalCode",
                 })
        {
            text.ShouldNotContain(forbidden, Case.Insensitive);
        }

        // INV-MSR-06: the access is recorded explicitly, not left to a request log that rolls over.
        using var scope = fixture.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        (await audit.AuditEvents.CountAsync(
            entry => entry.EntityId == customerId
                     && entry.Action == MeasurementCaptureHandler.SheetReadAction,
            Token)).ShouldBe(1);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (string Name, string Value)[] Key()
        => [("Idempotency-Key", Guid.CreateVersion7().ToString())];

    private Task<AdministrationHarness.AdministratorClient> CounterAsync(string prefix, string address)
        => AdministrationHarness.AdministratorAsync(
            fixture,
            prefix,
            address,
            CustomersPermissions.CaptureMeasurements,
            CustomersPermissions.Create,
            CustomersPermissions.Read,
            CustomersPermissions.ReadContact,
            CustomersPermissions.ReadConsent);

    private static async Task<string> TagAsync(
        AdministrationHarness.AdministratorClient client, Guid draftId)
    {
        using var read = await client.GetAsync($"{Drafts}/{draftId}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull();

        return read.Headers.ETag.ToString();
    }

    private static object BodiceBody(decimal inches)
        => new
        {
            groupName = "Bodice",
            values = new[]
            {
                new
                {
                    key = "chest_bust",
                    entered = inches,
                    unit = "Inch",
                    choice = (string?)null,
                    acknowledged = false,
                },
            },
        };

    private static async Task SaveBodiceAsync(
        AdministrationHarness.AdministratorClient client, Guid draftId, decimal inches)
    {
        var saved = await client.PostAsync(
            $"{Drafts}/{draftId}/sections",
            BodiceBody(inches),
            [.. Key(), ("If-Match", await TagAsync(client, draftId))]);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(
        AdministrationHarness.AdministratorClient client, Guid draftId)
        => await client.PostAsync(
            $"{Drafts}/{draftId}/confirm",
            new { reason = (string?)null, correctsVersionId = (Guid?)null },
            [.. Key(), ("If-Match", await TagAsync(client, draftId))]);

    private static async Task<Guid> StartAsync(
        AdministrationHarness.AdministratorClient client, Guid customerId, Guid templateId)
    {
        var started = await client.PostAsync(
            Drafts,
            new
            {
                customerId,
                measurementTemplateId = templateId,
                reuseFromVersionId = (Guid?)null,
            },
            Key());

        started.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync(Token));

        return body.RootElement.GetProperty("measurementDraftId").GetGuid();
    }

    private async Task<int> MeasurementCountAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        return await context.MeasurementVersions.CountAsync(
            version => version.CustomerId == customerId, Token);
    }

    /// <summary>A synthetic customer who has agreed to their measurements being kept.</summary>
    /// <remarks>
    /// Through the harness rather than over HTTP: registering a customer is a fixture for these tests, not the
    /// thing they are testing, and it has its own routes and its own tests. The consent is not decoration —
    /// INV-MSR-05 refuses a confirmation without it, so a fixture that skipped it would make every test below
    /// fail for the wrong reason.
    /// </remarks>
    private async Task<Guid> CustomerAsync()
    {
        var customerId = await CustomerHarness.CustomerAsync(fixture, SessionTestData.HomeBranchId);

        await CustomerHarness.SeededPurposesAsync(fixture);
        await CustomerHarness.ConsentAsync(
            fixture,
            customerId,
            ConsentPurposeKeys.MeasurementStorage,
            ConsentDecision.Granted,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        return customerId;
    }

    /// <summary>A measurement template with one published version carrying one measured field.</summary>
    private async Task<Guid> PublishedTemplateAsync(string stem)
    {
        using var scope = fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MeasurementTemplateHandler>();
        var organisationId = SessionTestData.OrganisationId;
        var code = $"MT_CAP_{stem}_{AdministrationHarness.UniqueToken(6).ToUpperInvariant()}";

        var template = await handler.CreateAsync(
            new CreateMeasurementTemplateCommand(organisationId, code, code, null, null), Token);

        template.IsSuccess.ShouldBeTrue();

        var templateId = template.Value.Template.Id;

        var draft = await handler.StartDraftAsync(
            new StartTemplateDraftCommand(
                templateId, organisationId, "Version 1", null, DisplayUnit.Inch, null, null),
            Token);

        draft.IsSuccess.ShouldBeTrue();

        var versionId = draft.Value.Version!.Id;

        var field = await handler.SaveFieldAsync(
            new SaveTemplateFieldCommand(
                templateId,
                versionId,
                organisationId,
                null,
                new TemplateFieldDefinition(
                    "chest_bust",
                    "Chest (bust)",
                    null,
                    "Bodice",
                    0,
                    CanonicalUnit.Millimetre,
                    FieldPrecision.Eighths,
                    new ValidationBands(550m, 1500m, 710m, 1270m),
                    true,
                    "Body measurement. Round the fullest part of the bust, tape level at the back.",
                    "blouse_front_v1",
                    null,
                    "Round the fullest part of the bust, tape level at the back.",
                    null,
                    []),
                null,
                null),
            Token);

        field.IsSuccess.ShouldBeTrue();

        var command = new TemplateLifecycleCommand(templateId, versionId, organisationId, "Fixture.", null, null);

        (await handler.SubmitAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.ApproveAsync(command, Token)).IsSuccess.ShouldBeTrue();
        (await handler.PublishAsync(command, Token)).IsSuccess.ShouldBeTrue();

        return templateId;
    }
}
