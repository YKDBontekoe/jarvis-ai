using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Integrations;

internal sealed class CustomMcpServerConfiguration : IEntityTypeConfiguration<CustomMcpServerEntity>
{
    public void Configure(EntityTypeBuilder<CustomMcpServerEntity> builder)
    {
        builder.ToTable("custom_mcp_servers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(320);
        builder.Property(x => x.RegistryServerId).HasColumnName("registry_server_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Revision).HasColumnName("revision");
        builder.Property(x => x.DraftSource).HasColumnName("draft_source").IsRequired();
        builder.Property(x => x.DraftSha256).HasColumnName("draft_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActiveSource).HasColumnName("active_source");
        builder.Property(x => x.ActiveSha256).HasColumnName("active_sha256").HasMaxLength(64);
        builder.Property(x => x.ActiveRevision).HasColumnName("active_revision");
        builder.Property(x => x.SecretsJson).HasColumnName("secrets").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastTestJson).HasColumnName("last_test").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.RegistryServerId }).IsUnique();
        builder.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomMcpServerRevisionConfiguration : IEntityTypeConfiguration<CustomMcpServerRevisionEntity>
{
    public void Configure(EntityTypeBuilder<CustomMcpServerRevisionEntity> builder)
    {
        builder.ToTable("custom_mcp_server_revisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ServerId).HasColumnName("server_id");
        builder.Property(x => x.Revision).HasColumnName("revision");
        builder.Property(x => x.Source).HasColumnName("source").IsRequired();
        builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.ServerId, x.Revision }).IsUnique();
    }
}
