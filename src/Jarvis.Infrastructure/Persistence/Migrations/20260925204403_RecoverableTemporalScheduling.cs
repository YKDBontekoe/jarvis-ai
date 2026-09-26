using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecoverableTemporalScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "schedule_dispatched_at",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "schedule_dispatched_at",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "schedule_dispatched_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_Status_schedule_dispatched_at",
                table: "tasks",
                columns: new[] { "Status", "schedule_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "IX_reminders_Status_schedule_dispatched_at",
                table: "reminders",
                columns: new[] { "Status", "schedule_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "IX_files_processing_status_schedule_dispatched_at",
                table: "files",
                columns: new[] { "processing_status", "schedule_dispatched_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_Status_schedule_dispatched_at",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_reminders_Status_schedule_dispatched_at",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_files_processing_status_schedule_dispatched_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "schedule_dispatched_at",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "schedule_dispatched_at",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "schedule_dispatched_at",
                table: "files");
        }
    }
}
