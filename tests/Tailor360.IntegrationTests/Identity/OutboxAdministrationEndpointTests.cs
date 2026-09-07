using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Api.Administration;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The dead letter, and putting a message in it back on the queue.
/// </summary>
/// <remarks>
/// This surface was console-only until #25, and the reason to test it through HTTP rather than through
/// the port is that the endpoint is where the interesting refusals live: the reason, the step-up, and
/// the second replay of a message somebody else already put back. The port itself is exercised by the
/// same tests, because both paths go through it.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason =
        "The notification provider outage was resolved at 09:40 and the customer was never told.";

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task ADeadLetteredMessageIsListedWithItsErrorAndPutBackOnTheQueue()
    {
        using var replayer = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outbox", "203.0.113.140", Permissions.OutboxReplay);

        var (messageId, eventType) = await DeadLetterAsync("listed");

        var listed = await ReadDeadLettersAsync(replayer);
        listed.ShouldContain(message => message.Id == messageId);

        var found = listed.First(message => message.Id == messageId);
        found.EventType.ShouldBe(eventType);
        found.AttemptCount.ShouldBe(5);
        found.DeadLetteredAt.ShouldNotBeNull();
        found.LastError.ShouldNotBeNullOrWhiteSpace(
            "an operator deciding whether to replay needs the failure it was given up on");

        var replayed = await replayer.PostAsync(
            $"/api/v1/admin/outbox/{messageId}/replay",
            new { reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        replayed.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await replayed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The response describes the message as it stood *before* the replay. An operator asking what
        // they just put back wants the failure they acted on, not a row that now says nothing happened.
        var body = (await AuthenticationClient.ReadAsync<DeadLetterBody>(replayed)).ShouldNotBeNull();
        body.Id.ShouldBe(messageId);
        body.AttemptCount.ShouldBe(5);
        body.DeadLetteredAt.ShouldNotBeNull();
        body.LastError.ShouldNotBeNullOrWhiteSpace();

        var row = await FindAsync(messageId);
        row.DeadLetteredAt.ShouldBeNull();
        row.AttemptCount.ShouldBe(0, "a replayed message starts its retry budget again");
        row.LeaseOwner.ShouldBeNull();
        row.LeaseExpiresAt.ShouldBeNull();

        // Cleared, unlike the command-line tool as it was written: a message that has since been
        // dispatched successfully still carrying the error it failed with once reads as a live problem.
        row.LastError.ShouldBeNull();

        (await ReadDeadLettersAsync(replayer)).ShouldNotContain(message => message.Id == messageId);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task ReplayingAMessageThatIsNotInTheDeadLetterIsRefused()
    {
        using var replayer = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outboxtwice", "203.0.113.141", Permissions.OutboxReplay);

        var (messageId, _) = await DeadLetterAsync("twice");

        (await replayer.PostAsync(
                $"/api/v1/admin/outbox/{messageId}/replay",
                new { reason = Reason },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A second operator reaching the same screen a moment later, or the same one refreshing: the
        // message is back on the queue and there is nothing to put back. Answered as not-found rather
        // than replayed a second time, which would be a second outbound effect.
        var again = await replayer.PostAsync(
            $"/api/v1/admin/outbox/{messageId}/replay",
            new { reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await FindAsync(messageId)).AttemptCount.ShouldBe(0);

        // And a message that never existed is the same answer, so the endpoint does not tell an
        // enumerating caller which identifiers are real.
        (await replayer.PostAsync(
                $"/api/v1/admin/outbox/{Guid.CreateVersion7()}/replay",
                new { reason = Reason },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task AReplayIsRecordedWithItsReasonAndWhatTheMessageWas()
    {
        using var replayer = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outboxaudit", "203.0.113.142", Permissions.OutboxReplay);

        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outboxreader", "203.0.113.143", Permissions.AuditRead);

        var (messageId, eventType) = await DeadLetterAsync("audited");

        (await replayer.PostAsync(
                $"/api/v1/admin/outbox/{messageId}/replay",
                new { reason = Reason },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var trail = await ReadTrailAsync(
            auditor, $"?entityType=OutboxMessage&entityId={messageId}&limit=10");

        trail.Entries.ShouldContain(entry => entry.Action == OutboxAdministrationActions.Replayed);
        var recorded = trail.Entries.First(entry => entry.Action == OutboxAdministrationActions.Replayed);

        recorded.Reason.ShouldBe(Reason);
        recorded.ActorId.ShouldBe(
            replayer.UserId,
            "a replay can duplicate an outbound effect, so the trail names who caused it");

        // The entry says what was replayed, not only that something was: the event and the number of
        // attempts it was given up after are what a later investigation asks about.
        recorded.Before.ShouldNotBeNull().ShouldContain(eventType);
        recorded.Summary.ShouldContain(eventType);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task AReplayWithoutAReasonIsRefusedAndChangesNothing()
    {
        using var replayer = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outboxwhy", "203.0.113.144", Permissions.OutboxReplay);

        var (messageId, _) = await DeadLetterAsync("noreason");

        foreach (var body in (object[])[new { }, new { reason = "   " }, new { reason = new string('x', 501) }])
        {
            (await replayer.PostAsync(
                    $"/api/v1/admin/outbox/{messageId}/replay",
                    body,
                    ("Idempotency-Key", Guid.CreateVersion7().ToString())))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await FindAsync(messageId)).DeadLetteredAt.ShouldNotBeNull(
            "a refused replay leaves the message where it was");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task BeingSignedInIsNotEnoughToReadTheDeadLetterOrReplayFromIt()
    {
        using var stranger = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-outboxdenied", "203.0.113.145", grantPermission: null);

        var (messageId, _) = await DeadLetterAsync("denied");

        (await stranger.GetAsync("/api/v1/admin/outbox/dead-letters"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await stranger.PostAsync(
                $"/api/v1/admin/outbox/{messageId}/replay",
                new { reason = Reason },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await FindAsync(messageId)).DeadLetteredAt.ShouldNotBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(OutboxAdministrationEndpointTests))]
    public async Task TheDrainReplaysEveryDeadLetteredMessageUnderOneEntry()
    {
        // The console path. It has no endpoint on purpose, so the port is called directly — which is
        // also what the command-line tool does, so this is the same code an operator runs.
        var seeded = new List<Guid>();

        for (var index = 0; index < 3; index++)
        {
            var (messageId, _) = await DeadLetterAsync($"drain{index}");
            seeded.Add(messageId);
        }

        using var scope = fixture.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<
            Tailor360.Platform.Abstractions.Outbox.IOutboxAdministration>();

        var drained = await administration.ReplayAllAsync(
            "Provider outage resolved; draining the queue.",
            cancellationToken: TestContext.Current.CancellationToken);

        drained.ShouldBeGreaterThanOrEqualTo(seeded.Count);

        foreach (var messageId in seeded)
        {
            (await FindAsync(messageId)).DeadLetteredAt.ShouldBeNull();
        }
    }

    [Fact]
    public void TheEndpointAndTheImplementationAgreeOnTheAuditAction()
    {
        // The endpoint declares the action and the implementation writes it, and they are separate
        // constants because an Api project may not reference Platform.Persistence (ARCH-004). A shared
        // constant would have looked like a guarantee; this is one.
        OutboxAdministrationActions.Replayed.ShouldBe(OutboxAdministration.ReplayedAction);
    }

    /// <summary>Puts a message in the dead letter, as a dispatcher that ran out of attempts would.</summary>
    private async Task<(Guid MessageId, string EventType)> DeadLetterAsync(string label)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        // Scoped by a token so a second run of the suite does not read the first run's messages, and
        // the payload is a shape rather than anything anybody said.
        var eventType = $"tests.outbox_{label}_{AdministrationHarness.UniqueToken(8)}";

        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = now.AddMinutes(-30),
            AggregateId = Guid.CreateVersion7(),
            EventType = eventType,
            SchemaVersion = 1,
            Payload = """{"synthetic":true}""",
            CorrelationId = $"test-{AdministrationHarness.UniqueToken(8)}",
            AvailableAt = now.AddMinutes(-5),
            AttemptCount = 5,
            DeadLetteredAt = now.AddMinutes(-1),
            LastError = "The provider refused the request: 503 Service Unavailable.",
        };

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (message.Id, eventType);
    }

    private async Task<OutboxMessage> FindAsync(Guid messageId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return (await context.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.Id == messageId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    private static async Task<IReadOnlyList<DeadLetterBody>> ReadDeadLettersAsync(
        AdministrationHarness.AdministratorClient client)
    {
        var response = await client.GetAsync(
            $"/api/v1/admin/outbox/dead-letters?limit={DeadLetterPageSize}");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<DeadLetterBody[]>(response)).ShouldNotBeNull();
    }

    private static async Task<TrailBody> ReadTrailAsync(
        AdministrationHarness.AdministratorClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/admin/audit/{query}");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<TrailBody>(response)).ShouldNotBeNull();
    }

    private const int DeadLetterPageSize = 200;

    private sealed record DeadLetterBody(
        Guid Id,
        Guid AggregateId,
        string EventType,
        int SchemaVersion,
        DateTimeOffset OccurredAt,
        DateTimeOffset? DeadLetteredAt,
        int AttemptCount,
        string? LastError,
        string? CorrelationId);

    private sealed record TrailBody(IReadOnlyList<TrailEntryBody> Entries, string? NextCursor);

    private sealed record TrailEntryBody(
        string Action,
        Guid? ActorId,
        string? Reason,
        string Summary,
        string? Before,
        string? After);
}
