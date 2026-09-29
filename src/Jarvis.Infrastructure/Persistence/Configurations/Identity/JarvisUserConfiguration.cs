using Jarvis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Identity;

internal sealed class JarvisUserConfiguration : IEntityTypeConfiguration<JarvisUser>
{
    public void Configure(EntityTypeBuilder<JarvisUser> builder)
    {
            builder.ToTable("users");
            builder.Property(user => user.Id).ValueGeneratedNever();
        }
}
