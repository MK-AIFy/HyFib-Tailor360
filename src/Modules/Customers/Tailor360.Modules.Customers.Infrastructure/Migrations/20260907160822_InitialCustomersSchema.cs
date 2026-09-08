using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCustomersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owning_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    normalised_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    native_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    phone_e164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    phone_last_six = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    alternate_phone_e164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    alternate_phone_last_six = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    postcode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    language = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.CheckConstraint("ck_customers_alternate_phone_is_e164", "alternate_phone_e164 IS NULL OR alternate_phone_e164 ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("ck_customers_deactivated_at_matches_status", "(status = 'Deactivated') = (deactivated_at IS NOT NULL)");
                    table.CheckConstraint("ck_customers_phone_is_e164", "phone_e164 ~ '^\\+[1-9][0-9]{7,14}$'");
                });

            migrationBuilder.CreateTable(
                name: "customer_aliases",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalised_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_aliases_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_branch_visibility",
                schema: "customers",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_branch_visibility", x => new { x.customer_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_customer_branch_visibility_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_aliases_customer_id",
                schema: "customers",
                table: "customer_aliases",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_aliases_normalised_value",
                schema: "customers",
                table: "customer_aliases",
                column: "normalised_value");

            migrationBuilder.CreateIndex(
                name: "ix_customer_branch_visibility_branch",
                schema: "customers",
                table: "customer_branch_visibility",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_alternate_phone_last_six",
                schema: "customers",
                table: "customers",
                columns: new[] { "organisation_id", "alternate_phone_last_six" },
                filter: "alternate_phone_last_six IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_native_name",
                schema: "customers",
                table: "customers",
                columns: new[] { "organisation_id", "native_name" },
                filter: "native_name IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_normalised_name",
                schema: "customers",
                table: "customers",
                columns: new[] { "organisation_id", "normalised_name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone_last_six",
                schema: "customers",
                table: "customers",
                columns: new[] { "organisation_id", "phone_last_six" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_organisation_customer_number",
                schema: "customers",
                table: "customers",
                columns: new[] { "organisation_id", "customer_number" },
                unique: true);

            // ---------------------------------------------------------------------------------
            // Fuzzy name search.
            //
            // The counter types part of a name and expects the record back. A btree index cannot
            // serve `ILIKE '%kavita%'` — the leading wildcard defeats it — so the search would fall
            // to a sequential scan of every customer in the organisation on every keystroke. A GIN
            // trigram index does serve it, which is why the model above declares the ordinary btree
            // index for exact lookups and this SQL adds the trigram one for contains.
            //
            // The extension is database-wide and the indexes are in this module's schema. Creating an
            // extension needs a role that may (`docs/platform/database.md`): the development and
            // continuous-integration databases run as the bootstrap superuser and this succeeds, and
            // a production migrator role that may not will fail HERE, loudly, with PostgreSQL's own
            // message — which is the right outcome. A migration that quietly skipped the index would
            // ship a search that works in every test and crawls in the shop.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_customers_normalised_name_trgm
                    ON customers.customers USING gin (normalised_name gin_trgm_ops);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_customers_native_name_trgm
                    ON customers.customers USING gin (native_name gin_trgm_ops);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_customer_aliases_normalised_value_trgm
                    ON customers.customer_aliases USING gin (normalised_value gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The indexes go with their tables, but they are dropped explicitly first so that Down
            // reverses Up statement for statement and can be read against it.
            //
            // The extension is deliberately NOT dropped. It is database-wide, another module may have
            // come to depend on it by the time this is reverted, and dropping it would cascade away
            // their indexes too. Removing it is a database administrator's decision, not a module's.
            migrationBuilder.Sql("DROP INDEX IF EXISTS customers.ix_customer_aliases_normalised_value_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS customers.ix_customers_native_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS customers.ix_customers_normalised_name_trgm;");

            migrationBuilder.DropTable(
                name: "customer_aliases",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customer_branch_visibility",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customers");
        }
    }
}
