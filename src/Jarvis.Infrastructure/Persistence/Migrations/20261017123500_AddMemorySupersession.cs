using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemorySupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_id",
                table: "memories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_memories_superseded_by_id",
                table: "memories",
                column: "superseded_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memories_superseded_by_id",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "superseded_by_id",
                table: "memories");
        }
    }
}
