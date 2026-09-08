using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateCandidatesAndCustomerMerges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "merged_at",
                schema: "customers",
                table: "customers",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_customer_id",
                schema: "customers",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_merges",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survivor_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_customer_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number_alias_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aliases_recorded = table.Column<int>(type: "integer", nullable: false),
                    visibility_branches_added = table.Column<int>(type: "integer", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    merged_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_merges", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_merges_customers_merged_customer_id",
                        column: x => x.merged_customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_merges_customers_survivor_customer_id",
                        column: x => x.survivor_customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_candidates",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reasons = table.Column<string[]>(type: "character varying(40)[]", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    merge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_duplicate_candidates_customer_merges_merge_id",
                        column: x => x.merge_id,
                        principalSchema: "customers",
                        principalTable: "customer_merges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duplicate_candidates_customers_candidate_customer_id",
                        column: x => x.candidate_customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_duplicate_candidates_customers_subject_customer_id",
                        column: x => x.subject_customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_merged_into",
                schema: "customers",
                table: "customers",
                column: "merged_into_customer_id",
                filter: "merged_into_customer_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_merged_into_is_consistent",
                schema: "customers",
                table: "customers",
                sql: "(merged_into_customer_id IS NULL) = (merged_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_merged_into_is_deactivated",
                schema: "customers",
                table: "customers",
                sql: "merged_into_customer_id IS NULL OR status = 'Deactivated'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_merged_into_is_not_self",
                schema: "customers",
                table: "customers",
                sql: "merged_into_customer_id IS NULL OR merged_into_customer_id <> id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_merges_survivor_merged_at",
                schema: "customers",
                table: "customer_merges",
                columns: new[] { "survivor_customer_id", "merged_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_customer_merges_merged_customer",
                schema: "customers",
                table: "customer_merges",
                column: "merged_customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_candidate_customer_id",
                schema: "customers",
                table: "duplicate_candidates",
                column: "candidate_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_merge_id",
                schema: "customers",
                table: "duplicate_candidates",
                column: "merge_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_subject_decided_at",
                schema: "customers",
                table: "duplicate_candidates",
                columns: new[] { "subject_customer_id", "decided_at" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "fk_customers_customers_merged_into_customer_id",
                schema: "customers",
                table: "customers",
                column: "merged_into_customer_id",
                principalSchema: "customers",
                principalTable: "customers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // docs/prd/exceptions.md EX-01: a merge is irreversible. The domain publishes no way to
            // rewrite one; this is the half that holds when somebody reaches the table with psql, and
            // it follows the pattern customers.consent_records_append_only already set.
            //
            // The single permitted change is the erasure workflow (#57) redacting the free text a
            // member of staff typed about a person, leaving the evidence that the merge happened. The
            // column list is written out rather than compared with ROW(NEW.*) so that a column added
            // later has to be considered here rather than silently becoming editable.
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.customer_merges_no_rewrite()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'UPDATE'
                       AND NEW.reason IS NULL
                       AND OLD.reason IS NOT NULL
                       AND ROW(NEW.id, NEW.organisation_id, NEW.survivor_customer_id,
                               NEW.merged_customer_id, NEW.merged_customer_number, NEW.branch_id,
                               NEW.number_alias_id, NEW.aliases_recorded,
                               NEW.visibility_branches_added, NEW.event_id, NEW.merged_at,
                               NEW.merged_by)
                           IS NOT DISTINCT FROM
                           ROW(OLD.id, OLD.organisation_id, OLD.survivor_customer_id,
                               OLD.merged_customer_id, OLD.merged_customer_number, OLD.branch_id,
                               OLD.number_alias_id, OLD.aliases_recorded,
                               OLD.visibility_branches_added, OLD.event_id, OLD.merged_at,
                               OLD.merged_by)
                    THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION
                        'customers.customer_merges records an irreversible decision; % is not '
                        'permitted. The only change this table accepts is clearing the reason, '
                        'which is how personal data is redacted without destroying the evidence '
                        'that the merge happened.', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER customer_merges_no_rewrite
                BEFORE UPDATE OR DELETE ON customers.customer_merges
                FOR EACH ROW EXECUTE FUNCTION customers.customer_merges_no_rewrite();
                """);

            // The other half of the same rule, on the record itself: once a customer has been folded
            // into another, the pointer is never cleared and the record never returns to ordinary use.
            //
            // A value-to-value change is permitted deliberately. It is how a chain is flattened when
            // the record this one was merged into is itself merged later, which keeps every pointer
            // one hop from a record that stands.
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.customers_merge_is_one_way()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD.merged_into_customer_id IS NULL THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.merged_into_customer_id IS NULL THEN
                        RAISE EXCEPTION
                            'customer % was merged into %; the pointer cannot be cleared, because '
                            'there is no un-merge.', OLD.id, OLD.merged_into_customer_id
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status <> 'Deactivated' THEN
                        RAISE EXCEPTION
                            'customer % was merged into %; it cannot be returned to ordinary use, '
                            'because that would put two records for one person back in search.',
                            OLD.id, OLD.merged_into_customer_id
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER customers_merge_is_one_way
                BEFORE UPDATE ON customers.customers
                FOR EACH ROW EXECUTE FUNCTION customers.customers_merge_is_one_way();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers before the tables they guard, and each function after the trigger using it.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS customers_merge_is_one_way ON customers.customers;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS customers.customers_merge_is_one_way();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS customer_merges_no_rewrite ON customers.customer_merges;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS customers.customer_merges_no_rewrite();");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_customers_merged_into_customer_id",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropTable(
                name: "duplicate_candidates",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customer_merges",
                schema: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_merged_into",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_merged_into_is_consistent",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_merged_into_is_deactivated",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_merged_into_is_not_self",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "merged_at",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "merged_into_customer_id",
                schema: "customers",
                table: "customers");
        }
    }
}
