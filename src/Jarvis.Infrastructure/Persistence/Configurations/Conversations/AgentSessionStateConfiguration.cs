using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Conversations;

internal sealed class AgentSessionStateConfiguration : IEntityTypeConfiguration<AgentSessionState>
{
    public void Configure(EntityTypeBuilder<AgentSessionState> builder)
    {
            builder.ToTable("agent_sessions");
            builder.HasKey(x => x.ConversationId);
            builder.Property(x => x.State).HasColumnType("jsonb").IsRequired();
            builder.HasOne<Conversation>().WithOne()
                .HasForeignKey<AgentSessionState>(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}
