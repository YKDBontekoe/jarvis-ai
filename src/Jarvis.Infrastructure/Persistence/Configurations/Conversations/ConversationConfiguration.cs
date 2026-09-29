using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Conversations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
            builder.ToTable("conversations");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
            builder.Property(x => x.ProfileSnapshotJson).HasColumnType("jsonb");
            builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
            builder.HasIndex(x => new { x.OwnerId, x.ProfileId });
            builder.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}
