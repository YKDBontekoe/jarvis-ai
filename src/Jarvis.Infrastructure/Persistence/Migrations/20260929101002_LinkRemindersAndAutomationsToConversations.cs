using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkRemindersAndAutomationsToConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "reminders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "automation_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminders_conversation_id",
                table: "reminders",
                column: "conversation_id",
                unique: true,
                filter: "conversation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_conversation_id",
                table: "automation_rules",
                column: "conversation_id",
                unique: true,
                filter: "conversation_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_automation_rules_conversations_conversation_id",
                table: "automation_rules",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_reminders_conversations_conversation_id",
                table: "reminders",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_automation_rules_conversations_conversation_id",
                table: "automation_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_reminders_conversations_conversation_id",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_reminders_conversation_id",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_automation_rules_conversation_id",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "automation_rules");
        }
    }
}
