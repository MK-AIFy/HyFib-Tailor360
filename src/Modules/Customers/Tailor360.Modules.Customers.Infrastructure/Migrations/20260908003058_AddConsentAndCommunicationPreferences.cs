using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the consent register, the consent trail and communication preferences.
    /// </summary>
    /// <remarks>
    /// Three things here the model could not express, each added as explicit SQL below:
    /// the append-only trigger that makes <c>consent_records</c> unwritable-over in the database and
    /// not only in the domain type; the check constraint that keeps a quiet-hours window from being
    /// stored as half a window; and the check constraint that keeps a decision one of the three
    /// outcomes the system can read.
    /// </remarks>
    public partial class AddConsentAndCommunicationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "communication_preferences",
                schema: "customers",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_channels = table.Column<string[]>(type: "character varying(20)[]", nullable: false),
                    language = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    quiet_hours_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    quiet_hours_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communication_preferences", x => x.customer_id);
                    table.ForeignKey(
                        name: "fk_communication_preferences_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consent_purposes",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_retired = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_purposes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    wording_version = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_records_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consent_wordings",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_wordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_wordings_consent_purposes_purpose_id",
                        column: x => x.purpose_id,
                        principalSchema: "customers",
                        principalTable: "consent_purposes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_consent_purposes_organisation_key",
                schema: "customers",
                table: "consent_purposes",
                columns: new[] { "organisation_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_customer_purpose_recorded",
                schema: "customers",
                table: "consent_records",
                columns: new[] { "customer_id", "purpose_key", "recorded_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_consent_wordings_purpose_version",
                schema: "customers",
                table: "consent_wordings",
                columns: new[] { "purpose_id", "version" },
                unique: true);

            // A consent record is evidence, and evidence that can be edited is not evidence.
            // docs/nfr/data-classification.md section 5.3: nobody may edit a historical consent record
            // — a change is a new record. The domain type publishes no mutator; this is the half that
            // holds when somebody reaches the table with psql, and it follows the pattern the audit
            // trail already uses (platform.audit_events_append_only).
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.consent_records_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'customers.consent_records is append-only; % is not permitted. Withdrawing '
                        'consent inserts a Withdrawn record; it does not change the record that '
                        'granted it.', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER consent_records_no_update
                BEFORE UPDATE OR DELETE ON customers.consent_records
                FOR EACH ROW EXECUTE FUNCTION customers.consent_records_append_only();
                """);

            // The three outcomes the current-consent query can read. A fourth value would be a row
            // that proves nothing, which is worse than no row at all.
            migrationBuilder.Sql("""
                ALTER TABLE customers.consent_records
                ADD CONSTRAINT ck_consent_records_decision_is_known
                CHECK (decision IN ('Granted', 'Declined', 'Withdrawn'));
                """);

            // Half a window is not a preference anybody could act on. The domain refuses it; so does
            // the table, because the two ends are separate columns and nothing else keeps them
            // agreeing.
            migrationBuilder.Sql("""
                ALTER TABLE customers.communication_preferences
                ADD CONSTRAINT ck_communication_preferences_quiet_hours_are_whole
                CHECK ((quiet_hours_start IS NULL) = (quiet_hours_end IS NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger before the table it guards, and the function after the trigger that uses it.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS consent_records_no_update ON customers.consent_records;");

            migrationBuilder.DropTable(
                name: "communication_preferences",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "consent_records",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "consent_wordings",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "consent_purposes",
                schema: "customers");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS customers.consent_records_append_only();");
        }
    }
}
