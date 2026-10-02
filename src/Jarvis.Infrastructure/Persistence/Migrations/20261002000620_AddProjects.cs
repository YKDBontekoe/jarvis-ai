using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_files_owner_id_project_id",
                table: "files",
                columns: new[] { "owner_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_files_project_id",
                table: "files",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_OwnerId_project_id_UpdatedAt",
                table: "conversations",
                columns: new[] { "OwnerId", "project_id", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_project_id",
                table: "conversations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_projects_owner_id_updated_at",
                table: "projects",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_projects_project_id",
                table: "conversations",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_files_projects_project_id",
                table: "files",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_projects_project_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_files_projects_project_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "IX_files_owner_id_project_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_project_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_conversations_OwnerId_project_id_UpdatedAt",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_project_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "conversations");
        }
    }
}
