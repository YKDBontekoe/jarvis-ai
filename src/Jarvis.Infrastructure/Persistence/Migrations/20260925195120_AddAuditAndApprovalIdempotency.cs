using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditAndApprovalIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    Tool = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Action = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    risk_class = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tool_approvals_idempotency",
                table: "tool_approvals",
                columns: new[] { "OwnerId", "RequestId", "ToolCallId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_owner_id_timestamp",
                table: "audit_events",
                columns: new[] { "owner_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropIndex(
                name: "ux_tool_approvals_idempotency",
                table: "tool_approvals");
        }
    }
}
