using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profile_id",
                table: "memories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProfileId",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileSnapshotJson",
                table: "conversations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileVersion",
                table: "conversations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assistant_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    persona_instructions = table.Column<string>(type: "text", nullable: true),
                    preferred_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    reply_language = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    include_owner_persona = table.Column<bool>(type: "boolean", nullable: false),
                    restrict_skills = table.Column<bool>(type: "boolean", nullable: false),
                    enabled_skill_ids = table.Column<string>(type: "jsonb", nullable: false),
                    restrict_files = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_collection_ids = table.Column<string>(type: "jsonb", nullable: false),
                    model_class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    chat_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fast_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reasoning_effort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    memory_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    include_pinned_memories = table.Column<bool>(type: "boolean", nullable: false),
                    contribute_to_learning = table.Column<bool>(type: "boolean", nullable: false),
                    allow_persona_learning = table.Column<bool>(type: "boolean", nullable: false),
                    allow_remember = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_collection_files",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_collection_files", x => new { x.collection_id, x.file_id });
                    table.ForeignKey(
                        name: "FK_document_collection_files_document_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "document_collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_collection_files_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memories_owner_id_profile_id",
                table: "memories",
                columns: new[] { "owner_id", "profile_id" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_OwnerId_ProfileId",
                table: "conversations",
                columns: new[] { "OwnerId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_profiles_owner_id_name",
                table: "assistant_profiles",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_profiles_owner_id_updated_at",
                table: "assistant_profiles",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_assistant_profiles_default",
                table: "assistant_profiles",
                column: "owner_id",
                unique: true,
                filter: "is_default = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_document_collection_files_file_id",
                table: "document_collection_files",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_collection_files_owner_id_file_id",
                table: "document_collection_files",
                columns: new[] { "owner_id", "file_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_collections_owner_id_name",
                table: "document_collections",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_collections_owner_id_updated_at",
                table: "document_collections",
                columns: new[] { "owner_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_profiles");

            migrationBuilder.DropTable(
                name: "document_collection_files");

            migrationBuilder.DropTable(
                name: "document_collections");

            migrationBuilder.DropIndex(
                name: "IX_memories_owner_id_profile_id",
                table: "memories");

            migrationBuilder.DropIndex(
                name: "IX_conversations_OwnerId_ProfileId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "profile_id",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ProfileSnapshotJson",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ProfileVersion",
                table: "conversations");
        }
    }
}
