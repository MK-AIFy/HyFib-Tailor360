using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Platform.Abstractions.Idempotency;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Concurrency;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Persistence.Idempotency;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// A running application whose endpoints are the shapes the idempotency, concurrency and timeout
/// contracts are defined over: a payment that must happen once, an editable record two people can edit,
/// and a command that runs longer than the server is prepared to wait.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the endpoints are declared here.</b> No module publishes a payment, a scan or an editable
/// aggregate yet — those arrive with issues #36, #43 and the rest of Wave 3. Waiting for them would mean
/// the mechanism every one of those endpoints is going to rely on shipped without anyone ever having
/// watched a retried payment not become a second payment, and the first module to write a command would
/// be the one discovering whether any of this worked.
/// </para>
/// <para>
/// <b>Everything under the endpoints is real.</b> A real PostgreSQL database with the real migrations,
/// the real record store, the real endpoint filters, the real request-timeout policies and the real
/// problem envelope. What is stand-in is only the handler bodies and the caller, which is a header
/// rather than a session cookie — because who the caller is has its own tier of tests, and repeating
/// them here would only make these ones slower and more fragile.
/// </para>
/// </remarks>
public sealed class CommandSafetyApplication : IAsyncLifetime
{
    /// <summary>The event type the probe payment endpoint writes; counting these counts payments.</summary>
    public const string PaymentEventType = "probe.payment_taken";

    /// <summary>The event type the slow write endpoint writes before it runs out of time.</summary>
    public const string SlowWriteEventType = "probe.slow_write";

    /// <summary>The header the probe reads the caller from.</summary>
    public const string PrincipalHeader = "X-Probe-Principal";

    /// <summary>A request-timeout policy short enough for a test to wait out.</summary>
    public const string FastTimeoutPolicy = "probe-fast";

    /// <summary>
    /// How long <see cref="FastTimeoutPolicy" /> gives a request.
    /// </summary>
    /// <remarks>
    /// It has to cover more than the handler. Everything an endpoint filter does happens inside the
    /// request, so a duplicate's wait, and the database round-trip each of its polls makes, are spent out
    /// of this budget too — and a duplicate that runs out of it is answered with a timeout instead of the
    /// conflict it was waiting to be told about. Two seconds is short enough for a test to wait out and
    /// long enough that the answer does not depend on how loaded the machine is; a quarter of a second
    /// was not, and failed in continuous integration while passing on every developer machine.
    /// </remarks>
    public static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds(2);

    private static readonly DateTimeOffset Start = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private string? _databaseName;
    private IHost? _host;

    /// <summary>True when a PostgreSQL instance was found and the probes can run.</summary>
    public static bool IsAvailable => DatabaseAvailability.IsAvailable;

    /// <summary>The clock every lease and expiry is measured on, so no test has to wait for one.</summary>
    public TestClock Clock { get; } = new(Start);

    /// <summary>The store's retention and lease settings, mutable so a test can shorten a wait.</summary>
    public IdempotencyOptions StoreOptions { get; } = new();

    /// <summary>The filter's duplicate-wait settings, mutable so a test can shorten a wait.</summary>
    public IdempotencyRequestOptions RequestOptions { get; } = new();

    /// <summary>The connection string of the database this application was built on.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Completes when the gated payment handler has started and is waiting to be let through.</summary>
    public TaskCompletionSource HandlerReachedGate { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set by a test to let the gated payment handler finish.</summary>
    public TaskCompletionSource GateOpened { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A client that sends no caller header, so the probe sees an anonymous request.</summary>
    public HttpClient AnonymousClient { get; private set; } = null!;

    /// <summary>Builds the database and starts the application.</summary>
    public async ValueTask InitializeAsync()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();

        if (!IsAvailable)
        {
            return;
        }

        ConnectionString = await CreateDatabaseAsync();
        await StartHostAsync();
    }

    /// <summary>Stops the application and drops its database.</summary>
    public async ValueTask DisposeAsync()
    {
        AnonymousClient?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        NpgsqlConnection.ClearAllPools();

        if (_databaseName is not null)
        {
            await OnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        }
    }

    /// <summary>A client that acts as the named person.</summary>
    /// <param name="principalId">The caller identity the record is keyed by.</param>
    public HttpClient ClientFor(string principalId)
    {
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Add(PrincipalHeader, principalId);
        return client;
    }

    /// <summary>Re-arms the gate for the next test that uses it.</summary>
    public void ResetGate()
    {
        HandlerReachedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GateOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Opens a context onto the probe database, for a test to assert on what was written.</summary>
    public PlatformDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlatformDbContext(options);
    }

    /// <summary>Counts the payments the probe endpoint has taken for one customer.</summary>
    /// <param name="customerId">The customer whose payments to count.</param>
    public async Task<int> PaymentCountAsync(Guid customerId)
    {
        await using var context = OpenContext();
        return await context.OutboxMessages.CountAsync(
            m => m.EventType == PaymentEventType && m.AggregateId == customerId,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Writes the editable record the concurrency probes act on.</summary>
    /// <param name="key">The record's key, one per test so no two tests edit the same row.</param>
    public async Task SeedFlagAsync(string key)
    {
        await using var context = OpenContext();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Key = key,
            ScopeType = FeatureFlagScopes.Organisation,
            ScopeId = Guid.Empty,
            Enabled = false,
            UpdatedAt = Clock.UtcNow,
            Reason = "Seeded by the concurrency probe.",
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads the editable record straight from the database.</summary>
    /// <param name="key">The record's key.</param>
    public async Task<FeatureFlag> ReadFlagAsync(string key)
    {
        await using var context = OpenContext();
        return await context.FeatureFlags.AsNoTracking()
            .FirstAsync(f => f.Key == key, TestContext.Current.CancellationToken);
    }

    /// <summary>Counts the rows the slow write endpoint left behind.</summary>
    public async Task<int> SlowWriteCountAsync()
    {
        await using var context = OpenContext();
        return await context.OutboxMessages.CountAsync(
            m => m.EventType == SlowWriteEventType, TestContext.Current.CancellationToken);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        _databaseName = $"{DatabaseAvailability.DatabaseNamespace}_cmdsafety";

        await OnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        await OnMaintenanceDatabaseAsync($"CREATE DATABASE {_databaseName}");

        var connectionString = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new PlatformDbContext(options);
        await context.Database.MigrateAsync();

        return connectionString;
    }

    private async Task StartHostAsync()
    {
        var connectionString = ConnectionString;

        _host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Critical));
                    services.AddRouting();
                    services.AddProblemDetails();
                    services.AddHttpContextAccessor();

                    services.AddSingleton<IClock>(Clock);
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IOptions<IdempotencyOptions>>(new OptionsWrapper<IdempotencyOptions>(StoreOptions));
                    services.AddSingleton<IOptions<IdempotencyRequestOptions>>(
                        new OptionsWrapper<IdempotencyRequestOptions>(RequestOptions));

                    services.AddDbContext<PlatformDbContext>(builder => builder
                        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                            ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
                        .UseSnakeCaseNamingConvention());

                    services.AddScoped<IIdempotencyStore, IdempotencyStore>();
                    services.AddScoped<ICurrentUser, HeaderPrincipal>();

                    // The real catalogue, plus one policy short enough for a test to wait out.
                    services.AddTailor360RequestTimeouts();
                    services.AddRequestTimeouts(options => options.AddPolicy(
                        FastTimeoutPolicy,
                        RequestTimeoutPolicies.Create(FastTimeout)));
                })
                .Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseTailor360IdempotencyKeys();
                    app.UseRequestTimeouts();
                    app.UseEndpoints(MapProbes);
                }))
            .Build();

        await _host.StartAsync(TestContext.Current.CancellationToken);
        AnonymousClient = _host.GetTestClient();
    }

    private void MapProbes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/probe/payments", TakePaymentAsync).RequireIdempotency();

        endpoints.MapPost("/probe/payments/gated", async (
            PaymentRequest request,
            PlatformDbContext db,
            IClock clock,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            HandlerReachedGate.TrySetResult();
            await GateOpened.Task.WaitAsync(cancellationToken);
            return await TakePaymentAsync(request, db, clock, http, cancellationToken);
        }).RequireIdempotency();

        // A failure whose outcome nobody can know. The claim must stand until its lease runs out.
        endpoints.MapPost(
            "/probe/payments/failing",
            IResult () => throw new InvalidOperationException("The probe was told to fail."))
            .RequireIdempotency();

        endpoints.MapPost("/probe/payments/slow", async (
            CancellationToken cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Results.Ok();
        }).RequireIdempotency().WithRequestTimeout(FastTimeoutPolicy);

        endpoints.MapPost("/probe/slow-write", async (
            PlatformDbContext db,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = clock.UtcNow,
                AggregateId = Guid.CreateVersion7(),
                EventType = SlowWriteEventType,
                Payload = "{}",
                AvailableAt = clock.UtcNow,
            });

            await db.SaveChangesAsync(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok();
        }).WithRequestTimeout(FastTimeoutPolicy);

        endpoints.MapGet("/probe/flags/{key}", async (
            string key,
            PlatformDbContext db,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken);
            if (flag is null)
            {
                return Results.NotFound();
            }

            http.Response.SetEntityTag(db.EntityTagOf(flag));
            return Results.Ok(new FlagView(flag.Key, flag.Enabled, flag.Reason));
        });

        endpoints.MapPut("/probe/flags/{key}", async (
            string key,
            FlagUpdate update,
            PlatformDbContext db,
            HttpContext http,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken);
            if (flag is null)
            {
                return Results.NotFound();
            }

            var refusal = ConcurrencyResults.CheckIfMatch(
                http,
                db.EntityTagOf(flag),
                "platform.version-conflict",
                "This setting was changed by somebody else. Reload to see the current value.");

            if (refusal is not null)
            {
                return refusal;
            }

            flag.Enabled = update.Enabled;
            flag.Reason = update.Reason;
            flag.UpdatedAt = clock.UtcNow;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The precondition held when it was checked and the row moved before the write landed.
                // The answer is the same one, built from what the row says now.
                db.ChangeTracker.Clear();
                var current = await db.FeatureFlags.AsNoTracking()
                    .FirstAsync(f => f.Key == key, cancellationToken);

                return ConcurrencyResults.VersionConflict(
                    http,
                    "platform.version-conflict",
                    "This setting was changed by somebody else. Reload to see the current value.",
                    db.EntityTagOf(current));
            }

            http.Response.SetEntityTag(db.EntityTagOf(flag));
            return Results.Ok(new FlagView(flag.Key, flag.Enabled, flag.Reason));
        }).RequireIfMatch();
    }

    private static async Task<IResult> TakePaymentAsync(
        PaymentRequest request,
        PlatformDbContext db,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // A refusal that changes nothing, so that the key is free for the client to correct the request
        // and send it again — which is what the design system's error handling tells it to do.
        if (request.Amount <= 0)
        {
            return ProblemResults.From(
                http,
                StatusCodes.Status400BadRequest,
                "probe.amount-invalid",
                "That request could not be accepted",
                "The amount has to be more than nothing.");
        }

        var paymentId = Guid.CreateVersion7();

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = paymentId,
            OccurredAt = clock.UtcNow,
            AggregateId = request.CustomerId,
            EventType = PaymentEventType,
            Payload = JsonSerializer.Serialize(new { amount = request.Amount }),
            AvailableAt = clock.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);

        return Results.Json(
            new PaymentReceipt(paymentId, request.Amount),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task OnMaintenanceDatabaseAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The caller, taken from a header. Only the identity matters here: the record is keyed by it, and
    /// whether the person may act at all is decided by the authorisation tier, before this filter runs.
    /// </summary>
    private sealed class HeaderPrincipal(IHttpContextAccessor accessor) : ICurrentUser
    {
        private string Header =>
            accessor.HttpContext?.Request.Headers[PrincipalHeader].ToString() ?? string.Empty;

        public bool IsAuthenticated => Header.Length > 0;

        public Guid UserId => Guid.Empty;

        public string PrincipalId => IsAuthenticated ? Header : "anonymous";

        public string DisplayName => PrincipalId;

        public OrganisationContext Context { get; } = new(Guid.Empty, null);

        public IReadOnlySet<Guid> AssignedBranches { get; } = new HashSet<Guid>();

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool MfaSatisfied => IsAuthenticated;

        public bool IsSignInComplete => IsAuthenticated;

        public DateTimeOffset? LastReauthenticatedAt => null;

        public bool HasPermission(string permissionKey) => IsAuthenticated;

        public bool CanActInBranch(Guid branchId) => IsAuthenticated;
    }
}

/// <summary>What the probe payment endpoint is asked to take.</summary>
/// <param name="CustomerId">Who is paying.</param>
/// <param name="Amount">How much, in whole rupees.</param>
public sealed record PaymentRequest(Guid CustomerId, int Amount);

/// <summary>What the probe payment endpoint answers with.</summary>
/// <param name="PaymentId">The payment that was recorded.</param>
/// <param name="Amount">How much was taken.</param>
public sealed record PaymentReceipt(Guid PaymentId, int Amount);

/// <summary>A change to the probe's editable record.</summary>
/// <param name="Enabled">The new value.</param>
/// <param name="Reason">Why it was changed.</param>
public sealed record FlagUpdate(bool Enabled, string Reason);

/// <summary>The probe's editable record as it is read back.</summary>
/// <param name="Key">Which record.</param>
/// <param name="Enabled">Its value.</param>
/// <param name="Reason">Why it was last changed.</param>
public sealed record FlagView(string Key, bool Enabled, string? Reason);

/// <summary>The collection that shares one probe application.</summary>
[CollectionDefinition(Name)]
public sealed class CommandSafetyCollection : ICollectionFixture<CommandSafetyApplication>
{
    /// <summary>The collection name.</summary>
    public const string Name = "command-safety";
}
