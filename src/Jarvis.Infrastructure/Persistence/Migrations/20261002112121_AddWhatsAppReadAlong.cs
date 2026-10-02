using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppReadAlong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whatsapp_chats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_group = table.Column<bool>(type: "boolean", nullable: false),
                    read_along = table.Column<bool>(type: "boolean", nullable: false),
                    auto_reminders = table.Column<bool>(type: "boolean", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scanned_through = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scan_lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ask_conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_chats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_chats_channel_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "channel_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_me = table.Column<bool>(type: "boolean", nullable: false),
                    sender = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_messages_channel_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "channel_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_chats_connection_id_chat_id",
                table: "whatsapp_chats",
                columns: new[] { "connection_id", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_chats_owner_id_last_message_at",
                table: "whatsapp_chats",
                columns: new[] { "owner_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_messages_connection_id_chat_id_sent_at",
                table: "whatsapp_messages",
                columns: new[] { "connection_id", "chat_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_messages_connection_id_external_id",
                table: "whatsapp_messages",
                columns: new[] { "connection_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_messages_owner_id_sent_at",
                table: "whatsapp_messages",
                columns: new[] { "owner_id", "sent_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whatsapp_chats");

            migrationBuilder.DropTable(
                name: "whatsapp_messages");
        }
    }
}
