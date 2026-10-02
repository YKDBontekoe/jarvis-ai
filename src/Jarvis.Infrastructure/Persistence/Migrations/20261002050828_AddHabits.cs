using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHabits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "habits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    cadence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_per_week = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_habits", x => x.Id);
                    table.CheckConstraint("ck_habits_cadence", "cadence IN ('daily', 'weekly')");
                    table.CheckConstraint("ck_habits_target_per_week", "target_per_week BETWEEN 1 AND 7");
                });

            migrationBuilder.CreateTable(
                name: "habit_check_ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    habit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_habit_check_ins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_habit_check_ins_habits_habit_id",
                        column: x => x.habit_id,
                        principalTable: "habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_habit_check_ins_habit_id_check_in_date",
                table: "habit_check_ins",
                columns: new[] { "habit_id", "check_in_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_habit_check_ins_owner_id_check_in_date",
                table: "habit_check_ins",
                columns: new[] { "owner_id", "check_in_date" });

            migrationBuilder.CreateIndex(
                name: "IX_habits_owner_id_archived_at",
                table: "habits",
                columns: new[] { "owner_id", "archived_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "habit_check_ins");

            migrationBuilder.DropTable(
                name: "habits");
        }
    }
}
