using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyBriefings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_briefings",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    local_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    schedule_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_delivered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_briefings", x => x.owner_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_briefings_enabled_schedule_dispatched_at",
                table: "daily_briefings",
                columns: new[] { "enabled", "schedule_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_briefings_workflow_id",
                table: "daily_briefings",
                column: "workflow_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_briefings");
        }
    }
}
