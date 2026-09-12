using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// That <c>20260911141153_InitialOrdersSchema</c> applies, rolls back and re-applies (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the evidence the migration register asks CI for.</strong>
/// <c>docs/dev/migrations.md</c> marks the row <em>blocked on evidence</em> because the machine it was written
/// on has no PostgreSQL: both directions were generated as SQL and read through, which proves the DDL is
/// emitted and ordered and proves nothing about whether a server accepts it. The register's closing ask is
/// literal — "that <c>Down</c> empties the schema to the history table alone and the migration re-applies
/// cleanly" — and that is what this asserts.
/// </para>
/// <para>
/// <strong>It runs on a database of its own and not on the collection's.</strong> Rolling the <c>orders</c>
/// schema back would take every other Orders test's rows with it, and the hosted application refuses to serve
/// while a migration is pending — so a rollback against the shared database would fail the whole collection on
/// a health check. The pattern is <c>PlatformDatabaseFixture</c>'s: create, use, drop, namespaced per run so
/// that two runs against one cluster cannot destroy each other's scratch database.
/// </para>
/// <para>
/// <strong>The re-apply is the half that is easy to get wrong.</strong> <c>Down</c> has to drop the eight
/// trigger functions as well as the triggers: dropping a table takes its triggers and leaves its functions
/// behind, and the re-applied migration's <c>CREATE FUNCTION</c> would then fail on a name that already exists.
/// A migration that only ever ran forwards would never show it.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class OrdersMigrationTests
{
    /// <summary>The tables <c>Up</c> creates, which is every table the module owns.</summary>
    /// <remarks>
    /// Ten from <c>docs/architecture/module-ownership.md</c> section 5.5 as amended, plus the module's own
    /// <c>outbox_messages</c> and <c>inbox_messages</c> from <c>ModuleDbContext</c> — which are in this schema
    /// and not in the platform's, because which outbox a published event lands in is decided by the context it
    /// was tracked on.
    /// </remarks>
    private static readonly string[] OrdersTables =
    [
        "design_snapshots",
        "estimates",
        "garment_jobs",
        "inbox_messages",
        "job_dependencies",
        "measurement_snapshots",
        "order_draft_garment_dependencies",
        "order_draft_garments",
        "order_drafts",
        "order_revisions",
        "orders",
        "outbox_messages",
    ];

    [Fact]
    public async Task AppliesRollsBackToTheHistoryTableAloneAndReAppliesCleanly()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var database = $"{DatabaseAvailability.DatabaseNamespace}_ordersmig";

        await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await MaintenanceAsync($"CREATE DATABASE {database}");

        var connectionString = ConnectionStringFor(database);

        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: OrdersHarness.Token);
            }

            (await TablesAsync(connectionString)).ShouldBe(
                [.. OrdersTables.Append(ModuleDbContext.MigrationsHistoryTable).Order(StringComparer.Ordinal)],
                "Up creates every table the module owns, beside the history table Entity Framework keeps");

            // Eight trigger functions: the shared snapshot guard, the price guard, the two append-only guards,
            // the ready gate and one display-number guard per numbered table.
            (await FunctionCountAsync(connectionString)).ShouldBe(8);

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>()
                    .MigrateAsync(Migration.InitialDatabase, OrdersHarness.Token);
            }

            (await TablesAsync(connectionString)).ShouldBe(
                [ModuleDbContext.MigrationsHistoryTable],
                "Down empties the schema to the history table alone");

            (await FunctionCountAsync(connectionString)).ShouldBe(
                0, "a function left behind makes the re-applied CREATE FUNCTION fail on a name already taken");

            (await AppliedMigrationCountAsync(connectionString)).ShouldBe(0);

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: OrdersHarness.Token);
            }

            (await TablesAsync(connectionString)).ShouldBe(
                [.. OrdersTables.Append(ModuleDbContext.MigrationsHistoryTable).Order(StringComparer.Ordinal)],
                "the migration re-applies cleanly onto the schema its own Down emptied");

            (await FunctionCountAsync(connectionString)).ShouldBe(8);
            (await AppliedMigrationCountAsync(connectionString)).ShouldBe(1);

            // Nothing is pending afterwards, which is what the startup probe reads before it lets the host
            // serve. A re-apply that left the model and the history disagreeing would pass every check above.
            await using var probe = CreateContext(connectionString);

            (await probe.Database.GetPendingMigrationsAsync(OrdersHarness.Token)).ShouldBeEmpty();
        }
        finally
        {
            // No pool to clear: every connection this test opens says Pooling=false, so the drop below is not
            // racing a pooled connector that outlived its context. Clearing the process-wide pools here would
            // reach the hosted application the rest of this assembly is using.
            await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static OrdersDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, OrdersDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OrdersDbContext(options);
    }

    private static string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = database,

            // Unpooled throughout, so that dropping the scratch database at the end is not racing a pooled
            // connection that outlives the context — the reason PlatformDatabaseFixture migrates its template
            // the same way.
            Pooling = false,
        }.ConnectionString;

    private static async Task<IReadOnlyList<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(OrdersHarness.Token);

        await using var command = new NpgsqlCommand(
            """
            SELECT table_name
              FROM information_schema.tables
             WHERE table_schema = 'orders'
            """,
            connection);

        var tables = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(OrdersHarness.Token);

        while (await reader.ReadAsync(OrdersHarness.Token))
        {
            tables.Add(reader.GetString(0));
        }

        // Sorted here rather than by the server. A linguistic collation weighs the underscore differently from
        // an ordinal one, so `ORDER BY table_name` would file `__ef_migrations_history` in the middle of the
        // list on one cluster and at the front on another, and the expectation would be right on one of them.
        tables.Sort(StringComparer.Ordinal);

        return tables;
    }

    private static async Task<int> FunctionCountAsync(string connectionString)
        => await ScalarAsync(
            connectionString,
            """
            SELECT count(*)
              FROM pg_proc AS routine
              JOIN pg_namespace AS space ON space.oid = routine.pronamespace
             WHERE space.nspname = 'orders'
            """);

    private static async Task<int> AppliedMigrationCountAsync(string connectionString)
        => await ScalarAsync(
            connectionString,
            $"SELECT count(*) FROM orders.{ModuleDbContext.MigrationsHistoryTable}");

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(OrdersHarness.Token);

        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(OrdersHarness.Token), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// How long a maintenance statement — creating or dropping the scratch database — is given.
    /// </summary>
    /// <remarks>
    /// Two minutes rather than Npgsql's generic thirty seconds, for the reason
    /// <c>PlatformDatabaseFixture</c> records: <c>DROP DATABASE … WITH (FORCE)</c> terminates every backend
    /// still attached and waits for them to go, and on a loaded runner thirty seconds is reachable.
    /// </remarks>
    private const int MaintenanceCommandTimeoutSeconds = 120;

    private static async Task MaintenanceAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
            CommandTimeout = MaintenanceCommandTimeoutSeconds,
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(OrdersHarness.Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(OrdersHarness.Token);
    }
}
