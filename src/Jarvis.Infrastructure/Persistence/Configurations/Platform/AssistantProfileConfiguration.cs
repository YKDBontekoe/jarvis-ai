using System.Text.Json;
using Jarvis.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class AssistantProfileConfiguration : IEntityTypeConfiguration<AssistantProfile>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AssistantProfile> builder)
    {
        builder.ToTable("assistant_profiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(AssistantProfile.MaxNameLength).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(AssistantProfile.MaxDescriptionLength);
        builder.Property(x => x.IsDefault).HasColumnName("is_default");
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.PersonaInstructions).HasColumnName("persona_instructions");
        builder.Property(x => x.PreferredName).HasColumnName("preferred_name").HasMaxLength(AssistantProfile.MaxPreferredNameLength);
        builder.Property(x => x.ReplyLanguage).HasColumnName("reply_language").HasMaxLength(AssistantProfile.MaxReplyLanguageLength);
        builder.Property(x => x.IncludeOwnerPersona).HasColumnName("include_owner_persona");
        builder.Property(x => x.RestrictSkills).HasColumnName("restrict_skills");
        builder.Property(x => x.EnabledSkillIds).HasColumnName("enabled_skill_ids").HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, Json),
                value => JsonSerializer.Deserialize<Guid[]>(value, Json) ?? Array.Empty<Guid>())
            .Metadata.SetValueComparer(GuidArrayComparer());
        builder.Property(x => x.RestrictFiles).HasColumnName("restrict_files");
        builder.Property(x => x.AllowedCollectionIds).HasColumnName("allowed_collection_ids").HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, Json),
                value => JsonSerializer.Deserialize<Guid[]>(value, Json) ?? Array.Empty<Guid>())
            .Metadata.SetValueComparer(GuidArrayComparer());
        builder.Property(x => x.ModelClass).HasColumnName("model_class").HasMaxLength(20);
        builder.Property(x => x.ChatModel).HasColumnName("chat_model").HasMaxLength(200);
        builder.Property(x => x.FastModel).HasColumnName("fast_model").HasMaxLength(200);
        builder.Property(x => x.ReasoningEffort).HasColumnName("reasoning_effort").HasMaxLength(20);
        builder.Property(x => x.MemoryScope).HasColumnName("memory_scope").HasMaxLength(20).IsRequired();
        builder.Property(x => x.IncludePinnedMemories).HasColumnName("include_pinned_memories");
        builder.Property(x => x.ContributeToLearning).HasColumnName("contribute_to_learning");
        builder.Property(x => x.AllowPersonaLearning).HasColumnName("allow_persona_learning");
        builder.Property(x => x.AllowRemember).HasColumnName("allow_remember");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        builder.HasIndex(x => x.OwnerId).IsUnique().HasFilter("is_default = TRUE")
            .HasDatabaseName("ux_assistant_profiles_default");
        builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
    }

    private static ValueComparer<Guid[]> GuidArrayComparer() =>
        new((left, right) => (left ?? Array.Empty<Guid>()).SequenceEqual(right ?? Array.Empty<Guid>()),
            value => value.Aggregate(0, HashCode.Combine),
            value => value.ToArray());
}
