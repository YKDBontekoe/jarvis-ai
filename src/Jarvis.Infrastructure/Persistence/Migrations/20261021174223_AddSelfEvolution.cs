using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals");

            migrationBuilder.AddColumn<string[]>(
                name: "required_tools",
                table: "skills",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals",
                sql: "kind IN ('memory', 'skill', 'review', 'lesson', 'skill_refine', 'capability')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Capability proposals cannot exist under the older rule, so they go with it.
            migrationBuilder.Sql("DELETE FROM improvement_proposals WHERE kind = 'capability'");
            migrationBuilder.DropCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals");

            migrationBuilder.DropColumn(
                name: "required_tools",
                table: "skills");

            migrationBuilder.AddCheckConstraint(
                name: "ck_improvement_proposals_kind",
                table: "improvement_proposals",
                sql: "kind IN ('memory', 'skill', 'review', 'lesson', 'skill_refine')");
        }
    }
}
