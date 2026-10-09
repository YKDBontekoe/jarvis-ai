using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComputerUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "screenshot_key",
                table: "browser_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "control_mode",
                table: "browser_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "agent");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "browser_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "browser");

            migrationBuilder.CreateIndex(
                name: "ix_browser_sessions_active_computer",
                table: "browser_sessions",
                column: "kind",
                unique: true,
                filter: "kind = 'computer' AND status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_browser_sessions_active_computer",
                table: "browser_sessions");

            migrationBuilder.DropColumn(
                name: "screenshot_key",
                table: "browser_steps");

            migrationBuilder.DropColumn(
                name: "control_mode",
                table: "browser_sessions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "browser_sessions");
        }
    }
}
