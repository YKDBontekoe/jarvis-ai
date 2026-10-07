using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Learning;

/// <summary>
/// No foreign key to messages on purpose: a regenerate signal refers to the reply that regenerating deletes, and a
/// cascade would erase the signal with it.
/// </summary>
internal sealed class LearningSignalConfiguration : IEntityTypeConfiguration<LearningSignalEntity>
{
    public void Configure(EntityTypeBuilder<LearningSignalEntity> builder)
    {
        builder.ToTable("learning_signals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(30).IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.ProfileId).HasColumnName("profile_id");
        builder.Property(x => x.Tool).HasColumnName("tool").HasMaxLength(120);
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(120);
        builder.Property(x => x.ErrorKind).HasColumnName("error_kind").HasMaxLength(60);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        builder.HasIndex(x => new { x.OwnerId, x.Kind, x.CreatedAt });
    }
}
