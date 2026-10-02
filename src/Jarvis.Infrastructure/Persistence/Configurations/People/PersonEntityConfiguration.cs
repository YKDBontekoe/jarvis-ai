using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.People;

internal sealed class PersonEntityConfiguration : IEntityTypeConfiguration<PersonEntity>
{
    public void Configure(EntityTypeBuilder<PersonEntity> builder)
    {
        builder.ToTable("people", table =>
        {
            table.HasCheckConstraint("ck_people_birthday_month", "birthday_month IS NULL OR birthday_month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_people_birthday_day", "birthday_day IS NULL OR birthday_day BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_people_contact_every_days",
                "contact_every_days IS NULL OR contact_every_days BETWEEN 1 AND 365");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.NameKey).HasColumnName("name_key").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Relationship).HasColumnName("relationship").HasMaxLength(40);
        builder.Property(x => x.BirthdayMonth).HasColumnName("birthday_month");
        builder.Property(x => x.BirthdayDay).HasColumnName("birthday_day");
        builder.Property(x => x.BirthYear).HasColumnName("birth_year");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.ContactEveryDays).HasColumnName("contact_every_days");
        builder.Property(x => x.LastContactedAt).HasColumnName("last_contacted_at");
        builder.Property(x => x.GraphEntityId).HasColumnName("graph_entity_id");
        builder.Property(x => x.BirthdayNotifiedYear).HasColumnName("birthday_notified_year");
        builder.Property(x => x.CheckInNudgedAt).HasColumnName("check_in_nudged_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.NameKey }).IsUnique();
        builder.HasIndex(x => x.GraphEntityId);
        // Forgetting or merging a graph entity unlinks the person instead of deleting them.
        builder.HasOne<GraphEntityEntity>().WithMany().HasForeignKey(x => x.GraphEntityId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
