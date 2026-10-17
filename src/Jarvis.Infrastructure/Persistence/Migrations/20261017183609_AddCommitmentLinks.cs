using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitmentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "person_id",
                table: "commitments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "commitments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_commitments_owner_id_person_id",
                table: "commitments",
                columns: new[] { "owner_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_owner_id_project_id",
                table: "commitments",
                columns: new[] { "owner_id", "project_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commitments_owner_id_person_id",
                table: "commitments");

            migrationBuilder.DropIndex(
                name: "IX_commitments_owner_id_project_id",
                table: "commitments");

            migrationBuilder.DropColumn(
                name: "person_id",
                table: "commitments");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "commitments");
        }
    }
}
