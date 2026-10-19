namespace Jarvis.Application.Missions;

/// <summary>One thing worth asking before planning: only what changes the plan.</summary>
public sealed record StarterQuestion(string Key, string Label, string Hint, bool Required);

public sealed record MissionTemplate(
    string Id,
    string Title,
    string Purpose,
    string Deliverable,
    IReadOnlyList<StarterQuestion> Questions);

public sealed record StarterComposed(string Title, string Goal);

public sealed record StarterResult(StarterComposed? Composed, string? Field = null, string? Message = null)
{
    public bool Succeeded => Composed is not null;
}

/// <summary>
/// Practical mission starters. They only turn a few answers into a clear goal; the existing planner then splits it
/// into steps the owner reviews before anything starts. No second mission engine.
/// </summary>
public static class MissionStarter
{
    public const int MaxAnswerLength = 500;

    private static StarterQuestion Question(string key, string label, string hint, bool required = false) =>
        new(key, label, hint, required);

    private static readonly StarterQuestion Sources = Question("sources", "Where may it look?",
        "For example: only my Library and the web. Leave empty for the web and your Library.");

    private static readonly StarterQuestion Constraints = Question("constraints", "Anything it must respect?",
        "Budget, deadline, things to avoid.");

    public static readonly IReadOnlyList<MissionTemplate> All =
    [
        new("decision", "Research a decision",
            "Weigh the options for a choice you have to make and recommend one, with the evidence.",
            "A short report that compares the options and names what is still uncertain.",
            [
                Question("topic", "What are you deciding?", "For example: which laptop to buy for travel.", true),
                Question("options", "Options you already have in mind", "Leave empty to let it find them."),
                Question("criteria", "What matters most?", "For example: weight, battery life, price."),
                Sources, Constraints
            ]),
        new("brief", "Prepare a project brief",
            "Pull what Jarvis knows about a project into a one-page brief you can share.",
            "A one-page brief: where things stand, what is next, what is at risk.",
            [
                Question("project", "Which project?", "Name it as you would to a colleague.", true),
                Question("audience", "Who will read it?", "For example: my client, the team, just me."),
                Sources, Constraints
            ]),
        new("compare", "Compare proposals",
            "Line up offers or quotes side by side and show the differences that matter.",
            "A comparison table with a recommendation and the questions to ask each supplier.",
            [
                Question("topic", "What are you comparing?", "For example: three quotes for the kitchen worktop.", true),
                Question("criteria", "What matters most?", "Price, delivery time, warranty…"),
                Sources, Constraints
            ]),
        new("meeting", "Assemble a meeting pack",
            "Gather everything you need for a meeting: background, open points and questions to ask.",
            "A pack with a short agenda, background on the people and topic, and open promises.",
            [
                Question("meeting", "Which meeting?", "Title, person or topic.", true),
                Question("audience", "Who is attending?", "Names help it find your history with them."),
                Sources, Constraints
            ])
    ];

    public static MissionTemplate? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

    public static StarterResult Compose(string? templateId, IReadOnlyDictionary<string, string?>? answers)
    {
        var template = Find(templateId);
        if (template is null) return new StarterResult(null, "template", "That is not a mission starter.");
        var clean = new Dictionary<string, string>();
        foreach (var question in template.Questions)
        {
            string? raw = null;
            answers?.TryGetValue(question.Key, out raw);
            var value = MissionRules.Limit(raw, MaxAnswerLength);
            if (value is null)
            {
                if (question.Required)
                    return new StarterResult(null, question.Key, $"{question.Label} is needed to plan this.");
                continue;
            }
            clean[question.Key] = value;
        }

        var subject = clean.GetValueOrDefault("topic") ?? clean.GetValueOrDefault("project") ??
                      clean.GetValueOrDefault("meeting") ?? template.Title;
        var lines = new List<string> { $"{template.Title}: {subject}", $"Deliverable: {template.Deliverable}" };
        if (clean.TryGetValue("options", out var options)) lines.Add($"Options to consider: {options}");
        if (clean.TryGetValue("criteria", out var criteria)) lines.Add($"What matters most: {criteria}");
        if (clean.TryGetValue("audience", out var audience)) lines.Add($"Audience: {audience}");
        lines.Add(clean.TryGetValue("sources", out var sources)
            ? $"Use only these sources: {sources}"
            : "Sources: the web and my Library; name every source you rely on.");
        if (clean.TryGetValue("constraints", out var constraints)) lines.Add($"Constraints: {constraints}");
        lines.Add("Do not send, post, buy, book or change anything outside Jarvis. Prepare drafts for me to review, " +
                  "and say what you could not confirm.");
        var goal = MissionRules.ClipText(string.Join('\n', lines), MissionRules.MaxGoalLength);
        var title = MissionRules.Limit($"{template.Title}: {subject}", MissionRules.MaxTitleLength)!;
        return new StarterResult(new StarterComposed(title, goal));
    }
}
