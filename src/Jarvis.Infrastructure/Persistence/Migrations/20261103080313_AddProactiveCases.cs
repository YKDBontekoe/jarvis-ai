using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProactiveCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proactive_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proactive_cases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_proactive_cases_next_check_at",
                table: "proactive_cases",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "IX_proactive_cases_owner_id_fingerprint",
                table: "proactive_cases",
                columns: new[] { "owner_id", "fingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proactive_cases");
        }
    }
}
