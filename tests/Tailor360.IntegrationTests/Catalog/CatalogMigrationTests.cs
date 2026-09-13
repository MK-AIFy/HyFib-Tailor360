using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// The design catalogue migrations (#137, the composite option key that followed it, and the selection
/// drafts of #140) applied, rolled back and re-applied against real PostgreSQL, in a scratch database of
/// its own. <c>src/Modules/CLAUDE.md</c> section 6 asks that every <c>Down</c> has been executed at least
/// once; this is where it is.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CatalogMigrationTests
{
    private const string BeforeDesign = "20260910215657_CatalogReferenceBreaches";

    /// <summary>How many migrations sit between <see cref="BeforeDesign"/> and the current model.</summary>
    private const int DesignMigrationCount = 3;

    private static readonly string[] DesignTables =
    [
        "design_option_group_branches",
        "design_option_groups",
        "design_options",
        "design_rules",
        "design_selection_draft_selections",
        "design_selection_drafts",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AppliesRollsBackTheDesignTablesAndReAppliesCleanly()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var database = $"{DatabaseAvailability.DatabaseNamespace}_catalogmig";
        await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await MaintenanceAsync($"CREATE DATABASE {database}");
        var connectionString = ConnectionStringFor(database);

        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            var tables = await TablesAsync(connectionString);
            DesignTables.ShouldAllBe(table => tables.Contains(table), "Up creates every design table");
            var functions = await FunctionCountAsync(connectionString);
            var applied = await AppliedMigrationCountAsync(connectionString);

            // Down three steps: the selection drafts of #140, the composite option key, then the design
            // tables and their two trigger functions go; the rest of the schema stays.
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(BeforeDesign, Token);
            }

            var rolledBack = await TablesAsync(connectionString);
            DesignTables.ShouldAllBe(table => !rolledBack.Contains(table), "Down drops every design table");
            rolledBack.ShouldContain("categories", "Down touches only what this migration created");
            (await FunctionCountAsync(connectionString)).ShouldBe(
                functions - 2,
                "the two functions the design-catalogue migration created are gone; a function left behind "
                + "makes the re-applied CREATE FUNCTION fail on a name already taken. The selection-drafts "
                + "migration of #140 creates none of its own");
            (await AppliedMigrationCountAsync(connectionString)).ShouldBe(applied - DesignMigrationCount);

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            var reapplied = await TablesAsync(connectionString);
            reapplied.ShouldBe(tables, "the migration re-applies cleanly onto the schema its own Down emptied");
            (await FunctionCountAsync(connectionString)).ShouldBe(functions);
            (await AppliedMigrationCountAsync(connectionString)).ShouldBe(applied);

            await using var probe = CreateContext(connectionString);
            (await probe.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static CatalogDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, CatalogDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogDbContext(options);
    }

    private static string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = database,
            // Unpooled, so that dropping the scratch database is not racing a pooled connection that
            // outlives its context.
            Pooling = false,
        }.ConnectionString;

    private static async Task<IReadOnlyList<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name
              FROM information_schema.tables
             WHERE table_schema = 'catalog'
            """,
            connection);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            tables.Add(reader.GetString(0));
        }

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
             WHERE space.nspname = 'catalog'
            """);

    private static async Task<int> AppliedMigrationCountAsync(string connectionString)
        => await ScalarAsync(
            connectionString,
            $"SELECT count(*) FROM catalog.{ModuleDbContext.MigrationsHistoryTable}");

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
    }

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
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }
}
