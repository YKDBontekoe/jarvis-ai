namespace Jarvis.Domain.Profiles;

/// <summary>Owner-defined assistant configuration captured and versioned per conversation or task.</summary>
public sealed class AssistantProfile
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxPersonaInstructionsLength = 4_000;
    public const int MaxPreferredNameLength = 60;
    public const int MaxReplyLanguageLength = 40;
    public const int MaxProfilesPerOwner = 25;
    public const string DefaultName = "Jarvis";
    public const string DefaultDescription =
        "Default assistant for this owner. Uses global persona, skills, files, models, and learning.";

    private AssistantProfile() { }

    public AssistantProfile(Guid ownerId, string name, bool isDefault)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Name = name;
        IsDefault = isDefault;
        IncludeOwnerPersona = true;
        EnabledSkillIds = [];
        AllowedCollectionIds = [];
        MemoryScope = MemoryScopes.All;
        IncludePinnedMemories = true;
        ContributeToLearning = true;
        AllowPersonaLearning = true;
        AllowRemember = true;
        Version = 1;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsDefault { get; private set; }
    public int Version { get; private set; }
    public string? PersonaInstructions { get; private set; }
    public string? PreferredName { get; private set; }
    public string? ReplyLanguage { get; private set; }
    public bool IncludeOwnerPersona { get; private set; }
    public bool RestrictSkills { get; private set; }
    public Guid[] EnabledSkillIds { get; private set; } = [];
    public bool RestrictFiles { get; private set; }
    public Guid[] AllowedCollectionIds { get; private set; } = [];
    public string? ModelClass { get; private set; }
    public string? ChatModel { get; private set; }
    public string? FastModel { get; private set; }
    public string? ReasoningEffort { get; private set; }
    public string MemoryScope { get; private set; } = MemoryScopes.All;
    public bool IncludePinnedMemories { get; private set; }
    public bool ContributeToLearning { get; private set; }
    public bool AllowPersonaLearning { get; private set; }
    public bool AllowRemember { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkDefault(bool isDefault)
    {
        if (IsDefault == isDefault) return;
        IsDefault = isDefault;
        Touch();
    }

    public void Apply(
        string name,
        string? description,
        string? personaInstructions,
        string? preferredName,
        string? replyLanguage,
        bool includeOwnerPersona,
        bool restrictSkills,
        Guid[] enabledSkillIds,
        bool restrictFiles,
        Guid[] allowedCollectionIds,
        string? modelClass,
        string? chatModel,
        string? fastModel,
        string? reasoningEffort,
        string memoryScope,
        bool includePinnedMemories,
        bool contributeToLearning,
        bool allowPersonaLearning,
        bool allowRemember,
        bool incrementVersion = true)
    {
        Name = name;
        Description = description;
        PersonaInstructions = personaInstructions;
        PreferredName = preferredName;
        ReplyLanguage = replyLanguage;
        IncludeOwnerPersona = includeOwnerPersona;
        RestrictSkills = restrictSkills;
        EnabledSkillIds = enabledSkillIds;
        RestrictFiles = restrictFiles;
        AllowedCollectionIds = allowedCollectionIds;
        ModelClass = modelClass;
        ChatModel = chatModel;
        FastModel = fastModel;
        ReasoningEffort = reasoningEffort;
        MemoryScope = memoryScope;
        IncludePinnedMemories = includePinnedMemories;
        ContributeToLearning = contributeToLearning;
        AllowPersonaLearning = allowPersonaLearning;
        AllowRemember = allowRemember;
        if (incrementVersion) Version++;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public static class MemoryScopes
{
    public const string All = "all";
    public const string Profile = "profile";
    public const string Pinned = "pinned";

    public static readonly IReadOnlySet<string> AllValues = new HashSet<string>(StringComparer.Ordinal)
        { All, Profile, Pinned };

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        value is not null && AllValues.Contains(value);
}

public static class ProfileModelClasses
{
    public const string Chat = "chat";
    public const string Fast = "fast";
    public const string Reasoning = "reasoning";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Chat, Fast, Reasoning };

    public static bool IsValid(string? value) =>
        string.IsNullOrEmpty(value) || All.Contains(value);
}
