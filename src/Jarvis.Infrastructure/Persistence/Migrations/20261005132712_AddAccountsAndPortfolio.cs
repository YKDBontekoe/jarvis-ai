using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jarvis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_category",
                table: "expenses");

            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "expenses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "expense");

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_account_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    institution = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    opening_balance = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
                    opening_on = table.Column<DateOnly>(type: "date", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_accounts", x => x.Id);
                    table.CheckConstraint("ck_financial_accounts_type", "type IN ('checking', 'savings', 'credit_card', 'cash', 'brokerage', 'other')");
                });

            migrationBuilder.CreateTable(
                name: "holdings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    asset_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    last_price_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    price_source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holdings", x => x.Id);
                    table.CheckConstraint("ck_holdings_asset_type", "asset_type IN ('stock', 'etf', 'fund', 'crypto', 'bond', 'other')");
                    table.ForeignKey(
                        name: "FK_holdings_financial_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "investment_trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    traded_on = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    fees = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_trades", x => x.Id);
                    table.CheckConstraint("ck_investment_trades_amounts", "quantity > 0 AND price >= 0 AND fees >= 0");
                    table.CheckConstraint("ck_investment_trades_kind", "kind IN ('buy', 'sell', 'dividend', 'fee', 'split')");
                    table.ForeignKey(
                        name: "FK_investment_trades_holdings_holding_id",
                        column: x => x.holding_id,
                        principalTable: "holdings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_history",
                columns: table => new
                {
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_history", x => new { x.holding_id, x.date });
                    table.ForeignKey(
                        name: "FK_price_history_holdings_holding_id",
                        column: x => x.holding_id,
                        principalTable: "holdings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_account_id",
                table: "expenses",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_owner_id_account_id_spent_on",
                table: "expenses",
                columns: new[] { "owner_id", "account_id", "spent_on" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_transfer_account_id",
                table: "expenses",
                column: "transfer_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_category",
                table: "expenses",
                sql: "category IN ('groceries', 'dining', 'transport', 'shopping', 'housing', 'bills', 'health', 'entertainment', 'travel', 'subscriptions', 'other', 'salary', 'freelance', 'interest', 'dividends', 'refund', 'gift', 'other_income', 'transfer')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_kind",
                table: "expenses",
                sql: "kind IN ('expense', 'income', 'transfer')");

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_owner_id",
                table: "financial_accounts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_holdings_account_id",
                table: "holdings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_holdings_owner_id_symbol_account_id",
                table: "holdings",
                columns: new[] { "owner_id", "symbol", "account_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_investment_trades_holding_id",
                table: "investment_trades",
                column: "holding_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_trades_owner_id_holding_id_traded_on",
                table: "investment_trades",
                columns: new[] { "owner_id", "holding_id", "traded_on" });

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_financial_accounts_account_id",
                table: "expenses",
                column: "account_id",
                principalTable: "financial_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_financial_accounts_transfer_account_id",
                table: "expenses",
                column: "transfer_account_id",
                principalTable: "financial_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expenses_financial_accounts_account_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_financial_accounts_transfer_account_id",
                table: "expenses");

            migrationBuilder.DropTable(
                name: "investment_trades");

            migrationBuilder.DropTable(
                name: "price_history");

            migrationBuilder.DropTable(
                name: "holdings");

            migrationBuilder.DropTable(
                name: "financial_accounts");

            migrationBuilder.DropIndex(
                name: "IX_expenses_account_id",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_expenses_owner_id_account_id_spent_on",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_expenses_transfer_account_id",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_category",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_kind",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "transfer_account_id",
                table: "expenses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_category",
                table: "expenses",
                sql: "category IN ('groceries', 'dining', 'transport', 'shopping', 'housing', 'bills', 'health', 'entertainment', 'travel', 'subscriptions', 'other')");
        }
    }
}
