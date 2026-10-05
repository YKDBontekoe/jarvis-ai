using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutineSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routine_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    evidence = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    automation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routine_suggestions", x => x.Id);
                    table.CheckConstraint("ck_routine_suggestions_status", "status IN ('pending', 'accepted', 'dismissed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_routine_suggestions_owner_id_status",
                table: "routine_suggestions",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_routine_suggestions_owner_fingerprint",
                table: "routine_suggestions",
                columns: new[] { "owner_id", "fingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routine_suggestions");
        }
    }
}
