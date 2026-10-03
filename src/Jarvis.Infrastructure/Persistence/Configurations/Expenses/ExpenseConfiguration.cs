using Jarvis.Application.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Expenses;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<ExpenseEntity>
{
    public void Configure(EntityTypeBuilder<ExpenseEntity> builder)
    {
        var categories = string.Join(", ", ExpenseCategories.All.Select(x => $"'{x}'"));
        builder.ToTable("expenses", table =>
        {
            table.HasCheckConstraint("ck_expenses_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_expenses_category", $"category IN ({categories})");
            table.HasCheckConstraint("ck_expenses_source", "source IN ('chat', 'receipt', 'app', 'import')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Merchant).HasColumnName("merchant").HasMaxLength(ExpenseRules.MaxMerchantLength);
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(ExpenseRules.MaxNoteLength);
        builder.Property(x => x.SpentOn).HasColumnName("spent_on");
        builder.Property(x => x.ReceiptFileId).HasColumnName("receipt_file_id");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.SpentOn });
    }
}
