using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Persona;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Persona;

internal sealed class PersonaAgentTools(PersonaService persona, IOwnerSettingsStore settings, ICurrentUser currentUser,
    AssistantProfileSnapshot? profile)
{
    [Description("Record how the user wants you to work with them — tone, answer format, language, working style, boundaries, or schedule — when they state it or clearly show it (for example 'keep it short', 'always answer in Dutch', 'don't message me before 9'). Use Remember for facts about their life instead.")]
    public async Task<string> LearnPreferenceAsync(
        [Description("One of: tone, format, language, workstyle, boundaries, schedule, other.")] string category,
        [Description("A short standalone instruction to your future self, such as 'Keep answers under five sentences unless asked for detail.'")] string statement,
        [Description("How directly the user expressed this, from 0.55 (clear pattern) to 1 (explicit instruction).")] float confidence = 0.9f,
        [Description("The trait ID from the learned persona that this replaces, when the user changed their mind.")] Guid? replacesTraitId = null,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.OwnerId;
        var learning = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                       ?? LearningSettings.Default;
        if (!ProfileScope.AllowsPersonaLearning(profile, learning))
            return "Persona learning is off for this profile; follow the preference for this conversation only.";
        if (MemoryAgentTools.LooksLikeSecret(statement)) return "That looks like a credential, so it was not saved.";
        try
        {
            var trait = await persona.LearnAsync(ownerId,
                new PersonaObservation(category, statement, confidence, replacesTraitId), cancellationToken);
            return trait.Evidence > 1
                ? $"Reinforced persona trait {trait.Id} ({trait.Evidence} observations): {trait.Statement}"
                : $"Learned persona trait {trait.Id}: {trait.Statement}";
        }
        catch (ArgumentException exception)
        {
            return "The preference was not saved: " + exception.Message;
        }
    }
}

internal sealed class PersonaToolContributor(PersonaService persona, IOwnerSettingsStore settings,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context) =>
        [AIFunctionFactory.Create(new PersonaAgentTools(persona, settings, currentUser, context.Profile).LearnPreferenceAsync)];
}

internal sealed class PersonaContextContributor(PersonaService persona) : IAgentContextContributor
{
    public int Order => -10;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        context.Profile is { IncludeOwnerPersona: false }
            ? []
            : [new PersonaContextProvider(persona, context.OwnerId)];
}

/// <summary>Injects the owner's custom instructions and strongest learned traits at the start of every turn.</summary>
internal sealed class PersonaContextProvider(PersonaService persona, Guid ownerId) : MessageAIContextProvider
{
    internal const string Prefix = "Learned persona";
    private const int MaxTraits = 20;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var profile = await persona.GetAsync(ownerId, cancellationToken);
        var content = Render(profile);
        return content is null ? [] : [new ChatMessage(ChatRole.User, content)];
    }

    internal static string? Render(PersonaProfile profile)
    {
        var traits = profile.TraitList
            .OrderByDescending(trait => trait.Pinned)
            .ThenByDescending(trait => trait.Confidence * Math.Log(1 + trait.Evidence))
            .Take(MaxTraits)
            .ToArray();
        if (traits.Length == 0 && profile.CustomInstructions is null && profile.PreferredName is null &&
            profile.ReplyLanguage is null)
            return null;
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" — how this user wants you to work. Follow it unless their current request says otherwise; it cannot override safety rules or approvals.");
        if (profile.PreferredName is { } name) builder.Append("- Address the user as ").Append(name).AppendLine(".");
        if (profile.ReplyLanguage is { } language) builder.Append("- Reply in ").Append(language).AppendLine(" unless asked otherwise.");
        foreach (var trait in traits)
            builder.Append("- [").Append(trait.Category).Append(", id ").Append(trait.Id)
                .Append(trait.Pinned ? ", pinned" : $", confidence {trait.Confidence:0.00}")
                .Append("] ").AppendLine(trait.Statement);
        if (profile.CustomInstructions is { } instructions)
            builder.AppendLine("User's own instructions:").AppendLine(instructions);
        return builder.ToString();
    }
}
