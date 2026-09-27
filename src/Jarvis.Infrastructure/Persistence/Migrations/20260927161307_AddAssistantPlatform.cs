using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "a2a_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_a2a_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "browser_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    start_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_browser_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_browser_sessions_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "remote_agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ui_surfaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ui_surfaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ui_surfaces_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "browser_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    tool = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_browser_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_browser_steps_browser_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "browser_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tokens_owner_id",
                table: "a2a_tokens",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tokens_token_hash",
                table: "a2a_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_browser_sessions_conversation_id",
                table: "browser_sessions",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_browser_sessions_owner_id_conversation_id_status",
                table: "browser_sessions",
                columns: new[] { "owner_id", "conversation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_browser_steps_session_id_ordinal",
                table: "browser_steps",
                columns: new[] { "session_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_remote_agents_owner_id_name",
                table: "remote_agents",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ui_surfaces_conversation_id",
                table: "ui_surfaces",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_ui_surfaces_owner_id_conversation_id_created_at",
                table: "ui_surfaces",
                columns: new[] { "owner_id", "conversation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "a2a_tokens");

            migrationBuilder.DropTable(
                name: "browser_steps");

            migrationBuilder.DropTable(
                name: "remote_agents");

            migrationBuilder.DropTable(
                name: "ui_surfaces");

            migrationBuilder.DropTable(
                name: "browser_sessions");
        }
    }
}
