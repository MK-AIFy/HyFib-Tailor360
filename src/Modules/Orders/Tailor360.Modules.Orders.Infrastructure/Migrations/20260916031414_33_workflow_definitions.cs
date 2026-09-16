using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _33_workflow_definitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transitions = table.Column<string>(type: "jsonb", nullable: false),
                    category_mapping = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    retired_by = table.Column<Guid>(type: "uuid", nullable: true),
                    retired_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_versions", x => x.id);
                    table.CheckConstraint("ck_workflow_versions_published_is_consistent", "(status = 'Published' OR status = 'Retired') = (published_at IS NOT NULL) AND (published_at IS NULL) = (published_reason IS NULL)");
                    table.CheckConstraint("ck_workflow_versions_retired_is_consistent", "(status = 'Retired') = (retired_at IS NOT NULL) AND (retired_at IS NULL) = (retired_reason IS NULL)");
                    table.ForeignKey(
                        name: "fk_workflow_versions_workflow_definitions_workflow_definition_",
                        column: x => x.workflow_definition_id,
                        principalSchema: "orders",
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_version_phases",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    required_role_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    requires_evidence = table.Column<bool>(type: "boolean", nullable: false),
                    expected_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    sla = table.Column<TimeSpan>(type: "interval", nullable: true),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    is_skippable = table.Column<bool>(type: "boolean", nullable: false),
                    is_terminal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_version_phases", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_version_phases_workflow_versions_workflow_version_",
                        column: x => x.workflow_version_id,
                        principalSchema: "orders",
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_version_phases_workflow_version_id",
                schema: "orders",
                table: "workflow_version_phases",
                column: "workflow_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_workflow_versions_definition_number",
                schema: "orders",
                table: "workflow_versions",
                columns: new[] { "workflow_definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workflow_versions_definition_published",
                schema: "orders",
                table: "workflow_versions",
                column: "workflow_definition_id",
                unique: true,
                filter: "status = 'Published'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_version_phases",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "workflow_versions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "orders");
        }
    }
}
