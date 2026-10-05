using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonChannelLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "person_channel_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tone_score = table.Column<int>(type: "integer", nullable: true),
                    tone_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_channel_links", x => x.Id);
                    table.CheckConstraint("ck_person_channel_links_tone", "tone_score IS NULL OR tone_score BETWEEN -2 AND 2");
                    table.ForeignKey(
                        name: "FK_person_channel_links_channel_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "channel_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_person_channel_links_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_person_channel_links_connection_id",
                table: "person_channel_links",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_person_channel_links_owner_id_person_id",
                table: "person_channel_links",
                columns: new[] { "owner_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_person_channel_links_person_id",
                table: "person_channel_links",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ux_person_channel_links_owner_chat",
                table: "person_channel_links",
                columns: new[] { "owner_id", "connection_id", "chat_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "person_channel_links");
        }
    }
}
