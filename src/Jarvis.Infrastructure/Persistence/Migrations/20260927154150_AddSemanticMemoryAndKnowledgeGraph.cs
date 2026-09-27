using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticMemoryAndKnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "memories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "graph_indexed_at",
                table: "memories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "graph_entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    aliases = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_entities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "graph_relations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predicate = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    object_value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<float>(type: "real", nullable: false),
                    source_memory_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_relations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_graph_relations_graph_entities_object_id",
                        column: x => x.object_id,
                        principalTable: "graph_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_graph_relations_graph_entities_subject_id",
                        column: x => x.subject_id,
                        principalTable: "graph_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_graph_relations_memories_source_memory_id",
                        column: x => x.source_memory_id,
                        principalTable: "memories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_graph_entities_owner_id_key",
                table: "graph_entities",
                columns: new[] { "owner_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_graph_entities_owner_id_updated_at",
                table: "graph_entities",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_graph_relations_object_id",
                table: "graph_relations",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "IX_graph_relations_owner_id_object_id",
                table: "graph_relations",
                columns: new[] { "owner_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_graph_relations_owner_id_subject_id_predicate_valid_to",
                table: "graph_relations",
                columns: new[] { "owner_id", "subject_id", "predicate", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "IX_graph_relations_source_memory_id",
                table: "graph_relations",
                column: "source_memory_id");

            migrationBuilder.CreateIndex(
                name: "IX_graph_relations_subject_id",
                table: "graph_relations",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "graph_relations");

            migrationBuilder.DropTable(
                name: "graph_entities");

            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "graph_indexed_at",
                table: "memories");
        }
    }
}
