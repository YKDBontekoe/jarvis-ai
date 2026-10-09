using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSpine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    to_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "owner_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caused_by_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entity_links_owner_id_from_type_from_id_to_type_to_id_relat~",
                table: "entity_links",
                columns: new[] { "owner_id", "from_type", "from_id", "to_type", "to_id", "relation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_links_owner_id_to_type_to_id",
                table: "entity_links",
                columns: new[] { "owner_id", "to_type", "to_id" });

            migrationBuilder.CreateIndex(
                name: "IX_owner_events_owner_id_at",
                table: "owner_events",
                columns: new[] { "owner_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_owner_events_owner_id_subject_type_subject_id",
                table: "owner_events",
                columns: new[] { "owner_id", "subject_type", "subject_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_links");

            migrationBuilder.DropTable(
                name: "owner_events");
        }
    }
}
