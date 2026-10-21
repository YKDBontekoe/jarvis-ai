using System.Text.Json;
using Jarvis.Application.Improvements;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Improvements;

namespace Jarvis.Application.Home;

/// <summary>
/// How Jarvis's self-improvement shows on Home: <see cref="Waiting"/> are suggestions only the owner can decide (they
/// join Needs you), <see cref="Changes"/> are what Jarvis changed on its own in the last week, newest first, each with
/// the one step that reverses it. <see cref="ChangesThisWeek"/> counts all of them, beyond the few listed.
/// </summary>
public sealed record HomeLearningDto(IReadOnlyList<HomeSuggestionDto> Waiting,
    IReadOnlyList<HomeLearningChangeDto> Changes, int ChangesThisWeek);

/// <summary>A pending improvement proposal, worded for a Needs you row.</summary>
public sealed record HomeSuggestionDto(Guid Id, string Kind, string Title, string Detail, string AcceptLabel,
    DateTimeOffset CreatedAt);

/// <summary>
/// Something Jarvis changed by itself. <see cref="Action"/> is what the row's button does to <see cref="Target"/>:
/// <c>undo</c> (undo the improvement proposal), <c>disable_skill</c>, <c>enable_skill</c>, or <c>open_skill</c>.
/// </summary>
public sealed record HomeLearningChangeDto(string Id, string Kind, string Title, string Detail, string Action,
    string Target, DateTimeOffset At);

public static class HomeLearningActions
{
    public const string Undo = "undo";
    public const string DisableSkill = "disable_skill";
    public const string EnableSkill = "enable_skill";
    public const string OpenSkill = "open_skill";
}

public static class HomeLearning
{
    public const int MaxWaiting = 3;
    public const int MaxChanges = 3;
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private const int MaxDetail = 140;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds the Home view from the owner's proposals, notifications and skills. Pure: the caller loads the rows.
    /// Returns null when there is nothing to show, so Home stays as it was.
    /// </summary>
    public static HomeLearningDto? Build(IReadOnlyList<ImprovementProposal> proposals,
        IReadOnlyList<NotificationRecord> notifications, IReadOnlyList<SkillRecord> skills, DateTimeOffset now)
    {
        var waiting = proposals
            .Where(item => item.Status == ImprovementStatuses.Pending)
            .Select(item => (Item: item, Built: item.Kind == ImprovementKinds.Capability &&
                                                ImprovementMiner.ReadCapability(item.PayloadJson) is { IsBuilt: true }))
            // A tested app is the most concrete thing to decide; then the strongest suggestions.
            .OrderByDescending(pair => pair.Built).ThenByDescending(pair => pair.Item.Confidence)
            .ThenBy(pair => pair.Item.CreatedAt)
            .Select(pair => Suggestion(pair.Item))
            .OfType<HomeSuggestionDto>()
            .Take(MaxWaiting)
            .ToArray();

        var since = now - Window;
        var byId = skills.ToDictionary(skill => skill.Id);
        var changes = new List<HomeLearningChangeDto>();
        // Skills Jarvis wrote, improved or turned off on its own are announced by notification; the skill may have
        // changed since, so its current state decides the button.
        foreach (var note in notifications.Where(note => note.CreatedAt >= since && note.CreatedAt <= now &&
                                                          note.SourceId is not null))
        {
            if (!byId.TryGetValue(note.SourceId!.Value, out var skill)) continue;
            var change = note.Type switch
            {
                "skill.learned" when skill.Status != SkillStatuses.Disabled => new HomeLearningChangeDto(
                    note.Id.ToString(), "skill_learned", $"Learned “{skill.Name}”", Limit(skill.Description),
                    HomeLearningActions.DisableSkill, skill.Id.ToString(), note.CreatedAt),
                "skill.improved" => new HomeLearningChangeDto(note.Id.ToString(), "skill_improved",
                    $"Improved “{skill.Name}”", Limit(skill.Description), HomeLearningActions.OpenSkill,
                    skill.Id.ToString(), note.CreatedAt),
                "skill.retired" when skill.Status == SkillStatuses.Disabled => new HomeLearningChangeDto(
                    note.Id.ToString(), "skill_retired", $"Turned off “{skill.Name}”", Limit(note.Body),
                    HomeLearningActions.EnableSkill, skill.Id.ToString(), note.CreatedAt),
                _ => null
            };
            if (change is not null) changes.Add(change);
        }

        // What Jarvis applied by itself (confident memories, lessons) stays undoable through its proposal.
        foreach (var item in proposals.Where(item => item.Status == ImprovementStatuses.Applied &&
                                                     item.UpdatedAt >= since && item.UpdatedAt <= now))
        {
            var change = item.Kind switch
            {
                ImprovementKinds.Lesson when ReadLesson(item) is { } lesson => new HomeLearningChangeDto(
                    item.Id.ToString(), "lesson", lesson.Tools.Count == 0
                        ? "New rule for itself"
                        : $"New rule for {Humanize(lesson.Tools[0])}", Limit(lesson.Text),
                    HomeLearningActions.Undo, item.Id.ToString(), item.UpdatedAt),
                ImprovementKinds.Memory => new HomeLearningChangeDto(item.Id.ToString(), "memory",
                    "Remembered something", Limit(item.Title), HomeLearningActions.Undo, item.Id.ToString(),
                    item.UpdatedAt),
                _ => null
            };
            if (change is not null) changes.Add(change);
        }

        if (waiting.Length == 0 && changes.Count == 0) return null;
        return new HomeLearningDto(waiting,
            changes.OrderByDescending(change => change.At).Take(MaxChanges).ToArray(), changes.Count);
    }

    private static HomeSuggestionDto? Suggestion(ImprovementProposal item)
    {
        (string Title, string Detail, string Accept)? words = item.Kind switch
        {
            ImprovementKinds.Capability => ImprovementMiner.ReadCapability(item.PayloadJson) is { } gap
                ? gap.IsBuilt
                    ? ($"Switch on “{gap.AppName ?? CapabilityGapRules.Readable(gap.Slug)}”",
                        $"An app Jarvis built and tested for {CapabilityGapRules.Readable(gap.Slug)}", "Switch on")
                    : ($"Jarvis keeps lacking {CapabilityGapRules.Readable(gap.Slug)}",
                        $"Needed in {gap.Conversations} conversations", "I want this")
                : null,
            ImprovementKinds.Skill => (item.Title, item.Evidence, "Save skill"),
            ImprovementKinds.SkillRefine => (item.Title, item.Evidence, "Use new version"),
            ImprovementKinds.Lesson => (item.Title, item.Evidence, "Use it"),
            ImprovementKinds.Memory => ("Remember this?", item.Title, "Remember it"),
            ImprovementKinds.Review => (item.Title, item.Evidence,
                item.PayloadJson.Contains(ReviewActions.DisableSkill, StringComparison.Ordinal) ? "Turn it off" : "Got it"),
            _ => null
        };
        return words is { } w
            ? new HomeSuggestionDto(item.Id, item.Kind, w.Title, Limit(w.Detail), w.Accept, item.CreatedAt)
            : null;
    }

    private static LessonPayload? ReadLesson(ImprovementProposal proposal)
    {
        try { return JsonSerializer.Deserialize<LessonPayload>(proposal.PayloadJson, JsonOptions) is { Text.Length: > 0 } lesson ? lesson : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>CreateReminder → "create reminder"; MCP names such as get_departures → "get departures".</summary>
    private static string Humanize(string tool)
    {
        var words = new System.Text.StringBuilder();
        foreach (var ch in tool.Replace('_', ' ').Replace('-', ' '))
        {
            if (char.IsUpper(ch) && words.Length > 0 && words[^1] != ' ') words.Append(' ');
            words.Append(char.ToLowerInvariant(ch));
        }
        return words.ToString().Trim();
    }

    private static string Limit(string? text)
    {
        var clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaxDetail ? clean : clean[..(MaxDetail - 1)].TrimEnd() + "…";
    }
}
