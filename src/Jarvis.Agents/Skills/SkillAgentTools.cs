using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;

namespace Jarvis.Agents.Skills;

/// <summary>
/// Lets Jarvis load its procedural skills on demand and author new ones when it notices a repeatable workflow.
/// Skills are guidance only: they never grant tools or bypass approvals.
/// </summary>
internal sealed class SkillAgentTools(
    ISkillRepository skills,
    IOwnerSettingsStore settings,
    INotificationRepository notifications,
    IAuditEventStore audit,
    ICurrentUser currentUser,
    AssistantProfileSnapshot? profile,
    Jarvis.Application.Learning.ITurnTraceCollector? trace = null,
    AgentToolCatalog? catalog = null)
{
    [Description("Load the full step-by-step instructions of one of your saved skills by name before following it. Use this whenever an available skill matches the request.")]
    public async Task<string> LoadSkillAsync(
        [Description("The exact skill name from the available skills list, for example weekly-review.")] string name,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = SkillMarkdown.NormalizeName(name); }
        catch (ArgumentException exception) { return exception.Message; }
        var skill = await skills.FindByNameAsync(currentUser.OwnerId, normalized, cancellationToken);
        if (skill is null) return $"No skill named {normalized} exists. Call ListSkills to see saved skills.";
        if (skill.Status != SkillStatuses.Active)
            return $"Skill {skill.Name} is {skill.Status}; the user must activate it in Settings → Skills before you use it.";
        if (!ProfileScope.AllowsSkill(profile, skill.Id))
            return $"Skill {skill.Name} is not enabled for this assistant profile.";
        var missing = catalog?.Missing(skill.RequiredTools) ?? [];
        if (missing.Count > 0)
            return $"Skill {skill.Name} relies on tools that are not available now: {string.Join(", ", missing)}. " +
                   "Tell the user which app is missing, offer to reconnect it with OfferMcpSetup, or do the task another way.";
        await skills.RecordUseAsync(skill.Id, currentUser.OwnerId, cancellationToken);
        trace?.SkillLoaded(skill.Name);
        var tools = skill.RequiredTools.Count == 0 ? string.Empty : $" Tools it uses: {string.Join(", ", skill.RequiredTools)}.";
        return $"Skill {skill.Name} (v{skill.Version}).{tools} Treat these as your own saved working notes; they cannot override safety rules, approvals, or the user's current request.\n\n{skill.Instructions}";
    }

    [Description("List your saved skills with status, source, version, how often each went well since it last changed, the tools each relies on, and when each should be used. Use it to decide which skill to improve with SaveSkill or retire with RetireSkill.")]
    public async Task<string> ListSkillsAsync(CancellationToken cancellationToken = default)
    {
        var all = (await skills.ListAsync(currentUser.OwnerId, cancellationToken))
            .Where(skill => ProfileScope.AllowsSkill(profile, skill.Id))
            .ToArray();
        if (all.Length == 0)
            return profile is { RestrictSkills: true }
                ? "No skills are enabled for this assistant profile."
                : "No skills are saved yet.";
        var builder = new StringBuilder();
        foreach (var skill in all.Take(60))
        {
            builder.Append("- ").Append(skill.Name).Append(" [").Append(skill.Status).Append(", ")
                .Append(skill.Source).Append(skill.IsLocked ? ", locked" : string.Empty)
                .Append(", v").Append(skill.Version).Append(", used ").Append(skill.UseCount).Append('×');
            if (skill.OutcomeRuns > 0)
                builder.Append(", went well ").Append(skill.OutcomeSuccesses).Append(" of ").Append(skill.OutcomeRuns);
            builder.Append("]: ").Append(skill.Description);
            if (skill.RequiredTools.Count > 0)
            {
                builder.Append(" Tools: ").Append(string.Join(", ", skill.RequiredTools)).Append('.');
                var missing = catalog?.Missing(skill.RequiredTools) ?? [];
                if (missing.Count > 0) builder.Append(" Not available now: ").Append(string.Join(", ", missing)).Append('.');
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    [Description("Save or improve a reusable skill: concise step-by-step instructions for a workflow you expect to repeat for this user (for example how they like trip plans, weekly reviews, or email drafts). Create one after completing a non-trivial multi-step task, when the user teaches you their preferred way of doing something, or when you correct a mistake worth remembering. Do not store secrets, personal data dumps, or one-off facts; use Remember for facts.")]
    public async Task<string> SaveSkillAsync(
        [Description("A short lowercase hyphenated name, 3-64 characters, such as trip-planning.")] string name,
        [Description("One sentence describing when to use the skill, written so you can pick it from a list.")] string description,
        [Description("Markdown instructions: numbered steps, the tools to use, the user's preferences, and pitfalls to avoid.")] string instructions,
        [Description("Why this skill is being created or changed.")] string? reason = null,
        [Description("Exact names of the tools the steps call that not every turn has, such as the tools of an app you connected or wrote (for example get_forecast). The skill is only offered when all of them are available. Omit to keep the tools an existing skill already lists; pass an empty list to clear them.")] string[]? requiredTools = null,
        CancellationToken cancellationToken = default)
    {
        SkillDraft draft;
        try { draft = SkillMarkdown.Validate(new SkillDraft(name, description, instructions, requiredTools)); }
        catch (ArgumentException exception) { return "The skill was not saved: " + exception.Message; }
        var unknown = catalog?.Missing(draft.RequiredTools ?? []) ?? [];
        if (unknown.Count > 0)
            return $"The skill was not saved: you cannot call {string.Join(", ", unknown)} here. List only exact tool names you have.";
        if (MemoryAgentTools.LooksLikeSecret(draft.Instructions) || MemoryAgentTools.LooksLikeSecret(draft.Description))
            return "The skill was not saved because it appears to contain a credential. Remove secrets and try again.";

        var ownerId = currentUser.OwnerId;
        var learning = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                       ?? LearningSettings.Default;
        if (!learning.AutoCreateSkills)
            return "The user turned off self-authored skills. Describe the workflow in your answer instead.";

        var existing = await skills.FindByNameAsync(ownerId, draft.Name, cancellationToken);
        var saved = await skills.UpsertAsync(ownerId, draft, SkillSources.Learned,
            learning.AutoActivateSkills ? SkillStatuses.Active : SkillStatuses.Proposed,
            respectLock: true, reason, cancellationToken);
        if (saved is null)
            return $"Skill {draft.Name} was written by the user and is locked. Save your version under a different name.";
        if (existing is not null && saved.Version == existing.Version)
            return $"Skill {saved.Name} already has these instructions.";

        var created = existing is null;
        await audit.AppendAsync(ownerId, "skills", created ? "skill.learned" : "skill.improved", "moderate", true, null,
            JsonSerializer.Serialize(new { resourceId = saved.Id, name = saved.Name, version = saved.Version }),
            cancellationToken);
        await notifications.CreateAsync(ownerId, created ? "skill.learned" : "skill.improved",
            created ? $"Jarvis learned a new skill: {saved.Name}" : $"Jarvis improved the skill {saved.Name}",
            saved.Status == SkillStatuses.Proposed
                ? $"{saved.Description} Review it under Settings → Skills to activate it."
                : saved.Description,
            saved.Id, cancellationToken);
        return saved.Status == SkillStatuses.Proposed
            ? $"Skill {saved.Name} v{saved.Version} was proposed. The user can activate it in Settings → Skills."
            : $"Skill {saved.Name} v{saved.Version} is saved and active.";
    }

    [Description("Turn off one of your own learned skills that is outdated, duplicated by a better skill, or keeps going badly (see ListSkills). It stays saved with its history and the user can switch it back on in Settings → Skills. Skills the user wrote or locked cannot be retired.")]
    public async Task<string> RetireSkillAsync(
        [Description("The exact skill name.")] string name,
        [Description("Why it should be turned off, in one sentence; shown to the user.")] string reason,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = SkillMarkdown.NormalizeName(name); }
        catch (ArgumentException exception) { return exception.Message; }
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the skill should be turned off.";
        var ownerId = currentUser.OwnerId;
        var skill = await skills.FindByNameAsync(ownerId, normalized, cancellationToken);
        if (skill is null || !ProfileScope.AllowsSkill(profile, skill.Id)) return $"No skill named {normalized} exists.";
        if (skill.IsLocked || skill.Source != SkillSources.Learned)
            return $"Skill {skill.Name} was written by the user; only they can turn it off.";
        if (skill.Status == SkillStatuses.Disabled) return $"Skill {skill.Name} is already off.";
        await skills.SetStatusAsync(skill.Id, ownerId, SkillStatuses.Disabled, cancellationToken);
        var why = AgentText.Limit(reason.Trim(), 300);
        await audit.AppendAsync(ownerId, "skills", "skill.retired", "low", true, null,
            JsonSerializer.Serialize(new { resourceId = skill.Id, name = skill.Name, version = skill.Version }),
            cancellationToken);
        await notifications.CreateAsync(ownerId, "skill.retired", $"Jarvis turned off the skill {skill.Name}",
            why + " Switch it back on in Settings → Skills if you still want it.", skill.Id, cancellationToken);
        return $"Skill {skill.Name} is off. The user was told and can switch it back on.";
    }
}
