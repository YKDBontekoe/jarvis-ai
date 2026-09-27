using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    account = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_senders = table.Column<string>(type: "jsonb", nullable: false),
                    forward_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    notify_recipient = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    webhook_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_outbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notifications_forwarded_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    peer = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_channel_messages_channel_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "channel_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_threads",
                columns: table => new
                {
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    peer = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_threads", x => new { x.connection_id, x.peer });
                    table.ForeignKey(
                        name: "FK_channel_threads_channel_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "channel_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_channel_threads_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_kind_account",
                table: "channel_connections",
                columns: new[] { "kind", "account" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_owner_id",
                table: "channel_connections",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_webhook_key",
                table: "channel_connections",
                column: "webhook_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_messages_connection_id_created_at",
                table: "channel_messages",
                columns: new[] { "connection_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_channel_messages_connection_id_external_id",
                table: "channel_messages",
                columns: new[] { "connection_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_channel_messages_direction_status_created_at",
                table: "channel_messages",
                columns: new[] { "direction", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_channel_threads_conversation_id",
                table: "channel_threads",
                column: "conversation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_messages");

            migrationBuilder.DropTable(
                name: "channel_threads");

            migrationBuilder.DropTable(
                name: "channel_connections");
        }
    }
}
