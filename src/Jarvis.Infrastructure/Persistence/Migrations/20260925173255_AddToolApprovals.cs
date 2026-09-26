using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToolApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToolCallId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tool_approvals_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tool_approvals_ConversationId",
                table: "tool_approvals",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_approvals_OwnerId_Status_CreatedAt",
                table: "tool_approvals",
                columns: new[] { "OwnerId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tool_approvals");
        }
    }
}
