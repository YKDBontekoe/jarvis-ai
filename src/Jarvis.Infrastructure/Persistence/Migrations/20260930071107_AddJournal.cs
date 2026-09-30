using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    highlights = table.Column<string>(type: "text", nullable: true),
                    gratitude = table.Column<string>(type: "text", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    mood = table.Column<int>(type: "integer", nullable: true),
                    energy = table.Column<int>(type: "integer", nullable: true),
                    stress = table.Column<int>(type: "integer", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    memory_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.Id);
                    table.CheckConstraint("ck_journal_entries_energy", "energy IS NULL OR energy BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_journal_entries_mood", "mood IS NULL OR mood BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_journal_entries_rating", "rating IS NULL OR rating BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_journal_entries_stress", "stress IS NULL OR stress BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_owner_id_entry_date",
                table: "journal_entries",
                columns: new[] { "owner_id", "entry_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_entries");
        }
    }
}
