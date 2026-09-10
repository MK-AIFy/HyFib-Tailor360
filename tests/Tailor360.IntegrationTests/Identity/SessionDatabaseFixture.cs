using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Infrastructure.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Sessions;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Gives the session tests a real, migrated <c>identity</c> schema. The session model is the one place
/// where a check constraint, a partial index and a unique index on a digest all have to agree with the
/// domain, so these tests run against PostgreSQL rather than against an in-memory substitute.
/// </summary>
public sealed class SessionDatabaseFixture : IAsyncLifetime
{
    private readonly List<string> _createdDatabases = [];

    /// <summary>True when a PostgreSQL instance was found and the fixture can run.</summary>
    public static bool IsAvailable => DatabaseAvailability.IsAvailable;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        return ValueTask.CompletedTask;
    }

    /// <summary>Creates a fresh database with both schemas migrated into it.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        // Namespaced per run. See DatabaseAvailability.DatabaseNamespace.
        var databaseName =
            $"{DatabaseAvailability.DatabaseNamespace}_s_{name.ToLowerInvariant()}_{_createdDatabases.Count}";

        await ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
        await ExecuteOnMaintenanceDatabaseAsync($"CREATE DATABASE {databaseName}");
        _createdDatabases.Add(databaseName);

        var connectionString = ConnectionStringFor(databaseName);

        // Both contexts, in the order the migration runner applies them. The session table has a
        // foreign key to users, so a partial migration would not create it at all.
        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var platform = new PlatformDbContext(platformOptions))
        {
            await platform.Database.MigrateAsync();
        }

        await using (var identity = CreateContext(connectionString))
        {
            await identity.Database.MigrateAsync();
        }

        return connectionString;
    }

    /// <summary>Opens a context onto a database this fixture created.</summary>
    public static IdentityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, IdentityDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }

    /// <summary>
    /// Builds the real service graph the session code runs under: the same context, the same clock
    /// abstraction and the same options binding as the web host, so the behaviour under test is the
    /// behaviour that ships.
    /// </summary>
    public static ServiceProvider BuildServices(
        string connectionString,
        TestClock clock,
        SessionAuthenticationOptions? sessionOptions = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddSingleton(Options.Create(sessionOptions ?? new SessionAuthenticationOptions()));

        services.AddDbContext<IdentityDbContext>(builder => builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, IdentityDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        // The per-request holder the authentication handler fills, exactly as the web host registers it.
        services.AddScoped<SessionContext>();

        services.AddScoped<IUserAccessQuery, UserAccessQuery>();
        services.AddScoped<ISessionTicketStore, SessionTicketStore>();
        services.AddScoped<ISessionService, SessionService>();

        return services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        foreach (var database in _createdDatabases)
        {
            await ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = database,
        }.ConnectionString;

    /// <summary>
    /// How long a maintenance statement — creating or dropping a scratch database — is given.
    /// </summary>
    /// <remarks>
    /// Npgsql's default is thirty seconds, which is a generic number rather than one chosen for
    /// this. <c>DROP DATABASE ... WITH (FORCE)</c> terminates every backend still attached and
    /// waits for them to go, and the collection fixtures tear down against one PostgreSQL, so on a
    /// loaded runner thirty seconds is reachable — and a fixture that cannot drop its database is
    /// reported as a failure of every test in its collection, over tests that all passed. Two
    /// minutes is room to finish, not room to hide: a statement that truly cannot complete still
    /// fails the run.
    /// </remarks>
    private const int MaintenanceCommandTimeoutSeconds = 120;

    private static async Task ExecuteOnMaintenanceDatabaseAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
            CommandTimeout = MaintenanceCommandTimeoutSeconds,
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// A clock the test moves by hand. Session expiry is the behaviour under test, and a test that waited
/// for thirty real minutes would never be run.
/// </summary>
/// <param name="start">The instant the clock starts at.</param>
public sealed class TestClock(DateTimeOffset start) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = start;

    /// <inheritdoc />
    public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>The collection that shares one session database fixture.</summary>
[CollectionDefinition(Name)]
public sealed class SessionDatabaseCollection : ICollectionFixture<SessionDatabaseFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "identity-sessions";
}
