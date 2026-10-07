using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Learning;

internal sealed class TurnTraceConfiguration : IEntityTypeConfiguration<TurnTraceEntity>
{
    public void Configure(EntityTypeBuilder<TurnTraceEntity> builder)
    {
        builder.ToTable("turn_traces");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.ProfileId).HasColumnName("profile_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(x => x.MemoryIdsJson).HasColumnName("memory_ids_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SkillsJson).HasColumnName("skills_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ToolsJson).HasColumnName("tools_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TotalMs).HasColumnName("total_ms");
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        builder.HasIndex(x => new { x.OwnerId, x.MessageId });
    }
}
