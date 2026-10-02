using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    relationship = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    birthday_month = table.Column<int>(type: "integer", nullable: true),
                    birthday_day = table.Column<int>(type: "integer", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    contact_every_days = table.Column<int>(type: "integer", nullable: true),
                    last_contacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    graph_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    birthday_notified_year = table.Column<int>(type: "integer", nullable: true),
                    check_in_nudged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.Id);
                    table.CheckConstraint("ck_people_birthday_day", "birthday_day IS NULL OR birthday_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_people_birthday_month", "birthday_month IS NULL OR birthday_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_people_contact_every_days", "contact_every_days IS NULL OR contact_every_days BETWEEN 1 AND 365");
                    table.ForeignKey(
                        name: "FK_people_graph_entities_graph_entity_id",
                        column: x => x.graph_entity_id,
                        principalTable: "graph_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_people_graph_entity_id",
                table: "people",
                column: "graph_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_people_owner_id_name_key",
                table: "people",
                columns: new[] { "owner_id", "name_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "people");
        }
    }
}
