using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. The supplier's names as issued, frozen on the invoice (Codex review on #160) ──────────────
            // The document is rendered after posting, and a registration amended in between must not change
            // what a statutory record says was issued; the GSTIN and the state code were already copied at
            // drafting, the names now travel with them. Existing rows are backfilled from the registration
            // they name — the best evidence that exists for them — under the immutability trigger disabled
            // for this one statement: the row's business content does not move, only a column that did not
            // exist when it was posted is filled in. Added nullable, filled, then made required.
            migrationBuilder.AddColumn<string>(
                name: "supplier_legal_name",
                schema: "billing",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_trade_name",
                schema: "billing",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql("ALTER TABLE billing.invoices DISABLE TRIGGER posted_invoices_are_immutable;");
            migrationBuilder.Sql(
                """
                UPDATE billing.invoices AS invoices
                   SET supplier_legal_name = registrations.legal_name,
                       supplier_trade_name = registrations.trade_name
                  FROM billing.gst_registrations AS registrations
                 WHERE registrations.id = invoices.gst_registration_id
                   AND invoices.supplier_legal_name IS NULL;
                """);
            migrationBuilder.Sql("UPDATE billing.invoices SET supplier_legal_name = '' WHERE supplier_legal_name IS NULL;");
            migrationBuilder.Sql("ALTER TABLE billing.invoices ENABLE TRIGGER posted_invoices_are_immutable;");
            migrationBuilder.Sql("ALTER TABLE billing.invoices ALTER COLUMN supplier_legal_name SET NOT NULL;");

            // ── 2. The note's business date, in the branch calendar (src/Modules/CLAUDE.md section 7) ────────
            // Added nullable, backfilled from the posting instant, then made required: a default of
            // 0001-01-01 on the rows that exist would be a lie on a statutory record. The backfill uses the
            // one timezone OD-06 has recorded for the launch branches; a branch in another zone gains its
            // notes after this migration and is never backfilled. Notes are append-only by trigger, and the
            // trigger is disabled for this one statement for the reason given above.
            migrationBuilder.AddColumn<DateOnly>(
                name: "posted_on",
                schema: "billing",
                table: "adjustment_notes",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("ALTER TABLE billing.adjustment_notes DISABLE TRIGGER adjustment_notes_are_append_only;");
            migrationBuilder.Sql(
                """
                UPDATE billing.adjustment_notes
                   SET posted_on = (posted_at AT TIME ZONE 'Asia/Kolkata')::date
                 WHERE posted_on IS NULL;
                """);
            migrationBuilder.Sql("ALTER TABLE billing.adjustment_notes ENABLE TRIGGER adjustment_notes_are_append_only;");
            migrationBuilder.Sql("ALTER TABLE billing.adjustment_notes ALTER COLUMN posted_on SET NOT NULL;");

            // ── 3. A draft's order revision is repaired where the InvoicePosting backfill stamped it blindly ──
            // That migration stamped every draft with its order's current revision, which lets a draft priced
            // against revision 1 post after the order moved to 2 — the case the revision check exists to
            // refuse. Here, and on an upgrade where that migration has already run: a draft keeps its
            // order's current revision only where the order fact has not moved since the draft was last
            // priced (a revision, an order cancellation and a garment cancellation each touch the fact's
            // updated_at; a re-pricing touches the draft's); every other draft is set to zero, is refused at
            // posting as revised, and is re-priced first. A draft whose order has no fact keeps zero.
            migrationBuilder.Sql(
                """
                UPDATE billing.invoices AS invoices
                   SET order_revision_number = CASE
                                                   WHEN facts.updated_at <= invoices.updated_at THEN facts.revision_number
                                                   ELSE 0
                                               END
                  FROM billing.order_facts AS facts
                 WHERE facts.order_id = invoices.order_id
                   AND invoices.status = 0;
                """);

            migrationBuilder.CreateTable(
                name: "document_artifacts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_artifacts", x => x.id);
                    table.CheckConstraint("ck_document_artifacts_completed_is_whole", "(status = 1 AND object_key IS NOT NULL AND sha256 IS NOT NULL AND size_bytes IS NOT NULL AND completed_at IS NOT NULL)\nOR (status <> 1 AND object_key IS NULL AND sha256 IS NULL AND size_bytes IS NULL AND completed_at IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_artifacts_status_requested_at",
                schema: "billing",
                table: "document_artifacts",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_document_artifacts_document_version",
                schema: "billing",
                table: "document_artifacts",
                columns: new[] { "kind", "document_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_document_artifacts_object_key",
                schema: "billing",
                table: "document_artifacts",
                column: "object_key",
                unique: true,
                filter: "object_key IS NOT NULL");

            // ── 4. A pending artefact for every document posted before the table existed ──────────────────────
            // Their posting events were consumed before the artefact handlers existed, so nothing would ever
            // ask for them; without this every download of an existing financial record would answer "not
            // available" for good. One pending row per posted invoice and per note, version 1, exactly as the
            // handler requests one. The identifier is a UUIDv7 composed in SQL — the posting instant in the
            // first 48 bits, the version nibble set over a random v4 — because the generator the application
            // uses (IIdGenerator, ARCH-015) is not reachable from a migration and a v4 here would break the
            // time ordering every other identifier in the schema has.
            migrationBuilder.Sql(
                """
                INSERT INTO billing.document_artifacts
                       (id, organisation_id, branch_id, kind, document_id, document_number, version, status,
                        attempts, requested_at, updated_at)
                SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid())
                                                      placing substring(int8send((extract(epoch FROM posted_at) * 1000)::bigint) FROM 3)
                                                      FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid,
                       organisation_id, branch_id, 0, id, invoice_number, 1, 0, 0, posted_at, posted_at
                  FROM billing.invoices AS invoices
                 WHERE invoices.status = 1
                   AND NOT EXISTS (SELECT 1 FROM billing.document_artifacts AS existing
                                    WHERE existing.kind = 0 AND existing.document_id = invoices.id);
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO billing.document_artifacts
                       (id, organisation_id, branch_id, kind, document_id, document_number, version, status,
                        attempts, requested_at, updated_at)
                SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid())
                                                      placing substring(int8send((extract(epoch FROM posted_at) * 1000)::bigint) FROM 3)
                                                      FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid,
                       organisation_id, branch_id, kind + 1, id, number, 1, 0, 0, posted_at, posted_at
                  FROM billing.adjustment_notes AS notes
                 WHERE NOT EXISTS (SELECT 1 FROM billing.document_artifacts AS existing
                                    WHERE existing.kind = notes.kind + 1 AND existing.document_id = notes.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_artifacts",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "supplier_legal_name",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supplier_trade_name",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "posted_on",
                schema: "billing",
                table: "adjustment_notes");
        }
    }
}
