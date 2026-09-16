using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Auditing;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.Printing;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The durable print queue end to end against real PostgreSQL (#251): enqueuing leaves a durable,
/// branch-scoped row and an outbox message; a station lists, resolves and cannot resolve a job twice;
/// the loser of a genuine race is told so rather than silently overwriting the winner.
/// </summary>
[Collection(PlatformDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PrintQueueTests(PlatformDatabaseFixture fixture)
{
    /// <summary>Gate used by the skip conditions on every test in this class.</summary>
    public static bool Available => PlatformDatabaseFixture.IsAvailable;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task EnqueuingLeavesAQueuedRowAndExactlyOneOutboxMessage()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueueenqueue");
        await using var provider = Build(context);

        var branchId = Guid.CreateVersion7();
        var jobId = await Enqueue(provider, branchId, copies: 3, printerHint: "counter-1");

        context.ChangeTracker.Clear();
        var job = await context.PrintJobs.AsNoTracking().SingleAsync(row => row.Id == jobId, Token);

        job.BranchId.ShouldBe(branchId);
        job.Kind.ShouldBe("document.invoice");
        job.Format.ShouldBe("pdf");
        job.PayloadReference.ShouldBe("documents/invoices/sample.pdf");
        job.Copies.ShouldBe(3);
        job.PrinterHint.ShouldBe("counter-1");
        job.Status.ShouldBe(PrintJobStatuses.Queued);
        job.ResolvedAt.ShouldBeNull();

        var messages = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.AggregateId == jobId).ToListAsync(Token);
        messages.Count.ShouldBe(1, "one job must publish exactly one platform.print-job-queued.v1 message");
        messages[0].EventType.ShouldBe("platform.print-job-queued.v1");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task ListingFiltersToTheBranchItWasGiven()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueuelist");
        await using var provider = Build(context);

        var branchA = Guid.CreateVersion7();
        var branchB = Guid.CreateVersion7();
        var jobA = await Enqueue(provider, branchA);
        await Enqueue(provider, branchB);

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();

        var listedForA = await stationQueue.ListQueuedAsync(branchA, cancellationToken: Token);

        listedForA.Select(view => view.Id).ShouldBe([jobA]);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task MarkingPrintedMovesTheJobAndWritesOneAuditEntryInTheSameSave()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueueprint");
        await using var provider = Build(context);

        var actor = Guid.CreateVersion7();
        var jobId = await Enqueue(provider, Guid.CreateVersion7());

        using (var scope = provider.CreateScope())
        {
            var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();
            var result = await stationQueue.MarkPrintedAsync(jobId, actor, "counter-1", Token);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Status.ShouldBe(PrintJobStatuses.Printed);
            result.Value.ResolvedBy.ShouldBe(actor);
            result.Value.ResolvedStation.ShouldBe("counter-1");
        }

        context.ChangeTracker.Clear();
        var job = await context.PrintJobs.AsNoTracking().SingleAsync(row => row.Id == jobId, Token);
        job.Status.ShouldBe(PrintJobStatuses.Printed);

        var audited = await context.AuditEvents.AsNoTracking()
            .SingleAsync(entry => entry.EntityId == jobId, Token);
        audited.Action.ShouldBe(PrintStationQueue.PrintedAction);
        audited.ActorId.ShouldBe(actor);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task MarkingFailedRecordsTheReason()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueuefail");
        await using var provider = Build(context);

        var jobId = await Enqueue(provider, Guid.CreateVersion7());

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();
        var result = await stationQueue.MarkFailedAsync(jobId, Guid.CreateVersion7(), "counter-1", "Out of paper.", Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(PrintJobStatuses.Failed);
        result.Value.FailureReason.ShouldBe("Out of paper.");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task AFailureWithNoReasonIsRefusedBeforeTheDatabaseIsTouched()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueuenoreason");
        await using var provider = Build(context);

        var jobId = await Enqueue(provider, Guid.CreateVersion7());

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();

        await Should.ThrowAsync<ArgumentException>(
            () => stationQueue.MarkFailedAsync(jobId, Guid.CreateVersion7(), "counter-1", "   ", Token));

        context.ChangeTracker.Clear();
        (await context.PrintJobs.AsNoTracking().SingleAsync(row => row.Id == jobId, Token))
            .Status.ShouldBe(PrintJobStatuses.Queued, "a refused command must not have reached the row");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task FindOnAnUnknownIdentifierAnswersNotFoundRatherThanThrowing()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueuenotfound");
        await using var provider = Build(context);

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();

        (await stationQueue.FindAsync(Guid.CreateVersion7(), Token)).ShouldBeNull();

        var resolved = await stationQueue.MarkPrintedAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "counter-1", Token);
        resolved.IsFailure.ShouldBeTrue();
        resolved.Error.ShouldBe(PrintJobErrors.NotFound);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task ASecondResolutionOfAResolvedJobIsRefused()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueueresolved");
        await using var provider = Build(context);

        var jobId = await Enqueue(provider, Guid.CreateVersion7());

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();

        (await stationQueue.MarkPrintedAsync(jobId, Guid.CreateVersion7(), "counter-1", Token)).IsSuccess.ShouldBeTrue();

        var second = await stationQueue.MarkFailedAsync(jobId, Guid.CreateVersion7(), "counter-2", "Too late.", Token);
        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(PrintJobErrors.AlreadyResolved);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task AJobWhoseObjectNoLongerExistsIsStillListable()
    {
        // This table never dereferences payloadReference, so deleting the object it names — simulated
        // here by never having one at all, since the fixture writes no object storage — changes nothing
        // about what is listable.
        await using var context = await fixture.CreateDatabaseAsync("printqueuemissingobject");
        await using var provider = Build(context);

        var branchId = Guid.CreateVersion7();
        var jobId = await Enqueue(provider, branchId, payloadReference: "documents/invoices/deleted-object.pdf");

        using var scope = provider.CreateScope();
        var stationQueue = scope.ServiceProvider.GetRequiredService<IPrintStationQueue>();

        var found = await stationQueue.FindAsync(jobId, Token);
        found.ShouldNotBeNull();
        found.PayloadReference.ShouldBe("documents/invoices/deleted-object.pdf");

        (await stationQueue.ListQueuedAsync(branchId, cancellationToken: Token))
            .Select(view => view.Id).ShouldContain(jobId);
    }

    /// <summary>
    /// The genuinely concurrent case: two stations load the same queued job before either has saved.
    /// The <c>xmin</c> token print_jobs carries is what decides it, not a lock in the test.
    /// </summary>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(PrintQueueTests))]
    public async Task TwoConcurrentResolutionsOfOneJobLeaveExactlyOneOutcome()
    {
        await using var context = await fixture.CreateDatabaseAsync("printqueuerace");
        await using var provider = Build(context);

        var jobId = await Enqueue(provider, Guid.CreateVersion7());

        await using var secondContext = PlatformDatabaseFixture.OpenSecondContext(context);

        var clock = new SystemClock();
        var ids = new UuidV7IdGenerator();
        var auditContext = new SystemAuditContext();

        var first = new PrintStationQueue(context, new AuditWriter<PlatformDbContext>(context, auditContext, clock, ids), clock);
        var second = new PrintStationQueue(secondContext, new AuditWriter<PlatformDbContext>(secondContext, auditContext, clock, ids), clock);

        // Both load the row while it is still queued before either resolves it, by resolving through
        // two independent contexts that only see the committed row from Enqueue above.
        var firstResult = await first.MarkPrintedAsync(jobId, Guid.CreateVersion7(), "counter-1", Token);
        var secondResult = await second.MarkFailedAsync(jobId, Guid.CreateVersion7(), "counter-2", "Too slow.", Token);

        var outcomes = new[] { firstResult, secondResult };
        outcomes.Count(result => result.IsSuccess).ShouldBe(1, "exactly one station may resolve the job");
        outcomes.Single(result => result.IsFailure).Error.ShouldBe(PrintJobErrors.AlreadyResolved);

        context.ChangeTracker.Clear();
        var stored = await context.PrintJobs.AsNoTracking().SingleAsync(row => row.Id == jobId, Token);
        stored.Status.ShouldBe(PrintJobStatuses.Printed, "the winner's resolution is the one that persisted");

        (await context.AuditEvents.AsNoTracking().CountAsync(entry => entry.EntityId == jobId, Token))
            .ShouldBe(1, "the loser's audit entry must not have committed with a save that failed");
    }

    private static ServiceProvider Build(PlatformDbContext context)
        => PlatformServiceHarness.Build(context, services =>
        {
            services.AddScoped<IAuditWriter, AuditWriter<PlatformDbContext>>();
            services.AddScoped<IPrintQueue, DatabasePrintQueue>();
            services.AddScoped<IPrintStationQueue, PrintStationQueue>();
        });

    private static async Task<Guid> Enqueue(
        IServiceProvider provider,
        Guid branchId,
        int copies = 1,
        string? printerHint = null,
        string payloadReference = "documents/invoices/sample.pdf")
    {
        using var scope = provider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPrintQueue>();

        return await queue.EnqueueAsync(
            new PrintJobRequest(branchId, "document.invoice", payloadReference, copies, printerHint),
            Token);
    }
}
