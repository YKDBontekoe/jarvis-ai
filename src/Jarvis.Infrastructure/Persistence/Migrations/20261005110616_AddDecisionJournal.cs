using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    context = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    prediction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    probability = table.Column<double>(type: "double precision", nullable: false),
                    review_on = table.Column<DateOnly>(type: "date", nullable: false),
                    outcome = table.Column<bool>(type: "boolean", nullable: true),
                    outcome_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decisions", x => x.Id);
                    table.CheckConstraint("ck_decisions_probability", "probability BETWEEN 0.01 AND 0.99");
                });

            migrationBuilder.CreateIndex(
                name: "IX_decisions_owner_id_resolved_at",
                table: "decisions",
                columns: new[] { "owner_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_decisions_owner_id_review_on",
                table: "decisions",
                columns: new[] { "owner_id", "review_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decisions");
        }
    }
}
