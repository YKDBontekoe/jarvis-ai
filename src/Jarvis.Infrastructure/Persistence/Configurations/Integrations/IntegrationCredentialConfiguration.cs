using Jarvis.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Integrations;

internal sealed class IntegrationCredentialConfiguration : IEntityTypeConfiguration<IntegrationCredential>
{
    public void Configure(EntityTypeBuilder<IntegrationCredential> builder)
    {
            builder.ToTable("integration_credentials");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            builder.Property(x => x.ProtectedPayload).HasColumnName("protected_payload").IsRequired();
            builder.Property(x => x.SecretNamesJson).HasColumnName("secret_names").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => new { x.OwnerId, x.Provider }).IsUnique();
        }
}
