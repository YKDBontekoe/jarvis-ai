using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    error_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_signals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "turn_traces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    memory_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    skills_json = table.Column<string>(type: "jsonb", nullable: false),
                    tools_json = table.Column<string>(type: "jsonb", nullable: false),
                    total_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turn_traces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_signals_owner_id_created_at",
                table: "learning_signals",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_signals_owner_id_kind_created_at",
                table: "learning_signals",
                columns: new[] { "owner_id", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_turn_traces_owner_id_created_at",
                table: "turn_traces",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_turn_traces_owner_id_message_id",
                table: "turn_traces",
                columns: new[] { "owner_id", "message_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_signals");

            migrationBuilder.DropTable(
                name: "turn_traces");
        }
    }
}
