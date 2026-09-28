using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonalAssistantLoops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credential_provider",
                table: "condition_watches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "condition_watches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "public_json");

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "condition_watches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "condition_watches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minutes_before",
                table: "condition_watches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "radius_meters",
                table: "condition_watches",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coding_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Repository = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Task = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    worktree_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    diff_summary = table.Column<string>(type: "text", nullable: true),
                    changed_files = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_telemetry",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    battery_percent = table.Column<int>(type: "integer", nullable: true),
                    charging = table.Column<bool>(type: "boolean", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_telemetry", x => x.owner_id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_oauth_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    server_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code_verifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    authorization_endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    token_endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    registration_endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_oauth_sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coding_runs_owner_id_created_at",
                table: "coding_runs",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_mcp_oauth_sessions_owner_id_created_at",
                table: "mcp_oauth_sessions",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_mcp_oauth_sessions_State",
                table: "mcp_oauth_sessions",
                column: "State",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coding_runs");

            migrationBuilder.DropTable(
                name: "device_telemetry");

            migrationBuilder.DropTable(
                name: "mcp_oauth_sessions");

            migrationBuilder.DropColumn(
                name: "credential_provider",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "minutes_before",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "radius_meters",
                table: "condition_watches");
        }
    }
}
