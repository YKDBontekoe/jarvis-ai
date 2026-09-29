using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(JarvisDbContext))]
[Migration("20260929000000_AddConversationMessageCursorIndex")]
public partial class AddConversationMessageCursorIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_messages_ConversationId_CreatedAt",
            table: "messages");

        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_CreatedAt_Id",
            table: "messages",
            columns: new[] { "ConversationId", "CreatedAt", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_messages_ConversationId_CreatedAt_Id",
            table: "messages");

        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_CreatedAt",
            table: "messages",
            columns: new[] { "ConversationId", "CreatedAt" });
    }
}
