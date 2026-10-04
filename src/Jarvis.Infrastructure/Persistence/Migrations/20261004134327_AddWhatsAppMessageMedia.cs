using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessageMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "media",
                table: "whatsapp_messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "whatsapp_message_media",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_message_media", x => x.message_id);
                    table.ForeignKey(
                        name: "FK_whatsapp_message_media_whatsapp_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "whatsapp_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_media_owner_id",
                table: "whatsapp_message_media",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whatsapp_message_media");

            migrationBuilder.DropColumn(
                name: "media",
                table: "whatsapp_messages");
        }
    }
}
