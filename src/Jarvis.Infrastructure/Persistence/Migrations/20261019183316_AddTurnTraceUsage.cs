using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnTraceUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cached_input_tokens",
                table: "turn_traces",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "input_tokens",
                table: "turn_traces",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "model_calls",
                table: "turn_traces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "output_tokens",
                table: "turn_traces",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cached_input_tokens",
                table: "turn_traces");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "turn_traces");

            migrationBuilder.DropColumn(
                name: "model_calls",
                table: "turn_traces");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "turn_traces");
        }
    }
}
