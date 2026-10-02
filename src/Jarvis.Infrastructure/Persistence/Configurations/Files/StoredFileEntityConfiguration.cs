using Jarvis.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Files;

internal sealed class StoredFileEntityConfiguration : IEntityTypeConfiguration<StoredFileEntity>
{
    public void Configure(EntityTypeBuilder<StoredFileEntity> builder)
    {
            builder.ToTable("files");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(500).IsRequired();
            builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(120).IsRequired();
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            builder.Property(x => x.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(30).IsRequired();
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
            builder.HasIndex(x => x.ObjectKey).IsUnique();
            builder.HasIndex(x => new { x.ProcessingStatus, x.ScheduleDispatchedAt });
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(x => new { x.OwnerId, x.ProjectId });
        }
}
