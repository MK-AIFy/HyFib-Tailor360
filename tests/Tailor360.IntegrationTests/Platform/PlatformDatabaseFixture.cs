using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// Gives each test class its own migrated database. Migrating once into a template and cloning it per
/// class keeps the suite fast while giving every class a clean, isolated schema; sharing one database
/// and truncating between tests would not work here because the audit trail is append-only by design.
/// </summary>
public sealed class PlatformDatabaseFixture : IAsyncLifetime
{
    // Namespaced per run. See DatabaseAvailability.DatabaseNamespace: a fixed name means two runs
    // against one cluster drop each other's template mid-migration.
    private static readonly string TemplateDatabase = $"{DatabaseAvailability.DatabaseNamespace}_template";

    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private static bool _templateReady;

    private readonly List<string> _createdDatabases = [];

    /// <summary>True when a PostgreSQL instance was found and the fixture can run.</summary>
    public static bool IsAvailable => DatabaseAvailability.IsAvailable;

    /// <summary>Prepares the migrated template once per test run.</summary>
    public async ValueTask InitializeAsync()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();

        if (!IsAvailable)
        {
            return;
        }

        await TemplateGate.WaitAsync();
        try
        {
            if (_templateReady)
            {
                return;
            }

            await ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {TemplateDatabase} WITH (FORCE)");
            await ExecuteOnMaintenanceDatabaseAsync($"CREATE DATABASE {TemplateDatabase}");

            // The template is migrated over an unpooled connection and every pool is cleared afterwards.
            // PostgreSQL refuses CREATE DATABASE ... TEMPLATE while any session is attached to the
            // template, and a pooled connection lingers long after the context is disposed.
            await using (var context = CreateContext(ConnectionStringFor(TemplateDatabase, pooling: false)))
            {
                await context.Database.MigrateAsync();
            }

            NpgsqlConnection.ClearAllPools();
            _templateReady = true;
        }
        finally
        {
            TemplateGate.Release();
        }
    }

    /// <summary>Creates a fresh database cloned from the migrated template and returns a context for it.</summary>
    public async Task<PlatformDbContext> CreateDatabaseAsync(string name)
    {
        var databaseName = $"{DatabaseAvailability.DatabaseNamespace}_{name.ToLowerInvariant()}_{_createdDatabases.Count}";

        await ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
        await ExecuteOnMaintenanceDatabaseAsync($"CREATE DATABASE {databaseName} TEMPLATE {TemplateDatabase}");
        _createdDatabases.Add(databaseName);

        return CreateContext(ConnectionStringFor(databaseName));
    }

    /// <summary>Opens a second context onto the same database, for concurrency tests.</summary>
    public static PlatformDbContext OpenSecondContext(PlatformDbContext existing)
        => CreateContext(existing.Database.GetConnectionString()!);

    /// <summary>Drops the databases this fixture created.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var database in _createdDatabases)
        {
            await ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static PlatformDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlatformDbContext(options);
    }

    private static string ConnectionStringFor(string database, bool pooling = true)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = database,
            Pooling = pooling,
        };

        return builder.ConnectionString;
    }

    private static async Task ExecuteOnMaintenanceDatabaseAsync(string sql)
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
}

/// <summary>The collection that shares one migrated template.</summary>
[CollectionDefinition(Name)]
public sealed class PlatformDatabaseCollection : ICollectionFixture<PlatformDatabaseFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "platform-database";
}
