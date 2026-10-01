using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemorySearchHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_hints",
                table: "memories",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "memories",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('simple'::regconfig, content || ' ' || coalesce(search_hints, ''))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "to_tsvector('simple'::regconfig, content)",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "search_hints",
                table: "memories");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "memories",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('simple'::regconfig, content)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "to_tsvector('simple'::regconfig, content || ' ' || coalesce(search_hints, ''))",
                oldStored: true);
        }
    }
}
