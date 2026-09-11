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
