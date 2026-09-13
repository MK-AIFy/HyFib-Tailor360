using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tailor360.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DesignSelectionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "design_selection_drafts",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_selection_drafts", x => x.id);
                    table.CheckConstraint("ck_design_selection_drafts_expires_after_it_started", "expires_at > started_at");
                });

            migrationBuilder.CreateTable(
                name: "design_selection_draft_selections",
                schema: "catalog",
                columns: table => new
                {
                    design_selection_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    group_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    option_codes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_selection_draft_selections", x => new { x.design_selection_draft_id, x.id });
                    table.ForeignKey(
                        name: "fk_design_selection_draft_selections_design_selection_drafts_d",
                        column: x => x.design_selection_draft_id,
                        principalSchema: "catalog",
                        principalTable: "design_selection_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_design_selection_drafts_organisation_expiry",
                schema: "catalog",
                table: "design_selection_drafts",
                columns: new[] { "organisation_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "design_selection_draft_selections",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "design_selection_drafts",
                schema: "catalog");
        }
    }
}
