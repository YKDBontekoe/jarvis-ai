using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAndCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    counterparty = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    suggested = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    inbox_thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commitments", x => x.Id);
                    table.CheckConstraint("ck_commitments_direction", "direction IN ('i_owe', 'owed_to_me')");
                    table.CheckConstraint("ck_commitments_source", "source IN ('chat', 'whatsapp', 'mail', 'manual', 'meeting')");
                    table.CheckConstraint("ck_commitments_status", "status IN ('open', 'done', 'dropped')");
                });

            migrationBuilder.CreateTable(
                name: "inbox_threads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_key = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chat_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    counterparty = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    suggested_reply = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    last_message_preview = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    last_from_me = table.Column<bool>(type: "boolean", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    snoozed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    triaged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_threads", x => x.Id);
                    table.CheckConstraint("ck_inbox_threads_priority", "priority BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_inbox_threads_source", "source IN ('whatsapp', 'mail', 'manual')");
                    table.CheckConstraint("ck_inbox_threads_state", "state IN ('needs_reply', 'waiting', 'fyi', 'snoozed', 'done')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_owner_id_due_on",
                table: "commitments",
                columns: new[] { "owner_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_owner_id_status",
                table: "commitments",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_threads_owner_id_state",
                table: "inbox_threads",
                columns: new[] { "owner_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_threads_owner_source_key",
                table: "inbox_threads",
                columns: new[] { "owner_id", "source", "external_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commitments");

            migrationBuilder.DropTable(
                name: "inbox_threads");
        }
    }
}
