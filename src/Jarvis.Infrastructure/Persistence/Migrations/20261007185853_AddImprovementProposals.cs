using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImprovementProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "improvement_proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    evidence = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resulting_ref = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_improvement_proposals", x => x.Id);
                    table.CheckConstraint("ck_improvement_proposals_kind", "kind IN ('memory', 'skill', 'review')");
                    table.CheckConstraint("ck_improvement_proposals_status", "status IN ('pending', 'accepted', 'applied', 'dismissed', 'undone')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_improvement_proposals_owner_id_status",
                table: "improvement_proposals",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_improvement_proposals_owner_fingerprint",
                table: "improvement_proposals",
                columns: new[] { "owner_id", "fingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "improvement_proposals");
        }
    }
}
