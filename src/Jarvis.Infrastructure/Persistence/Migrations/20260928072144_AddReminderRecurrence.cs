using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_delivered_at",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "local_time",
                table: "reminders",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurrence",
                table: "reminders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "reminders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<DateOnly>(
                name: "until",
                table: "reminders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "weekdays",
                table: "reminders",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_delivered_at",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "local_time",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "recurrence",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "until",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "weekdays",
                table: "reminders");
        }
    }
}
