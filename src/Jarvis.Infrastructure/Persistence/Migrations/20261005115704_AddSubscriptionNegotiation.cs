using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionNegotiation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_url",
                table: "subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "negotiation_goal",
                table: "subscriptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "negotiation_started_at",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "negotiation_task_id",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_negotiation_task_id",
                table: "subscriptions",
                column: "negotiation_task_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_negotiation_goal",
                table: "subscriptions",
                sql: "negotiation_goal IS NULL OR negotiation_goal IN ('cancel', 'lower_price')");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_tasks_negotiation_task_id",
                table: "subscriptions",
                column: "negotiation_task_id",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_tasks_negotiation_task_id",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_negotiation_task_id",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_negotiation_goal",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "cancel_url",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "negotiation_goal",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "negotiation_started_at",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "negotiation_task_id",
                table: "subscriptions");
        }
    }
}
