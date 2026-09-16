using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The <c>PrintJobs</c> migration (#251) applied, rolled back to the migration before it and re-applied
/// against real PostgreSQL, in a scratch database of its own. <c>src/Modules/CLAUDE.md</c> section 6 asks
/// that every <c>Down</c> has been executed at least once; this is where it is.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PrintJobsMigrationTests
{
    /// <summary>The platform migration immediately before <c>PrintJobs</c>.</summary>
    private const string BeforePrintJobs = "20260906170048_IdempotencyInFlightLease";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AppliesRollsBackToTheMigrationBeforeItAndReAppliesCleanly()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var database = $"{DatabaseAvailability.DatabaseNamespace}_printjobsmig";
        await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await MaintenanceAsync($"CREATE DATABASE {database}");
        var connectionString = ConnectionStringFor(database);

        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(BeforePrintJobs, Token);
            }

            (await TablesAsync(connectionString)).ShouldNotContain(
                "print_jobs", "the table does not exist before this migration");

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            var tables = await TablesAsync(connectionString);
            tables.ShouldContain("print_jobs", "Up creates the table");

            // xmin is a PostgreSQL system column and is not listed by information_schema.columns, even
            // though PlatformDbContext maps it as the row's concurrency token.
            var columns = await ColumnsAsync(connectionString, "print_jobs");
            columns.ShouldBe(
                [
                    "id", "branch_id", "kind", "format", "payload_reference", "copies", "printer_hint",
                    "status", "requested_by", "requested_at", "resolved_at", "resolved_by",
                    "resolved_station", "failure_reason",
                ],
                ignoreOrder: true);

            var indexes = await IndexNamesAsync(connectionString, "print_jobs");
            indexes.ShouldContain("ix_print_jobs_branch_status_requested_at");
            indexes.ShouldContain("pk_print_jobs");

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(BeforePrintJobs, Token);
            }

            (await TablesAsync(connectionString)).ShouldNotContain(
                "print_jobs", "Down drops the table");

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            (await TablesAsync(connectionString)).ShouldContain("print_jobs", "the migration re-applies cleanly");
            (await IndexNamesAsync(connectionString, "print_jobs")).ShouldContain(
                "ix_print_jobs_branch_status_requested_at", "the partial index came back too, not only the table");

            await using var probe = CreateContext(connectionString);
            (await probe.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
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

    private static string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = database,
            Pooling = false,
        }.ConnectionString;

    private static async Task<IReadOnlyList<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform' ORDER BY table_name",
            connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<IReadOnlyList<string>> ColumnsAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = @table",
            connection);
        command.Parameters.AddWithValue("table", table);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<string>> IndexNamesAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'platform' AND tablename = @table",
            connection);
        command.Parameters.AddWithValue("table", table);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task MaintenanceAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }
}
