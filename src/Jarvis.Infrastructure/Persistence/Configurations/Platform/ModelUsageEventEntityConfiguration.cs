using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class ModelUsageEventEntityConfiguration : IEntityTypeConfiguration<ModelUsageEventEntity>
{
    public void Configure(EntityTypeBuilder<ModelUsageEventEntity> builder)
    {
            builder.ToTable("model_usage_events");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(200);
            builder.Property(x => x.InputTokens).HasColumnName("input_tokens");
            builder.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            builder.Property(x => x.CachedInputTokens).HasColumnName("cached_input_tokens");
            builder.Property(x => x.ReasoningOutputTokens).HasColumnName("reasoning_output_tokens");
            builder.Property(x => x.EstimatedCostUsd).HasColumnName("estimated_cost_usd").HasColumnType("numeric(18,8)");
            builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
            builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(20).IsRequired();
            builder.Property(x => x.WebSearchActions).HasColumnName("web_search_actions");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        }
}
