using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reading_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    url_key = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    site_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    excerpt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    summary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    key_points = table.Column<string[]>(type: "text[]", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: true),
                    reading_minutes = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fetch_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_fetch_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_items", x => x.Id);
                    table.CheckConstraint("ck_reading_items_source", "source IN ('app', 'chat')");
                    table.CheckConstraint("ck_reading_items_status", "status IN ('pending', 'ready', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_reading_items_owner_id_created_at",
                table: "reading_items",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_reading_items_owner_id_url_key",
                table: "reading_items",
                columns: new[] { "owner_id", "url_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reading_items_status_next_fetch_at",
                table: "reading_items",
                columns: new[] { "status", "next_fetch_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reading_items");
        }
    }
}
