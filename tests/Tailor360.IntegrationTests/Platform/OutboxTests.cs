using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The outbox guarantees. Two dispatcher instances are the expected deployment, so the tests run two
/// and assert what a consumer would actually observe: no duplicate effect, and no reordering within an
/// aggregate.
/// </summary>
[Collection(PlatformDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxTests(PlatformDatabaseFixture fixture)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => PlatformDatabaseFixture.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task DiscardsTheEventWhenTheChangeRollsBack()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxrollback");
        await using var provider = PlatformServiceHarness.Build(context);

        using (var scope = provider.CreateScope())
        {
            var scoped = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

            await using var transaction = await scoped.Database.BeginTransactionAsync(
                TestContext.Current.CancellationToken);

            await publisher.PublishAsync(SampleEvent.Create(Guid.CreateVersion7()),
                TestContext.Current.CancellationToken);
            await scoped.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // Nobody may hear about work that did not happen. That is the entire reason the event is a row
        // in the same transaction rather than a call made after commit.
        (await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task DeliversAnEventToItsHandlerAndMarksItProcessed()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxdeliver");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        await PublishAsync(provider, Guid.CreateVersion7());

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        var processed = await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        processed.ShouldBe(1);
        handler.Deliveries.Count.ShouldBe(1);

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        message.ProcessedAt.ShouldNotBeNull();
        message.LeaseOwner.ShouldBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task RunningTheSameMessageTwiceHasNoSecondEffect()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxdedupe");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        var messageId = Guid.CreateVersion7();
        await PublishAsync(provider, Guid.CreateVersion7(), messageId);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        // Force the message back into the queue as a redelivery would.
        await context.Database.ExecuteSqlAsync(
            $"UPDATE platform.outbox_messages SET processed_at = NULL WHERE id = {messageId}",
            TestContext.Current.CancellationToken);

        await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        // The handler ran once. At-least-once delivery is what the transport gives; the inbox row is
        // what turns it into at-most-once effect.
        handler.Deliveries.Count.ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task TwoDispatchersNeverProcessTheSameMessage()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxtwo");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        for (var i = 0; i < 12; i++)
        {
            await PublishAsync(provider, Guid.CreateVersion7());
        }

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        var first = dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);
        var second = dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second);

        handler.Deliveries.Select(d => d.MessageId).Distinct().Count()
            .ShouldBe(handler.Deliveries.Count, "no message may be handled twice");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task PreservesOrderWithinOneAggregate()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxorder");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        var aggregateId = Guid.CreateVersion7();
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-5);

        for (var i = 0; i < 4; i++)
        {
            await PublishAsync(provider, aggregateId, occurredAt: occurred.AddSeconds(i));
        }

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        // Four cycles, because only the oldest undelivered message of an aggregate is ever eligible.
        // That is what makes reordering impossible rather than merely unlikely.
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var first = dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);
            var second = dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken);
            await Task.WhenAll(first, second);
        }

        handler.Deliveries.Count.ShouldBe(4);
        handler.Deliveries.Select(d => d.OccurredAt)
            .ShouldBe(handler.Deliveries.Select(d => d.OccurredAt).Order());
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task AnExpiredLeaseMakesTheMessageAvailableAgain()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxlease");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        var messageId = Guid.CreateVersion7();
        await PublishAsync(provider, Guid.CreateVersion7(), messageId);

        // A dispatcher that crashed mid-delivery leaves a lease behind. Once it lapses, another
        // instance must pick the message up without an operator clearing anything.
        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE platform.outbox_messages
                SET lease_owner = 'crashed-instance', lease_expires_at = now() - interval '1 minute'
              WHERE id = {messageId}
             """,
            TestContext.Current.CancellationToken);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        var processed = await dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken);

        processed.ShouldBe(1);
        handler.Deliveries.Count.ShouldBe(1);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task ALiveLeaseHidesTheMessageFromOtherDispatchers()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxheld");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        var messageId = Guid.CreateVersion7();
        await PublishAsync(provider, Guid.CreateVersion7(), messageId);

        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE platform.outbox_messages
                SET lease_owner = 'busy-instance', lease_expires_at = now() + interval '5 minutes'
              WHERE id = {messageId}
             """,
            TestContext.Current.CancellationToken);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        (await dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken)).ShouldBe(0);
        handler.Deliveries.ShouldBeEmpty();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task APoisonMessageIsRetriedThenDeadLetteredAndCanBeReplayed()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxpoison");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder") { ThrowOnHandle = true };
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        var messageId = Guid.CreateVersion7();
        await PublishAsync(provider, Guid.CreateVersion7(), messageId);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

            // Skip past the backoff so the test does not have to wait it out.
            await context.Database.ExecuteSqlAsync(
                $"UPDATE platform.outbox_messages SET available_at = now() WHERE id = {messageId}",
                TestContext.Current.CancellationToken);
        }

        context.ChangeTracker.Clear();
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        message.DeadLetteredAt.ShouldNotBeNull();
        message.LastError.ShouldNotBeNull();
        message.LastError.ShouldNotContain("payload", Case.Insensitive);

        // Replay is what an operator does after fixing the cause; the message must come back live.
        handler.ThrowOnHandle = false;
        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE platform.outbox_messages
                SET dead_lettered_at = NULL, attempt_count = 0, available_at = now()
              WHERE id = {messageId}
             """,
            TestContext.Current.CancellationToken);

        (await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken)).ShouldBe(1);

        context.ChangeTracker.Clear();
        (await context.OutboxMessages.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken))
            .ProcessedAt.ShouldNotBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task AnEventWithNoHandlerIsStillMarkedProcessed()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxnohandler");
        await using var provider = PlatformServiceHarness.Build(context);

        await PublishAsync(provider, Guid.CreateVersion7());

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        (await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken)).ShouldBe(1);

        // Publishing a fact nobody currently consumes is normal; leaving it in the queue for ever would
        // make the backlog alert meaningless.
        context.ChangeTracker.Clear();
        (await context.OutboxMessages.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken))
            .ProcessedAt.ShouldNotBeNull();
    }

    private static async Task PublishAsync(
        IServiceProvider provider,
        Guid aggregateId,
        Guid? messageId = null,
        DateTimeOffset? occurredAt = null)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        await publisher.PublishAsync(
            SampleEvent.Create(aggregateId, messageId, occurredAt),
            TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record SampleEvent(Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId, string Note)
        : IntegrationEvent(EventId, OccurredAt, AggregateId)
    {
        public const string TypeName = "tests.sample_happened";

        public override string EventType => TypeName;

        public static SampleEvent Create(Guid aggregateId, Guid? eventId = null, DateTimeOffset? occurredAt = null)
            => new(eventId ?? Guid.CreateVersion7(), occurredAt ?? DateTimeOffset.UtcNow, aggregateId, "sample");
    }
}
