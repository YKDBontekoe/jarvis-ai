using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileCollectionsAndCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "citations_json",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "end_offset",
                table: "file_content_chunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "page_number",
                table: "file_content_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "start_offset",
                table: "file_content_chunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "conversation_file_attachments",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_file_attachments", x => new { x.ConversationId, x.FileId });
                    table.ForeignKey(
                        name: "FK_conversation_file_attachments_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_file_attachments_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_collection_attachments",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_collection_attachments", x => new { x.ConversationId, x.CollectionId });
                    table.ForeignKey(
                        name: "FK_conversation_collection_attachments_conversations_Conversat~",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_collection_attachments_file_collections_Collec~",
                        column: x => x.CollectionId,
                        principalTable: "file_collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_collection_members",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_collection_members", x => new { x.CollectionId, x.FileId });
                    table.ForeignKey(
                        name: "FK_file_collection_members_file_collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "file_collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_file_collection_members_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_collection_attachments_CollectionId",
                table: "conversation_collection_attachments",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_file_attachments_FileId",
                table: "conversation_file_attachments",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_file_collection_members_FileId",
                table: "file_collection_members",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_file_collection_members_owner_id_FileId",
                table: "file_collection_members",
                columns: new[] { "owner_id", "FileId" });

            migrationBuilder.CreateIndex(
                name: "IX_file_collections_owner_id_normalized_name",
                table: "file_collections",
                columns: new[] { "owner_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_collection_attachments");

            migrationBuilder.DropTable(
                name: "conversation_file_attachments");

            migrationBuilder.DropTable(
                name: "file_collection_members");

            migrationBuilder.DropTable(
                name: "file_collections");

            migrationBuilder.DropColumn(
                name: "citations_json",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "end_offset",
                table: "file_content_chunks");

            migrationBuilder.DropColumn(
                name: "page_number",
                table: "file_content_chunks");

            migrationBuilder.DropColumn(
                name: "start_offset",
                table: "file_content_chunks");
        }
    }
}
