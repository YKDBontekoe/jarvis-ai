using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PinnedAt",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_OwnerId_PinnedAt",
                table: "conversations",
                columns: new[] { "OwnerId", "PinnedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_OwnerId_PinnedAt",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "conversations");
        }
    }
}
