using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class UiSurfaceEntityConfiguration : IEntityTypeConfiguration<UiSurfaceEntity>
{
    public void Configure(EntityTypeBuilder<UiSurfaceEntity> builder)
    {
            builder.ToTable("ui_surfaces");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(80).IsRequired();
            builder.Property(x => x.SchemaJson).HasColumnName("schema").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            builder.Property(x => x.CompletedAction).HasColumnName("completed_action").HasMaxLength(40);
            builder.Property(x => x.ValuesJson).HasColumnName("values").HasColumnType("jsonb");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.ConversationId, x.CreatedAt });
        }
}
