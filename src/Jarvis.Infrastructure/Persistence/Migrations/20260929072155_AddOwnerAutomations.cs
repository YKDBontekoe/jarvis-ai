using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    schedule_workflow_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    schedule_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cooldown_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "automation_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trigger_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trigger_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    test_run = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    action_results_json = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    failure_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_owner_id_Status_updated_at",
                table: "automation_rules",
                columns: new[] { "owner_id", "Status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_schedule_workflow_id",
                table: "automation_rules",
                column: "schedule_workflow_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_Status_schedule_dispatched_at",
                table: "automation_rules",
                columns: new[] { "Status", "schedule_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_owner_id_started_at",
                table: "automation_runs",
                columns: new[] { "owner_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_rule_id_idempotency_key",
                table: "automation_runs",
                columns: new[] { "rule_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_rule_id_started_at",
                table: "automation_runs",
                columns: new[] { "rule_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_workflow_id",
                table: "automation_runs",
                column: "workflow_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_rules");

            migrationBuilder.DropTable(
                name: "automation_runs");
        }
    }
}
