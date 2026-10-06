using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepeatingWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "armed",
                table: "condition_watches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "cooldown_minutes",
                table: "condition_watches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_triggered_at",
                table: "condition_watches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "repeat",
                table: "condition_watches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "trigger_count",
                table: "condition_watches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "armed",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "cooldown_minutes",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "last_triggered_at",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "repeat",
                table: "condition_watches");

            migrationBuilder.DropColumn(
                name: "trigger_count",
                table: "condition_watches");
        }
    }
}
