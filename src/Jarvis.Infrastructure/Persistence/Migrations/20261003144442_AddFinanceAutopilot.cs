using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceAutopilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_source",
                table: "expenses");

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    monthly_limit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    alerted_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    alerted_level = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.Id);
                    table.CheckConstraint("ck_budgets_alert_level", "alerted_level IN (0, 80, 100)");
                    table.CheckConstraint("ck_budgets_limit_positive", "monthly_limit > 0");
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    merchant = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cadence = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    last_charged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    next_due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    charge_count = table.Column<int>(type: "integer", nullable: false),
                    previous_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    remind_days_before = table.Column<int>(type: "integer", nullable: true),
                    reminder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                    table.CheckConstraint("ck_subscriptions_cadence", "cadence IN ('weekly', 'monthly', 'quarterly', 'yearly')");
                    table.CheckConstraint("ck_subscriptions_status", "status IN ('active', 'dismissed', 'cancelled')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_source",
                table: "expenses",
                sql: "source IN ('chat', 'receipt', 'app', 'import')");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_owner_id_category",
                table: "budgets",
                columns: new[] { "owner_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_owner_id_merchant_key_currency",
                table: "subscriptions",
                columns: new[] { "owner_id", "merchant_key", "currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_source",
                table: "expenses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_source",
                table: "expenses",
                sql: "source IN ('chat', 'receipt', 'app')");
        }
    }
}
