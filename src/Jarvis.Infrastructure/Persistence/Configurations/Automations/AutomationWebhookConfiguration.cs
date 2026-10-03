using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Automations;

internal sealed class AutomationWebhookConfiguration : IEntityTypeConfiguration<AutomationWebhookEntity>
{
    public void Configure(EntityTypeBuilder<AutomationWebhookEntity> entity)
    {
        entity.ToTable("automation_webhooks");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.OwnerId).HasColumnName("owner_id");
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(60).IsRequired();
        entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        entity.Property(x => x.TokenHint).HasColumnName("token_hint").HasMaxLength(8).IsRequired();
        entity.Property(x => x.UseCount).HasColumnName("use_count");
        entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(x => x.TokenHash).IsUnique();
        entity.HasIndex(x => x.OwnerId);
    }
}
