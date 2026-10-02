using Jarvis.Domain.Conversations;
using Jarvis.Domain.Projects;
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
            builder.HasIndex(x => new { x.OwnerId, x.PinnedAt });
            builder.HasIndex(x => new { x.OwnerId, x.ProfileId });
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(x => new { x.OwnerId, x.ProjectId, x.UpdatedAt });
            builder.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}
