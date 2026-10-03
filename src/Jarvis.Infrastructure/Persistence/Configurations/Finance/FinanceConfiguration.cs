using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Finance;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<BudgetEntity>
{
    public void Configure(EntityTypeBuilder<BudgetEntity> builder)
    {
        builder.ToTable("budgets", table =>
        {
            table.HasCheckConstraint("ck_budgets_limit_positive", "monthly_limit > 0");
            table.HasCheckConstraint("ck_budgets_alert_level", "alerted_level IN (0, 80, 100)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Limit).HasColumnName("monthly_limit").HasPrecision(14, 2);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.AlertedMonth).HasColumnName("alerted_month").HasMaxLength(7);
        builder.Property(x => x.AlertedLevel).HasColumnName("alerted_level");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Category }).IsUnique();
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<SubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<SubscriptionEntity> builder)
    {
        builder.ToTable("subscriptions", table =>
        {
            table.HasCheckConstraint("ck_subscriptions_status", "status IN ('active', 'dismissed', 'cancelled')");
            table.HasCheckConstraint("ck_subscriptions_cadence",
                "cadence IN ('weekly', 'monthly', 'quarterly', 'yearly')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.MerchantKey).HasColumnName("merchant_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Merchant).HasColumnName("merchant").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Cadence).HasColumnName("cadence").HasMaxLength(12).IsRequired();
        builder.Property(x => x.LastChargedOn).HasColumnName("last_charged_on");
        builder.Property(x => x.NextDueOn).HasColumnName("next_due_on");
        builder.Property(x => x.ChargeCount).HasColumnName("charge_count");
        builder.Property(x => x.PreviousAmount).HasColumnName("previous_amount").HasPrecision(12, 2);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(12).IsRequired();
        builder.Property(x => x.RemindDaysBefore).HasColumnName("remind_days_before");
        builder.Property(x => x.ReminderId).HasColumnName("reminder_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.MerchantKey, x.Currency }).IsUnique();
    }
}
