using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.People;

internal sealed class PersonChannelLinkConfiguration : IEntityTypeConfiguration<PersonChannelLinkEntity>
{
    public void Configure(EntityTypeBuilder<PersonChannelLinkEntity> builder)
    {
        builder.ToTable("person_channel_links", table =>
            table.HasCheckConstraint("ck_person_channel_links_tone",
                "tone_score IS NULL OR tone_score BETWEEN -2 AND 2"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.PersonId).HasColumnName("person_id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ToneScore).HasColumnName("tone_score");
        builder.Property(x => x.ToneReason).HasColumnName("tone_reason").HasMaxLength(300);
        builder.Property(x => x.ToneAt).HasColumnName("tone_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        // One chat belongs to one person; a person can have several chats.
        builder.HasIndex(x => new { x.OwnerId, x.ConnectionId, x.ChatId }).IsUnique()
            .HasDatabaseName("ux_person_channel_links_owner_chat");
        builder.HasIndex(x => new { x.OwnerId, x.PersonId });
        // Removing a person or an unlinked WhatsApp connection removes the link, never the messages.
        builder.HasOne<PersonEntity>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
