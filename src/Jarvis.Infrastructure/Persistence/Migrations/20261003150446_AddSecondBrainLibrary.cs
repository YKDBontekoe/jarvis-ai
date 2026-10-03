using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecondBrainLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flashcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    front = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    back = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ease = table.Column<double>(type: "double precision", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    repetitions = table.Column<int>(type: "integer", nullable: false),
                    lapses = table.Column<int>(type: "integer", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    last_reviewed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flashcards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "library_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    url = table.Column<string>(type: "character varying(2100)", maxLength: 2100, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    key_points = table.Column<string[]>(type: "text[]", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_items", x => x.Id);
                    table.CheckConstraint("ck_library_items_kind", "kind IN ('web', 'note', 'report')");
                    table.CheckConstraint("ck_library_items_origin", "origin IN ('app', 'chat', 'research')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_flashcards_item_id",
                table: "flashcards",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_flashcards_owner_id_due_on",
                table: "flashcards",
                columns: new[] { "owner_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "IX_library_items_owner_id_created_at",
                table: "library_items",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_library_items_owner_id_url",
                table: "library_items",
                columns: new[] { "owner_id", "url" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flashcards");

            migrationBuilder.DropTable(
                name: "library_items");
        }
    }
}
