using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecoverableApprovalResumes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResumeStartedAt",
                table: "tool_approvals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeStatus",
                table: "tool_approvals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "not_started");

            migrationBuilder.Sql("""
                UPDATE tool_approvals
                SET "ResumeStatus" = 'failed', "ResumeStartedAt" = COALESCE("DecidedAt", "CreatedAt")
                WHERE "Status" IN ('approved', 'rejected');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResumeStartedAt",
                table: "tool_approvals");

            migrationBuilder.DropColumn(
                name: "ResumeStatus",
                table: "tool_approvals");
        }
    }
}
