using Jarvis.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Integrations;

internal sealed class McpOAuthSessionConfiguration : IEntityTypeConfiguration<McpOAuthSession>
{
    public void Configure(EntityTypeBuilder<McpOAuthSession> builder)
    {
            builder.ToTable("mcp_oauth_sessions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.State).HasMaxLength(80).IsRequired();
            builder.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            builder.Property(x => x.ServerKey).HasColumnName("server_key").HasMaxLength(200);
            builder.Property(x => x.CodeVerifier).HasColumnName("code_verifier").HasMaxLength(128).IsRequired();
            builder.Property(x => x.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(500).IsRequired();
            builder.Property(x => x.AuthorizationEndpoint).HasColumnName("authorization_endpoint").HasMaxLength(500);
            builder.Property(x => x.TokenEndpoint).HasColumnName("token_endpoint").HasMaxLength(500);
            builder.Property(x => x.RegistrationEndpoint).HasColumnName("registration_endpoint").HasMaxLength(500);
            builder.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(200);
            builder.Property(x => x.Resource).HasMaxLength(500);
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.Error).HasMaxLength(200);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.HasIndex(x => x.State).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        }
}
