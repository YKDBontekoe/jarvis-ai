using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "paused_from",
                table: "habits",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paused_until",
                table: "habits",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paused_from",
                table: "habits");

            migrationBuilder.DropColumn(
                name: "paused_until",
                table: "habits");
        }
    }
}
