using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
            builder.ToTable("push_devices");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Token).HasMaxLength(4096).IsRequired();
            builder.Property(x => x.Platform).HasMaxLength(16).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => x.Token).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        }
}
