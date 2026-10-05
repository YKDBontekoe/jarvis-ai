using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppCatchUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "catch_up",
                table: "whatsapp_chats",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "catch_up_lease_until",
                table: "whatsapp_chats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "catch_up_through",
                table: "whatsapp_chats",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "catch_up",
                table: "whatsapp_chats");

            migrationBuilder.DropColumn(
                name: "catch_up_lease_until",
                table: "whatsapp_chats");

            migrationBuilder.DropColumn(
                name: "catch_up_through",
                table: "whatsapp_chats");
        }
    }
}
