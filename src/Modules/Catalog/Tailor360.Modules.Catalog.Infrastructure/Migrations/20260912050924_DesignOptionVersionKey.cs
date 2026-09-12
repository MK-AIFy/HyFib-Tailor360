using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DesignOptionVersionKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_design_options_catalog_versions_catalog_version_id",
                schema: "catalog",
                table: "design_options");

            migrationBuilder.DropForeignKey(
                name: "fk_design_options_design_groups_design_option_group_id",
                schema: "catalog",
                table: "design_options");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_design_option_groups_id_catalog_version_id",
                schema: "catalog",
                table: "design_option_groups",
                columns: new[] { "id", "catalog_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_design_options_design_option_group_id_catalog_version_id",
                schema: "catalog",
                table: "design_options",
                columns: new[] { "design_option_group_id", "catalog_version_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_design_options_design_option_groups_version",
                schema: "catalog",
                table: "design_options",
                columns: new[] { "design_option_group_id", "catalog_version_id" },
                principalSchema: "catalog",
                principalTable: "design_option_groups",
                principalColumns: new[] { "id", "catalog_version_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_design_options_design_option_groups_version",
                schema: "catalog",
                table: "design_options");

            migrationBuilder.DropIndex(
                name: "ix_design_options_design_option_group_id_catalog_version_id",
                schema: "catalog",
                table: "design_options");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_design_option_groups_id_catalog_version_id",
                schema: "catalog",
                table: "design_option_groups");

            migrationBuilder.AddForeignKey(
                name: "fk_design_options_catalog_versions_catalog_version_id",
                schema: "catalog",
                table: "design_options",
                column: "catalog_version_id",
                principalSchema: "catalog",
                principalTable: "catalog_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_design_options_design_groups_design_option_group_id",
                schema: "catalog",
                table: "design_options",
                column: "design_option_group_id",
                principalSchema: "catalog",
                principalTable: "design_option_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
