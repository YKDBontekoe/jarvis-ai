using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tasks_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_conversation_id",
                table: "tasks",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_owner_id_conversation_id_Status",
                table: "tasks",
                columns: new[] { "owner_id", "conversation_id", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_owner_id_created_at",
                table: "tasks",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_workflow_id",
                table: "tasks",
                column: "workflow_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
