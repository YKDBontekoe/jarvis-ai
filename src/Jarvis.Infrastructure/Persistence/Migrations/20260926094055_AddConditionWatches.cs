using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConditionWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "condition_watches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    JsonPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Comparison = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    threshold = table.Column<double>(type: "double precision", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    schedule_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_value = table.Column<double>(type: "double precision", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_condition_watches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_condition_watches_owner_id_Status_created_at",
                table: "condition_watches",
                columns: new[] { "owner_id", "Status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_condition_watches_Status_schedule_dispatched_at",
                table: "condition_watches",
                columns: new[] { "Status", "schedule_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "IX_condition_watches_workflow_id",
                table: "condition_watches",
                column: "workflow_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "condition_watches");
        }
    }
}
