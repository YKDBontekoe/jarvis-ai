using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodingRunPullRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch_name",
                table: "coding_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pull_request_number",
                table: "coding_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_repository",
                table: "coding_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_state",
                table: "coding_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_url",
                table: "coding_runs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_name",
                table: "coding_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_number",
                table: "coding_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_repository",
                table: "coding_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_state",
                table: "coding_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_url",
                table: "coding_runs");
        }
    }
}
