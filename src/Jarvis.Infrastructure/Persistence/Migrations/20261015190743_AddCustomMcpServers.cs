using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomMcpServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_mcp_servers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    registry_server_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    draft_source = table.Column<string>(type: "text", nullable: false),
                    draft_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    active_source = table.Column<string>(type: "text", nullable: true),
                    active_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    active_revision = table.Column<int>(type: "integer", nullable: true),
                    secrets = table.Column<string>(type: "jsonb", nullable: false),
                    last_test = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_mcp_servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_mcp_server_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_mcp_server_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_mcp_server_revisions_custom_mcp_servers_server_id",
                        column: x => x.server_id,
                        principalTable: "custom_mcp_servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_mcp_server_revisions_server_id_revision",
                table: "custom_mcp_server_revisions",
                columns: new[] { "server_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_mcp_servers_owner_id_name",
                table: "custom_mcp_servers",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_mcp_servers_owner_id_registry_server_id",
                table: "custom_mcp_servers",
                columns: new[] { "owner_id", "registry_server_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_mcp_server_revisions");

            migrationBuilder.DropTable(
                name: "custom_mcp_servers");
        }
    }
}
