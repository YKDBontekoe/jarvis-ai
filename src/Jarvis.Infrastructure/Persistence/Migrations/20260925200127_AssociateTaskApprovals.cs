using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssociateTaskApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TaskId",
                table: "tool_approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_approvals_TaskId",
                table: "tool_approvals",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_tool_approvals_tasks_TaskId",
                table: "tool_approvals",
                column: "TaskId",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tool_approvals_tasks_TaskId",
                table: "tool_approvals");

            migrationBuilder.DropIndex(
                name: "IX_tool_approvals_TaskId",
                table: "tool_approvals");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "tool_approvals");
        }
    }
}
