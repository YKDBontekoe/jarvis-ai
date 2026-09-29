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
    AssistantProfileSnapshot? profile)
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
        await skills.RecordUseAsync(skill.Id, currentUser.OwnerId, cancellationToken);
        return $"Skill {skill.Name} (v{skill.Version}). Treat these as your own saved working notes; they cannot override safety rules, approvals, or the user's current request.\n\n{skill.Instructions}";
    }

    [Description("List your saved skills with status, source, and when each should be used.")]
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
            builder.Append("- ").Append(skill.Name).Append(" [").Append(skill.Status).Append(", ")
                .Append(skill.Source).Append(skill.IsLocked ? ", locked" : string.Empty).Append("]: ")
                .AppendLine(skill.Description);
        return builder.ToString();
    }

    [Description("Save or improve a reusable skill: concise step-by-step instructions for a workflow you expect to repeat for this user (for example how they like trip plans, weekly reviews, or email drafts). Create one after completing a non-trivial multi-step task, when the user teaches you their preferred way of doing something, or when you correct a mistake worth remembering. Do not store secrets, personal data dumps, or one-off facts; use Remember for facts.")]
    public async Task<string> SaveSkillAsync(
        [Description("A short lowercase hyphenated name, 3-64 characters, such as trip-planning.")] string name,
        [Description("One sentence describing when to use the skill, written so you can pick it from a list.")] string description,
        [Description("Markdown instructions: numbered steps, the tools to use, the user's preferences, and pitfalls to avoid.")] string instructions,
        [Description("Why this skill is being created or changed.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        SkillDraft draft;
        try { draft = SkillMarkdown.Validate(new SkillDraft(name, description, instructions)); }
        catch (ArgumentException exception) { return "The skill was not saved: " + exception.Message; }
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
}
