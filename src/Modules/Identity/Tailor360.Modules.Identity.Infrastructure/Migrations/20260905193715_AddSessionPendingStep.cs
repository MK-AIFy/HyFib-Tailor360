using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// Records on each session what its holder still owes before the sign-in is finished, so that a
    /// session which has answered only a password can be refused everything but finishing or ending it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expand only: one nullable-free column with a default, no data rewritten and nothing dropped. The
    /// default is <c>None</c> rather than the scaffolder's empty string on purpose. Existing rows were
    /// written by a build that issued a session only once the first factor had been answered, so
    /// treating them as owing nothing is what they meant; an empty string would parse as no enum member
    /// at all and would fault every request that presented one of those cookies.
    /// </para>
    /// <para>
    /// Rolling back drops the column, after which every session again reads as complete. That is the
    /// same state the previous release ran in, so a rollback loses the control rather than breaking the
    /// sessions.
    /// </para>
    /// </remarks>
    public partial class AddSessionPendingStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_step",
                schema: "identity",
                table: "sessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_step",
                schema: "identity",
                table: "sessions");
        }
    }
}
