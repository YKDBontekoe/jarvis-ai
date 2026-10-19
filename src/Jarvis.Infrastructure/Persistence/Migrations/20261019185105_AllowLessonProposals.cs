using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowLessonProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals",
                sql: "kind IN ('memory', 'skill', 'review', 'lesson')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lessons cannot exist under the older rule, so they go with it.
            migrationBuilder.Sql("DELETE FROM improvement_proposals WHERE kind = 'lesson'");
            migrationBuilder.DropCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals",
                sql: "kind IN ('memory', 'skill', 'review')");
        }
    }
}
