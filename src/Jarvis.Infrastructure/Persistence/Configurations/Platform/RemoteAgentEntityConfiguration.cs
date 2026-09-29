using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class RemoteAgentEntityConfiguration : IEntityTypeConfiguration<RemoteAgentEntity>
{
    public void Configure(EntityTypeBuilder<RemoteAgentEntity> builder)
    {
            builder.ToTable("remote_agents");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
            builder.Property(x => x.Enabled).HasColumnName("enabled");
            builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        }
}
