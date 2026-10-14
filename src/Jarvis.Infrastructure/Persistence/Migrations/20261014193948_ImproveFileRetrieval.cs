using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImproveFileRetrieval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "partially_indexed",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "file_content_chunks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "heading",
                table: "file_content_chunks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_text",
                table: "file_content_chunks",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('simple'::regconfig, coalesce(heading, '') || ' ' || content)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "to_tsvector('simple'::regconfig, content)",
                oldStored: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_content_chunks_content",
                table: "file_content_chunks",
                column: "content")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_file_content_chunks_owner_id_unembedded",
                table: "file_content_chunks",
                column: "owner_id",
                filter: "embedding IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_content_chunks_content",
                table: "file_content_chunks");

            migrationBuilder.DropIndex(
                name: "ix_file_content_chunks_owner_id_unembedded",
                table: "file_content_chunks");

            migrationBuilder.DropColumn(
                name: "partially_indexed",
                table: "files");

            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "file_content_chunks");

            migrationBuilder.DropColumn(
                name: "heading",
                table: "file_content_chunks");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_text",
                table: "file_content_chunks",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('simple'::regconfig, content)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "to_tsvector('simple'::regconfig, coalesce(heading, '') || ' ' || content)",
                oldStored: true);
        }
    }
}
