using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "push_deliveries",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_deliveries", x => new { x.notification_id, x.device_id });
                    table.ForeignKey(
                        name: "FK_push_deliveries_notifications_notification_id",
                        column: x => x.notification_id,
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_push_deliveries_push_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "push_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_delivered_at_next_attempt_at_lease_until",
                table: "push_deliveries",
                columns: new[] { "delivered_at", "next_attempt_at", "lease_until" });

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_device_id",
                table: "push_deliveries",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_devices_owner_id_updated_at",
                table: "push_devices",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_push_devices_Token",
                table: "push_devices",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_deliveries");

            migrationBuilder.DropTable(
                name: "push_devices");
        }
    }
}
