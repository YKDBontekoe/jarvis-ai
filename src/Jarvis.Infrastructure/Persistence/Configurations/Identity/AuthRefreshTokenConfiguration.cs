using Jarvis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Identity;

internal sealed class AuthRefreshTokenConfiguration : IEntityTypeConfiguration<AuthRefreshToken>
{
    public void Configure(EntityTypeBuilder<AuthRefreshToken> builder)
    {
            builder.ToTable("auth_refresh_tokens");
            builder.HasKey(token => token.Id);
            builder.Property(token => token.Id).ValueGeneratedNever();
            builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            builder.Property(token => token.ReplacedByHash).HasMaxLength(128);
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.HasIndex(token => new { token.UserId, token.ExpiresAt });
            builder.HasOne<JarvisUser>().WithMany().HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}
