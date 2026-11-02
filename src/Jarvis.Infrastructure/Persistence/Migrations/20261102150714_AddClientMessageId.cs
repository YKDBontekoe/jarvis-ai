using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_message_id",
                table: "messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_payload_hash",
                table: "messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_ConversationId_client_message_id",
                table: "messages",
                columns: new[] { "ConversationId", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_ConversationId_client_message_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "client_message_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "client_payload_hash",
                table: "messages");
        }
    }
}
