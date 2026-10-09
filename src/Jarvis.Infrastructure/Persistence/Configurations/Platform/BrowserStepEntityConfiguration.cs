using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class BrowserStepEntityConfiguration : IEntityTypeConfiguration<BrowserStepEntity>
{
    public void Configure(EntityTypeBuilder<BrowserStepEntity> builder)
    {
            builder.ToTable("browser_steps");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.SessionId).HasColumnName("session_id");
            builder.Property(x => x.Ordinal).HasColumnName("ordinal");
            builder.Property(x => x.Tool).HasColumnName("tool").HasMaxLength(80).IsRequired();
            builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1_000).IsRequired();
            builder.Property(x => x.Success).HasColumnName("success");
            builder.Property(x => x.ScreenshotKey).HasColumnName("screenshot_key").HasMaxLength(200);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.HasIndex(x => new { x.SessionId, x.Ordinal }).IsUnique();
        }
}
