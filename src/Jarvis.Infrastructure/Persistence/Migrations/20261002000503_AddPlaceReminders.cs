using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "location_inside",
                table: "reminders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "location_latitude",
                table: "reminders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "location_longitude",
                table: "reminders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_name",
                table: "reminders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "location_radius_meters",
                table: "reminders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "location_repeats",
                table: "reminders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "location_trigger",
                table: "reminders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminders_owner_id_Status_place",
                table: "reminders",
                columns: new[] { "owner_id", "Status" },
                filter: "location_latitude IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reminders_owner_id_Status_place",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_inside",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_latitude",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_longitude",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_name",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_radius_meters",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_repeats",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "location_trigger",
                table: "reminders");
        }
    }
}
