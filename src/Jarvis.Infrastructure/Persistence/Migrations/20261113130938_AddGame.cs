using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_bets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    evidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_bets", x => x.Id);
                    table.CheckConstraint("ck_game_bets_confidence", "confidence BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "game_quests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    is_boss = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_quests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_bets_owner_id_created_at",
                table: "game_bets",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_game_bets_owner_id_subject_key",
                table: "game_bets",
                columns: new[] { "owner_id", "subject_key" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_game_quests_owner_id_week_start_subject_key",
                table: "game_quests",
                columns: new[] { "owner_id", "week_start", "subject_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_bets");

            migrationBuilder.DropTable(
                name: "game_quests");
        }
    }
}
