using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Options;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Scheduling;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Background;
using Tailor360.Worker;
using Tailor360.Worker.Jobs;

namespace Tailor360.UnitTests.Worker;

/// <summary>
/// The shell around the job that destroys expired copies of customers' personal data.
/// </summary>
/// <remarks>
/// <para>
/// The purge itself — which exports have expired and what emptying one does — is asserted by the
/// integration tier against a real database. What is asserted here is the part around it, which had
/// never been executed by anything: that a run happens at start-up rather than one interval later,
/// that an instance which cannot take the lease does no work, that the lease is released even when the
/// work throws, and that a failed pass is reported and does not stop the loop.
/// </para>
/// <para>
/// Those are worth asserting on this job in particular. A pass that silently stops running does not
/// break anything a user can see; it leaves copies of people's names, numbers, addresses and consent
/// histories in the database indefinitely, which is precisely what the expiry exists to prevent, and
/// nothing else in the system would notice.
/// </para>
/// <para>
/// No database and no host: the lease arrives through <see cref="IJobLease"/> and the scope through
/// <see cref="IWorkerScopeFactory"/>, both of which are interfaces for this reason.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CustomerExportPurgeTests
{
    /// <summary>
    /// The job runs once as soon as it starts, rather than waiting out its first interval.
    /// </summary>
    /// <remarks>
    /// A deployment is the moment an installation that was stopped comes back, and the exports that
    /// expired while it was down are the ones that have been sitting there longest. Waiting a quarter
    /// of an hour to notice them would be the wrong way round.
    /// </remarks>
    [Fact]
    public async Task TheJobPurgesOnceAtStartUpWithoutWaitingForItsInterval()
    {
        var store = new StubExportStore();
        var lease = new StubLease(granted: true);
        var logger = new RecordingLogger();
        var service = ServiceUnder(store, lease, logger);

        await RunAsync(service, store.Swept);

        logger.ShouldReportNoFailure();
        store.Passes.ShouldBe(1);
        lease.Acquired.ShouldBe(1);
        lease.Released.ShouldBe(1);
    }

    /// <summary>
    /// A second instance that cannot take the lease does nothing at all.
    /// </summary>
    /// <remarks>
    /// Two workers emptying the same rows would not corrupt anything — purging is idempotent — but
    /// they would write two audit entries per destroyed copy, and the trail would then say a person's
    /// data was destroyed twice, which is not what happened.
    /// </remarks>
    [Fact]
    public async Task AnInstanceThatCannotTakeTheLeaseDoesNoWork()
    {
        var store = new StubExportStore();
        var lease = new StubLease(granted: false);
        var logger = new RecordingLogger();
        var service = ServiceUnder(store, lease, logger);

        // Waiting on the lease rather than on a pass: the whole point is that no pass happens.
        await RunAsync(service, lease.Asked);

        logger.ShouldReportNoFailure();
        store.Passes.ShouldBe(0);
        lease.Acquired.ShouldBe(1);

        // And it does not release a lease it never held.
        lease.Released.ShouldBe(0);
    }

    /// <summary>
    /// The lease is released when the work throws, so one failed pass does not block the job for ever.
    /// </summary>
    [Fact]
    public async Task TheLeaseIsReleasedWhenThePassThrows()
    {
        var store = new StubExportStore { Throw = true };
        var lease = new StubLease(granted: true);
        var service = ServiceUnder(store, lease);

        await RunAsync(service, store.Swept);

        lease.Acquired.ShouldBe(1);
        lease.Released.ShouldBe(1);
    }

    /// <summary>
    /// A pass that fails is survivable: the job does not fall over, and the copies stay for the next
    /// pass to find.
    /// </summary>
    /// <remarks>
    /// The failure mode chosen deliberately: a copy living longer than it should and being reported, in
    /// preference to a copy silently believed destroyed.
    /// </remarks>
    [Fact]
    public async Task AFailedPassDoesNotStopTheJob()
    {
        var store = new StubExportStore { Throw = true };
        var service = ServiceUnder(store, new StubLease(granted: true));

        await Should.NotThrowAsync(() => RunAsync(service, store.Swept));
    }

    /// <summary>
    /// A refusal from the handler is reported rather than thrown, and the lease still comes back.
    /// </summary>
    [Fact]
    public async Task ARefusedPassReleasesTheLeaseAndKeepsGoing()
    {
        var store = new StubExportStore { Fail = true };
        var lease = new StubLease(granted: true);
        var service = ServiceUnder(store, lease);

        await RunAsync(service, store.Swept);

        store.Passes.ShouldBe(1);
        lease.Released.ShouldBe(1);
    }

    /// <summary>
    /// Starts the job, waits for the signal that its first pass reached the point under test, and
    /// stops it.
    /// </summary>
    /// <remarks>
    /// <see cref="BackgroundService.StartAsync"/> does not run the job to any particular point before
    /// it returns — the work happens on the thread pool — so asserting straight after start and stop
    /// races the job and reports whatever happened to have run. Waiting on a signal the job itself
    /// raises is what makes these assertions about the job rather than about scheduling. The timeout is
    /// generous because it should never be reached: if it is, the job did not get where the test says
    /// it did, and a hang would be a worse way to learn that than a failure.
    /// </remarks>
    private static async Task RunAsync(CustomerExportPurgeService service, Task signal)
    {
        await service.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            await signal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static CustomerExportPurgeService ServiceUnder(
        StubExportStore store, StubLease lease, RecordingLogger? logger = null)
    {
        var options = Options.Create(new CustomerExportOptions
        {
            // Long enough that the timer never fires inside the test: what is under test is the
            // start-up pass, and a second pass racing the assertions would make the counts flaky.
            PurgeInterval = TimeSpan.FromHours(1),
        });

        var handler = new CustomerExportHandler(
            store,
            new StubAudit(),
            new FixedClock(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new StubIds(),
            options);

        return new CustomerExportPurgeService(
            new StubScopeFactory(lease, handler),
            Options.Create(new WorkerOptions { InstanceName = "worker-under-test" }),
            options,
            logger ?? new RecordingLogger());
    }

    private sealed class StubLease(bool granted) : IJobLease
    {
        private readonly TaskCompletionSource _asked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Acquired { get; private set; }

        public int Released { get; private set; }

        /// <summary>Completes the first time the job asks for the lease.</summary>
        public Task Asked => _asked.Task;

        public Task<bool> TryAcquireAsync(
            string jobName, string owner, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Acquired++;
            _asked.TrySetResult();

            return Task.FromResult(granted);
        }

        public Task ReleaseAsync(string jobName, string owner, CancellationToken cancellationToken = default)
        {
            Released++;

            return Task.CompletedTask;
        }
    }

    private sealed class StubExportStore : IExportStore
    {
        private readonly TaskCompletionSource _swept =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Passes { get; private set; }

        public bool Throw { get; init; }

        public bool Fail { get; init; }

        /// <summary>Completes the first time the job looks for expired exports.</summary>
        public Task Swept => _swept.Task;

        public Task<IReadOnlyList<CustomerExport>> ExpiredHoldingDataAsync(
            DateTimeOffset asAt, int limit, CancellationToken cancellationToken = default)
        {
            Passes++;
            _swept.TrySetResult();

            if (Throw)
            {
                throw new InvalidOperationException("The database was unreachable.");
            }

            return Task.FromResult<IReadOnlyList<CustomerExport>>([]);
        }

        public Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Fail ? Result.Failure(CustomersErrorStub) : Result.Success());

        public Task<CustomerSubjectData?> GatherAsync(
            Guid customerId, Guid organisationId, CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerSubjectData?>(null);

        public Task<CustomerExport?> FindAsync(
            Guid exportId, Guid customerId, Guid organisationId, CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerExport?>(null);

        public Task<IReadOnlyList<CustomerExport>> LiveForCustomerAsync(
            Guid customerId, Guid organisationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerExport>>([]);

        public void Add(CustomerExport export)
        {
            // Nothing here writes an export.
        }

        private static Error CustomersErrorStub { get; } =
            Error.Conflict("customers.concurrent-change", "Somebody else changed it.");
    }

    /// <summary>
    /// A scope factory that hands out the two services this job resolves, and refuses the rest.
    /// </summary>
    /// <remarks>
    /// The members the job never touches throw rather than returning a plausible empty value. If the
    /// job grows a use for its principal or for a requester-scoped run, this stub should stop
    /// compiling quietly and start failing loudly, because at that point it is no longer a fair stand-in
    /// for the real factory.
    /// </remarks>
    private sealed class StubScopeFactory(StubLease lease, CustomerExportHandler handler) : IWorkerScopeFactory
    {
        public IWorkerScope CreateSystemScope(
            Type jobType, Guid? branchId = null, string? correlationId = null)
            => new StubScope(lease, handler);

        public Task<IWorkerScope> CreateScopeForAsync(
            Type jobType, WorkerJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "The purge job runs on its own authority and never acts for a requester.");

        private sealed class StubScope(StubLease lease, CustomerExportHandler handler)
            : IWorkerScope, IServiceProvider
        {
            public IServiceProvider Services => this;

            public WorkerPrincipal Principal => throw new NotSupportedException(
                "The purge job never reads its principal; the handler it calls takes no caller.");

            // Resolved on access, not in an initialiser. Building a descriptor needs the composed
            // permission catalogue, which a unit test does not have — and an initialiser that threw
            // would throw *inside* the job's own try/catch, which swallows it and reports a failed
            // pass. The test would then be asserting the failure path while claiming to assert the
            // happy one.
            public WorkerJobDescriptor Job => throw new NotSupportedException(
                "The purge job never reads its own declaration at run time.");

            public string? CorrelationId => null;

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IJobLease))
                {
                    return lease;
                }

                return serviceType == typeof(CustomerExportHandler) ? handler : null;
            }

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }

    private sealed class StubAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A logger that keeps what the job reported.
    /// </summary>
    /// <remarks>
    /// The job catches everything so its loop survives a bad pass, which means a mistake in the
    /// arrangement of a test looks exactly like a quiet no-op. Asserting that nothing was reported as
    /// failed is what tells the two apart.
    /// </remarks>
    private sealed class RecordingLogger : ILogger<CustomerExportPurgeService>
    {
        private readonly List<(EventId Id, Exception? Error, string Message)> _entries = [];

        public IReadOnlyList<(EventId Id, Exception? Error, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add((eventId, exception, formatter(state, exception)));
        }

        /// <summary>Nothing was reported as a failed pass.</summary>
        public void ShouldReportNoFailure()
            => _entries
                .Where(entry => entry.Error is not null)
                .Select(entry => entry.Error!.ToString())
                .ShouldBeEmpty();
    }

    private sealed class StubIds : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, branchTimeZone).DateTime);
    }
}
