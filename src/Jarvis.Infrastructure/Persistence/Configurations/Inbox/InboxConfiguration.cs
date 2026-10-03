using Jarvis.Application.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Inbox;

internal sealed class InboxThreadConfiguration : IEntityTypeConfiguration<InboxThreadEntity>
{
    public void Configure(EntityTypeBuilder<InboxThreadEntity> builder)
    {
        var states = string.Join(", ", InboxStates.All.Select(x => $"'{x}'"));
        builder.ToTable("inbox_threads", table =>
        {
            table.HasCheckConstraint("ck_inbox_threads_state", $"state IN ({states})");
            table.HasCheckConstraint("ck_inbox_threads_source", "source IN ('whatsapp', 'mail', 'manual')");
            table.HasCheckConstraint("ck_inbox_threads_priority", "priority BETWEEN 0 AND 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExternalKey).HasColumnName("external_key").HasMaxLength(InboxRules.MaxKeyLength + 40)
            .IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").HasMaxLength(200);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(InboxRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.Counterparty).HasColumnName("counterparty").HasMaxLength(InboxRules.MaxCounterpartyLength);
        builder.Property(x => x.State).HasColumnName("state").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(InboxRules.MaxSummaryLength);
        builder.Property(x => x.SuggestedReply).HasColumnName("suggested_reply").HasMaxLength(InboxRules.MaxReplyLength);
        builder.Property(x => x.LastMessagePreview).HasColumnName("last_message_preview")
            .HasMaxLength(InboxRules.MaxPreviewLength);
        builder.Property(x => x.LastFromMe).HasColumnName("last_from_me");
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.Property(x => x.SnoozedUntil).HasColumnName("snoozed_until");
        builder.Property(x => x.TriagedAt).HasColumnName("triaged_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Source, x.ExternalKey }).IsUnique()
            .HasDatabaseName("ux_inbox_threads_owner_source_key");
        builder.HasIndex(x => new { x.OwnerId, x.State });
    }
}

internal sealed class CommitmentConfiguration : IEntityTypeConfiguration<CommitmentEntity>
{
    public void Configure(EntityTypeBuilder<CommitmentEntity> builder)
    {
        builder.ToTable("commitments", table =>
        {
            table.HasCheckConstraint("ck_commitments_direction", "direction IN ('i_owe', 'owed_to_me')");
            table.HasCheckConstraint("ck_commitments_status", "status IN ('open', 'done', 'dropped')");
            table.HasCheckConstraint("ck_commitments_source",
                "source IN ('chat', 'whatsapp', 'mail', 'manual', 'meeting')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(12).IsRequired();
        builder.Property(x => x.Counterparty).HasColumnName("counterparty")
            .HasMaxLength(InboxRules.MaxCounterpartyLength).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description")
            .HasMaxLength(InboxRules.MaxDescriptionLength).IsRequired();
        builder.Property(x => x.DueOn).HasColumnName("due_on");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(12).IsRequired();
        builder.Property(x => x.Suggested).HasColumnName("suggested");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(12).IsRequired();
        builder.Property(x => x.InboxThreadId).HasColumnName("inbox_thread_id");
        builder.Property(x => x.ReminderId).HasColumnName("reminder_id");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Status });
        builder.HasIndex(x => new { x.OwnerId, x.DueOn });
    }
}
