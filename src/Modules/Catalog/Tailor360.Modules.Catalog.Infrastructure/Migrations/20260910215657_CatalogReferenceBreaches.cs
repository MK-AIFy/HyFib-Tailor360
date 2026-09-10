using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogReferenceBreaches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_breaches",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    validator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detected_because_of = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    resolved_because_of = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_breaches", x => x.id);
                    table.CheckConstraint("ck_reference_breaches_resolution_is_ordered", "resolved_at IS NULL OR resolved_at >= detected_at");
                });

            migrationBuilder.CreateIndex(
                name: "ix_reference_breaches_organisation_resolved",
                schema: "catalog",
                table: "reference_breaches",
                columns: new[] { "organisation_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_reference_breaches_one_open",
                schema: "catalog",
                table: "reference_breaches",
                columns: new[] { "catalog_version_id", "code", "target" },
                unique: true,
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_breaches",
                schema: "catalog");
        }
    }
}
