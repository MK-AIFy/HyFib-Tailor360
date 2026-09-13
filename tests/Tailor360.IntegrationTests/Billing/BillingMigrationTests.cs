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
        "document_artifacts",
        "payment_modes",
        "payment_mode_branches",
        "cashier_sessions",
        "cashier_session_counts",
        "cashier_session_mode_totals",
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

    /// <summary>
    /// The upgrade path <c>DocumentArtifacts</c> has to survive: a database that already holds a posted invoice,
    /// a credit note and two drafts when the migration arrives. The append-only and immutability triggers are
    /// in force on those rows, the note gains its business date, the invoice its supplier's names, both posted
    /// documents a pending artefact to render, and the drafts' order revisions are repaired — kept only where
    /// the order has not moved since the draft was priced.
    /// </summary>
    [Fact]
    public async Task UpgradesADatabaseThatAlreadyHoldsPostedRecords()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var database = $"{DatabaseAvailability.DatabaseNamespace}_billingupg";
        await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await MaintenanceAsync($"CREATE DATABASE {database}");
        var connectionString = ConnectionStringFor(database);

        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(BeforeDocumentArtifacts, Token);
            }

            await ExecuteAsync(connectionString, PostedRecordsBeforeDocumentArtifacts);

            await using (var context = CreateContext(connectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
            }

            (await ScalarAsync<DateTime>(connectionString, "SELECT posted_on::timestamp FROM billing.adjustment_notes WHERE id = '00000000-0000-0000-0000-0000000000c1'"))
                .ShouldBe(new DateTime(2026, 9, 12), "20:30 UTC on the 11th is 02:00 on the 12th in the branch calendar");
            (await ScalarAsync<string>(connectionString, "SELECT supplier_legal_name FROM billing.invoices WHERE id = '00000000-0000-0000-0000-0000000000a1'"))
                .ShouldBe("Example Tailors Private Limited", "the name is copied from the registration the invoice names");
            (await ScalarAsync<string>(connectionString, "SELECT supplier_trade_name FROM billing.invoices WHERE id = '00000000-0000-0000-0000-0000000000a1'"))
                .ShouldBe("Example Tailors");

            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM billing.document_artifacts")).ShouldBe(2L, "one pending artefact per posted document, none for a draft");
            (await ScalarAsync<int>(connectionString, "SELECT kind FROM billing.document_artifacts WHERE document_id = '00000000-0000-0000-0000-0000000000a1'")).ShouldBe(0);
            (await ScalarAsync<int>(connectionString, "SELECT kind FROM billing.document_artifacts WHERE document_id = '00000000-0000-0000-0000-0000000000c1'")).ShouldBe(1, "a credit note's kind");
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM billing.document_artifacts WHERE status = 0 AND version = 1 AND attempts = 0 AND object_key IS NULL")).ShouldBe(2L);
            (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM billing.document_artifacts WHERE substring(id::text FROM 15 FOR 1) = '7'")).ShouldBe(2L, "the seeded identifiers are UUIDv7");
            (await ScalarAsync<string>(connectionString, "SELECT document_number FROM billing.document_artifacts WHERE kind = 1")).ShouldBe("CN-MAIN-2627-000001");

            (await ScalarAsync<int>(connectionString, "SELECT order_revision_number FROM billing.invoices WHERE id = '00000000-0000-0000-0000-0000000000a2'"))
                .ShouldBe(3, "the order has not moved since the draft was priced, so the draft keeps its revision");
            (await ScalarAsync<int>(connectionString, "SELECT order_revision_number FROM billing.invoices WHERE id = '00000000-0000-0000-0000-0000000000a3'"))
                .ShouldBe(0, "the order moved after the draft was priced, so the draft must be re-priced before it posts");

            await using var probe = CreateContext(connectionString);
            (await probe.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }
        finally
        {
            await MaintenanceAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private const string BeforeDocumentArtifacts = "20260912103135_InvoicePosting";

    /// <summary>
    /// A registration, one posted invoice (inserted as the draft the trigger permits, then posted), one credit
    /// note against it at 20:30 UTC, and two drafts blindly stamped with revision 3 — one whose order fact
    /// last moved before the draft was priced, one whose order moved after. Synthetic throughout.
    /// </summary>
    private const string PostedRecordsBeforeDocumentArtifacts = """
        INSERT INTO billing.gst_registrations
               (id, organisation_id, branch_id, gstin, state_code, legal_name, trade_name, effective_from, created_at, updated_at)
        VALUES ('00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002',
                '33AAAAA0000A1Z5', '33', 'Example Tailors Private Limited', 'Example Tailors', DATE '2026-04-01',
                TIMESTAMPTZ '2026-09-01T00:00:00Z', TIMESTAMPTZ '2026-09-01T00:00:00Z');

        INSERT INTO billing.order_facts
               (order_id, organisation_id, branch_id, customer_id, order_number, revision_number, status, first_seen_at, updated_at)
        VALUES ('00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002',
                '00000000-0000-0000-0000-000000000003', 'ORD-MAIN-2627-000001', 1, 1, TIMESTAMPTZ '2026-09-10T00:00:00Z', TIMESTAMPTZ '2026-09-10T00:00:00Z'),
               ('00000000-0000-0000-0000-0000000000b2', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002',
                '00000000-0000-0000-0000-000000000003', 'ORD-MAIN-2627-000002', 3, 1, TIMESTAMPTZ '2026-09-10T00:00:00Z', TIMESTAMPTZ '2026-09-10T00:00:00Z'),
               ('00000000-0000-0000-0000-0000000000b3', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002',
                '00000000-0000-0000-0000-000000000003', 'ORD-MAIN-2627-000003', 3, 1, TIMESTAMPTZ '2026-09-10T00:00:00Z', TIMESTAMPTZ '2026-09-11T12:00:00Z');

        INSERT INTO billing.invoices
               (id, organisation_id, branch_id, customer_id, order_id, order_number, status, revision, created_at, updated_at,
                gst_registration_id, gstin, place_of_supply_state_code, price_list_version_id, calculation_reference, scheme,
                supplier_state_code, tax_configuration_version_id, tax_inclusive, customer_number, customer_display_name,
                central_tax_amount, central_tax_currency, cess_amount, cess_currency, discount_total_amount, discount_total_currency,
                grand_total_amount, grand_total_currency, integrated_tax_amount, integrated_tax_currency, round_off_amount, round_off_currency,
                state_tax_amount, state_tax_currency, subtotal_amount, subtotal_currency, taxable_value_amount, taxable_value_currency,
                order_revision_number)
        SELECT id, '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003',
               order_id, order_number, 0, 1, TIMESTAMPTZ '2026-09-11T06:00:00Z', TIMESTAMPTZ '2026-09-11T06:00:00Z',
               '00000000-0000-0000-0000-0000000000e1', '33AAAAA0000A1Z5', '33', '00000000-0000-0000-0000-0000000000f1', reference, 'IntraState',
               '33', '00000000-0000-0000-0000-0000000000f2', false, 'C-000001', 'Synthetic Customer',
               0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR',
               stamped
          FROM (VALUES
                ('00000000-0000-0000-0000-0000000000a1'::uuid, '00000000-0000-0000-0000-0000000000b1'::uuid, 'ORD-MAIN-2627-000001', 'order:b1:1', 1),
                ('00000000-0000-0000-0000-0000000000a2'::uuid, '00000000-0000-0000-0000-0000000000b2'::uuid, 'ORD-MAIN-2627-000002', 'order:b2:3', 3),
                ('00000000-0000-0000-0000-0000000000a3'::uuid, '00000000-0000-0000-0000-0000000000b3'::uuid, 'ORD-MAIN-2627-000003', 'order:b3:3', 3))
               AS seed (id, order_id, order_number, reference, stamped);

        UPDATE billing.invoices
           SET status = 1, invoice_number = 'INV-MAIN-2627-000001', barcode_payload = 'I-7K3M9QW2XZ4B', financial_year = '2627',
               posted_at = TIMESTAMPTZ '2026-09-11T06:30:00Z', posted_on = DATE '2026-09-11'
         WHERE id = '00000000-0000-0000-0000-0000000000a1';

        INSERT INTO billing.adjustment_notes
               (id, invoice_id, organisation_id, branch_id, kind, number, reason, posted_at,
                central_tax_amount, central_tax_currency, cess_amount, cess_currency, discount_total_amount, discount_total_currency,
                grand_total_amount, grand_total_currency, integrated_tax_amount, integrated_tax_currency, round_off_amount, round_off_currency,
                state_tax_amount, state_tax_currency, subtotal_amount, subtotal_currency, taxable_value_amount, taxable_value_currency)
        VALUES ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000a1', '00000000-0000-0000-0000-000000000001',
                '00000000-0000-0000-0000-000000000002', 0, 'CN-MAIN-2627-000001', 'Lining charged twice.', TIMESTAMPTZ '2026-09-11T20:30:00Z',
                0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR', 0, 'INR');
        """;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Token))!;
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
