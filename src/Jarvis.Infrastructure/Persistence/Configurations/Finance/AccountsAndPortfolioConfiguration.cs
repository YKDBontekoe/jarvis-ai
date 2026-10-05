using Jarvis.Application.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Finance;

internal sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccountEntity>
{
    public void Configure(EntityTypeBuilder<FinancialAccountEntity> builder)
    {
        var types = string.Join(", ", AccountTypes.All.Select(x => $"'{x}'"));
        builder.ToTable("financial_accounts", table => table.HasCheckConstraint("ck_financial_accounts_type",
            $"type IN ({types})"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(AccountRules.MaxNameLength).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Institution).HasColumnName("institution").HasMaxLength(80);
        builder.Property(x => x.Last4).HasColumnName("last4").HasMaxLength(4);
        builder.Property(x => x.OpeningBalance).HasColumnName("opening_balance").HasPrecision(16, 2);
        builder.Property(x => x.OpeningOn).HasColumnName("opening_on");
        builder.Property(x => x.Archived).HasColumnName("archived");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.OwnerId);
    }
}

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<HoldingEntity>
{
    public void Configure(EntityTypeBuilder<HoldingEntity> builder)
    {
        var types = string.Join(", ", AssetTypes.All.Select(x => $"'{x}'"));
        builder.ToTable("holdings", table => table.HasCheckConstraint("ck_holdings_asset_type",
            $"asset_type IN ({types})"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.AccountId).HasColumnName("account_id");
        builder.Property(x => x.Symbol).HasColumnName("symbol").HasMaxLength(PortfolioRules.MaxSymbolLength).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(PortfolioRules.MaxNameLength).IsRequired();
        builder.Property(x => x.AssetType).HasColumnName("asset_type").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.LastPrice).HasColumnName("last_price").HasPrecision(18, 6);
        builder.Property(x => x.LastPriceAt).HasColumnName("last_price_at");
        builder.Property(x => x.PriceSource).HasColumnName("price_source").HasMaxLength(12);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Symbol, x.AccountId }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<FinancialAccountEntity>().WithMany().HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class InvestmentTradeConfiguration : IEntityTypeConfiguration<InvestmentTradeEntity>
{
    public void Configure(EntityTypeBuilder<InvestmentTradeEntity> builder)
    {
        builder.ToTable("investment_trades", table =>
        {
            table.HasCheckConstraint("ck_investment_trades_kind",
                "kind IN ('buy', 'sell', 'dividend', 'fee', 'split')");
            table.HasCheckConstraint("ck_investment_trades_amounts", "quantity > 0 AND price >= 0 AND fees >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.HoldingId).HasColumnName("holding_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired();
        builder.Property(x => x.TradedOn).HasColumnName("traded_on");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(20, 8);
        builder.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 6);
        builder.Property(x => x.Fees).HasColumnName("fees").HasPrecision(14, 2);
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.HoldingId, x.TradedOn });
        builder.HasOne<HoldingEntity>().WithMany().HasForeignKey(x => x.HoldingId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PricePointConfiguration : IEntityTypeConfiguration<PricePointEntity>
{
    public void Configure(EntityTypeBuilder<PricePointEntity> builder)
    {
        builder.ToTable("price_history");
        builder.HasKey(x => new { x.HoldingId, x.Date });
        builder.Property(x => x.HoldingId).HasColumnName("holding_id");
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 6);
        builder.HasOne<HoldingEntity>().WithMany().HasForeignKey(x => x.HoldingId).OnDelete(DeleteBehavior.Cascade);
    }
}
