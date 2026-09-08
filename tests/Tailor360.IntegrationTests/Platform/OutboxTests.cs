using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Abstractions.Events;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;
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

            publisher.Publish(SampleEvent.Create(Guid.CreateVersion7()));
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

    /// <summary>
    /// The inbox half of issue #77. A handler stages a write and then fails, and neither the write nor
    /// the row recording that it ran survives.
    /// </summary>
    /// <remarks>
    /// Until #77 the inbox row was written on the platform's context while a handler wrote through its
    /// own module's, so the two were separate transactions: a crash between them left the effect
    /// applied with nothing to say so, and redelivery applied it again. That is precisely the
    /// at-most-once <em>effect</em> ADR-0008 section 4.2 says the row buys, so this asks for it
    /// directly — the handler's writes and its inbox row are one commit or neither.
    /// </remarks>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task AHandlerThatFailsAfterStagingItsWriteLeavesNeitherTheWriteNorTheInboxRow()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxstaged");
        var sequenceKey = "staged-" + Guid.CreateVersion7().ToString("N")[..8];
        var behaviour = new StagingBehaviour(sequenceKey) { Throw = true };

        await using var provider = PlatformServiceHarness.Build(
            context,
            services => services.AddScoped<IOutboxMessageHandler>(sp => new StagingHandler(
                sp.GetRequiredService<PlatformDbContext>(), behaviour)));

        await PublishAsync(provider, Guid.CreateVersion7());

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        behaviour.Invocations.ShouldBe(1);

        context.ChangeTracker.Clear();

        (await context.Sequences.CountAsync(
            row => row.SequenceKey == sequenceKey, TestContext.Current.CancellationToken))
            .ShouldBe(0, "the handler's staged write survived a failure it was part of.");

        (await context.InboxMessages.CountAsync(
            row => row.HandlerName == StagingHandler.Name, TestContext.Current.CancellationToken))
            .ShouldBe(0, "the handler was recorded as having run when it did not.");

        // The message is back on the queue rather than processed, which is what makes the retry below
        // the system's own behaviour rather than the test's.
        var message = await context.OutboxMessages.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        message.ProcessedAt.ShouldBeNull();
        message.LastError.ShouldNotBeNull();
    }

    /// <summary>
    /// And the other side of it: once the handler stops failing, the effect and the row that records
    /// it land together, and a further redelivery does nothing.
    /// </summary>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task AStagedWriteAndItsInboxRowLandTogetherAndOnlyOnce()
    {
        await using var context = await fixture.CreateDatabaseAsync("outboxstagedok");
        var sequenceKey = "staged-" + Guid.CreateVersion7().ToString("N")[..8];
        var behaviour = new StagingBehaviour(sequenceKey);

        await using var provider = PlatformServiceHarness.Build(
            context,
            services => services.AddScoped<IOutboxMessageHandler>(sp => new StagingHandler(
                sp.GetRequiredService<PlatformDbContext>(), behaviour)));

        var messageId = Guid.CreateVersion7();
        await PublishAsync(provider, Guid.CreateVersion7(), messageId);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();

        (await context.Sequences.CountAsync(
            row => row.SequenceKey == sequenceKey, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await context.InboxMessages.CountAsync(
            row => row.HandlerName == StagingHandler.Name, TestContext.Current.CancellationToken))
            .ShouldBe(1);

        // Force the message back into the queue as a lost lease would.
        await context.Database.ExecuteSqlAsync(
            $"UPDATE platform.outbox_messages SET processed_at = NULL WHERE id = {messageId}",
            TestContext.Current.CancellationToken);

        await dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        behaviour.Invocations.ShouldBe(1, "the inbox row did not stop the second delivery.");

        context.ChangeTracker.Clear();

        (await context.Sequences.CountAsync(
            row => row.SequenceKey == sequenceKey, TestContext.Current.CancellationToken)).ShouldBe(1);
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

        // Two dispatchers run together until the queue drains. Only the oldest undelivered message of
        // an aggregate is ever eligible, so a cycle delivers at most one of these four; the loop runs
        // until nothing is left rather than assuming how many cycles that takes, because how many
        // cycles it takes is a timing detail and the ordering guarantee is not.
        for (var cycle = 0; cycle < 20 && handler.Deliveries.Count < 4; cycle++)
        {
            var first = dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);
            var second = dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken);
            await Task.WhenAll(first, second);
        }

        var delivered = handler.Deliveries;
        var ids = delivered.Select(d => d.MessageId).ToList();

        // Reported separately so a failure says which property broke: a repeated identifier is a
        // duplicate delivery, a short count is an undrained queue, and out-of-order timestamps are a
        // reordering. They have entirely different causes.
        ids.Count.ShouldBe(
            ids.Distinct().Count(),
            $"no message may be handled twice; delivered: {string.Join(", ", ids)}");

        ids.Distinct().Count().ShouldBe(4, "the queue should have drained within the cycle budget");

        delivered.Select(d => d.OccurredAt).ShouldBe(delivered.Select(d => d.OccurredAt).Order());
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

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task ManyDispatchersRacingNeverHandleOneMessageTwice()
    {
        // The regression this guards. Eligibility used to be decided in a separate common table
        // expression, which PostgreSQL evaluates against the statement's snapshot. A dispatcher whose
        // statement began just before a rival claim committed would take the row lock after that commit
        // and still see its stale "unleased" view, so both claimed the same message, both found no
        // inbox row, and both ran the handler — duplicate notifications, or worse, duplicate financial
        // effects. Four dispatchers contending for one aggregate is the shape that reproduces it.
        await using var context = await fixture.CreateDatabaseAsync("outboxrace");
        var handler = new RecordingHandler(SampleEvent.TypeName, "test.recorder");
        await using var provider = PlatformServiceHarness.Build(
            context, services => services.AddSingleton<IOutboxMessageHandler>(handler));

        // One aggregate, so exactly one message is eligible at any moment and every dispatcher
        // contends for that same row. Spreading the messages over many aggregates would let each
        // dispatcher find a different row and would barely exercise the race at all.
        const int messageCount = 12;
        var aggregateId = Guid.CreateVersion7();
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-10);

        for (var i = 0; i < messageCount; i++)
        {
            await PublishAsync(provider, aggregateId, occurredAt: occurred.AddSeconds(i));
        }

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        for (var round = 0; round < 60 && handler.Deliveries.Count < messageCount; round++)
        {
            await Task.WhenAll(
                Enumerable.Range(1, 4).Select(n =>
                    dispatcher.RunCycleAsync($"dispatcher-{n}", TestContext.Current.CancellationToken)));
        }

        var ids = handler.Deliveries.Select(d => d.MessageId).ToList();

        ids.Count.ShouldBe(
            ids.Distinct().Count(),
            "no message may be handled twice, however many dispatchers race for it");
        ids.Distinct().Count().ShouldBe(messageCount);

        context.ChangeTracker.Clear();
        (await context.OutboxMessages.CountAsync(
            m => m.ProcessedAt == null, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(OutboxTests))]
    public async Task AHandlerThatOutlivesTheLeaseIsNotRunTwice()
    {
        // The failure this guards against: a handler waiting on a stalled provider outlives its lease,
        // a second dispatcher claims the same message, and both run the handler at once. The inbox row
        // cannot prevent it because it is written only after the handler returns.
        await using var context = await fixture.CreateDatabaseAsync("outboxslowhandler");

        var lease = TimeSpan.FromSeconds(2);
        var handler = new SlowHandler(SampleEvent.TypeName, "test.slow", TimeSpan.FromSeconds(5));
        await using var provider = PlatformServiceHarness.Build(
            context,
            services =>
            {
                services.AddSingleton<IOutboxMessageHandler>(handler);
                services.AddSingleton(Options.Create(new OutboxOptions { LeaseDuration = lease }));
            });

        await PublishAsync(provider, Guid.CreateVersion7());

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        var slow = dispatcher.RunCycleAsync("dispatcher-1", TestContext.Current.CancellationToken);

        // While the first dispatcher is inside the handler, a second one repeatedly tries to claim.
        // Renewal must keep the lease alive across the whole handler, so every attempt finds nothing.
        var stolen = 0;
        while (!slow.IsCompleted)
        {
            stolen += await dispatcher.RunCycleAsync("dispatcher-2", TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
        }

        await slow;

        stolen.ShouldBe(0, "the lease was renewed, so no other dispatcher could claim the message");
        handler.Invocations.ShouldBe(1);

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

        publisher.Publish(SampleEvent.Create(aggregateId, messageId, occurredAt));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A handler that takes longer than a short lease, standing in for a stalled provider.</summary>
    private sealed class SlowHandler(string eventType, string handlerName, TimeSpan delay) : IOutboxMessageHandler
    {
        private int _invocations;

        public string EventType { get; } = eventType;

        public string HandlerName { get; } = handlerName;

        public string Schema { get; } = PlatformDbContext.SchemaName;

        public int Invocations => Volatile.Read(ref _invocations);

        public async Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>What a <see cref="StagingHandler"/> should do, shared across the scopes it runs in.</summary>
    /// <param name="sequenceKey">The row the handler writes, so a test can look for it.</param>
    private sealed class StagingBehaviour(string sequenceKey)
    {
        private int _invocations;

        public string SequenceKey { get; } = sequenceKey;

        /// <summary>Set to make the handler fail after staging its write.</summary>
        public bool Throw { get; init; }

        public int Invocations => Volatile.Read(ref _invocations);

        public void Record() => Interlocked.Increment(ref _invocations);
    }

    /// <summary>
    /// A handler that stages a real write on the module's context and does not save it, which is the
    /// contract <see cref="IOutboxMessageHandler"/> states: the dispatcher commits the writes with the
    /// inbox row that records them.
    /// </summary>
    /// <param name="context">The module's context, the same instance the dispatcher will save.</param>
    /// <param name="behaviour">What to do, and where to count it.</param>
    private sealed class StagingHandler(PlatformDbContext context, StagingBehaviour behaviour)
        : IOutboxMessageHandler
    {
        public const string Name = "test.staging-writer";

        public string EventType => SampleEvent.TypeName;

        public string HandlerName => Name;

        public string Schema => PlatformDbContext.SchemaName;

        public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
        {
            behaviour.Record();

            context.Sequences.Add(new SequenceRow
            {
                SequenceKey = behaviour.SequenceKey,
                Scope = "outbox-test",
                NextValue = 1,
                UpdatedAt = DateTimeOffset.UnixEpoch,
            });

            return behaviour.Throw
                ? throw new InvalidOperationException("staged, then failed")
                : Task.CompletedTask;
        }
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
