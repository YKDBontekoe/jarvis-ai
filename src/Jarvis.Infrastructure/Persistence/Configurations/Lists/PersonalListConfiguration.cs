using Jarvis.Application.Lists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Lists;

internal sealed class PersonalListConfiguration : IEntityTypeConfiguration<PersonalListEntity>
{
    public void Configure(EntityTypeBuilder<PersonalListEntity> builder)
    {
        builder.ToTable("personal_lists", table =>
            table.HasCheckConstraint("ck_personal_lists_kind", "kind IN ('shopping', 'todo', 'general')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(ListRules.MaxNameLength).IsRequired();
        builder.Property(x => x.NameKey).HasColumnName("name_key").HasMaxLength(ListRules.MaxNameLength).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.NameKey }).IsUnique();
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ListItemConfiguration : IEntityTypeConfiguration<ListItemEntity>
{
    public void Configure(EntityTypeBuilder<ListItemEntity> builder)
    {
        builder.ToTable("personal_list_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ListId).HasColumnName("list_id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Text).HasColumnName("text").HasMaxLength(ListRules.MaxItemLength).IsRequired();
        builder.Property(x => x.IsDone).HasColumnName("is_done");
        builder.Property(x => x.DoneAt).HasColumnName("done_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.ListId });
    }
}
