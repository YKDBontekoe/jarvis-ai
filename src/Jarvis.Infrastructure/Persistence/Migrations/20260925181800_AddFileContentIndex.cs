using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileContentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_content_chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    search_text = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "to_tsvector('simple'::regconfig, content)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_content_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_content_chunks_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_content_chunks_embedding",
                table: "file_content_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_file_content_chunks_file_id",
                table: "file_content_chunks",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "IX_file_content_chunks_owner_id_file_id_chunk_index",
                table: "file_content_chunks",
                columns: new[] { "owner_id", "file_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_content_chunks_search_text",
                table: "file_content_chunks",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_content_chunks");
        }
    }
}
