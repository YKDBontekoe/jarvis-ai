using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class A2ATokenEntityConfiguration : IEntityTypeConfiguration<A2ATokenEntity>
{
    public void Configure(EntityTypeBuilder<A2ATokenEntity> builder)
    {
            builder.ToTable("a2a_tokens");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            builder.HasIndex(x => x.TokenHash).IsUnique();
            builder.HasIndex(x => x.OwnerId);
        }
}
