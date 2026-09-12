using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Billing.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.IntegrationTests.Billing;

/// <summary>
/// The Billing migrations (#145's initial schema and #146's price lists) applied, rolled back to nothing
/// and re-applied against real PostgreSQL, in a scratch database of its own. <c>src/Modules/CLAUDE.md</c> section 6 asks that every
/// <c>Down</c> has been executed at least once; this is where it is.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BillingMigrationTests
{
    private static readonly string[] Tables =
    [
        "gst_registrations",
        "tax_configuration_versions",
        "tax_codes",
        "tax_components",
        "price_lists",
        "price_list_versions",
        "price_list_version_branches",
        "price_list_items",
        "discount_rules",
        "calculation_snapshots",
        "order_facts",
        "order_fact_jobs",
        "invoices",
        "invoice_lines",
        "invoice_line_surcharges",
        "invoice_tax_components",
        "invoice_cancellations",
        "adjustment_notes",
        "adjustment_note_lines",
        "adjustment_note_taxes",
        "outbox_messages",
        "inbox_messages",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AppliesRollsBackToTheHistoryTableAloneAndReAppliesCleanly()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var database = $"{DatabaseAvailability.DatabaseNamespace}_billingmig";
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
            Tables.ShouldAllBe(table => tables.Contains(table), "Up creates the tables");
            var functions = await FunctionCountAsync(connectionString);
            functions.ShouldBeGreaterThanOrEqualTo(2, "the immutability trigger functions exist");

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync("0", Token);
            }

            var rolledBack = await TablesAsync(connectionString);
            rolledBack.ShouldBe(["__ef_migrations_history"], "Down empties the schema to the history table");
            (await FunctionCountAsync(connectionString)).ShouldBe(0, "Down drops the trigger functions");

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            (await TablesAsync(connectionString)).ShouldBe(tables, "the migration re-applies cleanly");
            (await FunctionCountAsync(connectionString)).ShouldBe(functions);

            await using var probe = CreateContext(connectionString);
            (await probe.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static BillingDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, BillingDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new BillingDbContext(options);
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
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'billing' ORDER BY table_name",
            connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<int> FunctionCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'billing'",
            connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
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
