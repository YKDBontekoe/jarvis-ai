using System.Text.Json;
using System.Text.RegularExpressions;
using Jarvis.Application.Events;

namespace Jarvis.Agents;

/// <summary>
/// Finds the things a tool call made or changed in its result text, so the app can show them as cards that open the
/// thing itself. Tools write ids either as refs (<c>task:&lt;id&gt;</c>) or in prose ("reminder ID &lt;id&gt;",
/// "Condition watch created (id: &lt;id&gt;"); both are read. A result naming more than
/// <see cref="MaxRefs"/> things is a listing, not an action, and yields none.
/// </summary>
internal static partial class ToolResultRefs
{
    public const int MaxRefs = 3;

    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reminder"] = EntityTypes.Reminder,
        ["task"] = EntityTypes.Task,
        ["memory"] = EntityTypes.Memory,
        ["expense"] = EntityTypes.Expense,
        ["journal"] = EntityTypes.Journal,
        ["journal entry"] = EntityTypes.Journal,
        ["entry"] = EntityTypes.Journal,
        ["project"] = EntityTypes.Project,
        ["decision"] = EntityTypes.Decision,
        ["habit"] = EntityTypes.Habit,
        ["mission"] = EntityTypes.Mission,
        ["person"] = EntityTypes.Person,
        ["watch"] = EntityTypes.Watch,
        ["file"] = EntityTypes.File,
        ["commitment"] = EntityTypes.Commitment,
        ["automation"] = EntityTypes.Automation
    };

    [GeneratedRegex(@"\b([a-z_]+):([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitRef();

    [GeneratedRegex(@"\b(journal entry|reminder|task|memory|expense|journal|entry|project|decision|habit|mission|person|watch|file|commitment|automation)\b[^\n]{0,16}?\b([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProseRef();

    public static IReadOnlyList<string> Extract(object? result)
    {
        var text = result switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => result.ToString()
        };
        if (string.IsNullOrWhiteSpace(text)) return [];

        var found = new List<EntityRef>();
        foreach (Match match in ExplicitRef().Matches(text))
            if (EntityRef.TryParse(match.Value, out var entity) && !found.Contains(entity))
                found.Add(entity);
        foreach (Match match in ProseRef().Matches(text))
        {
            if (!Words.TryGetValue(match.Groups[1].Value, out var type) ||
                !Guid.TryParse(match.Groups[2].Value, out var id) || id == Guid.Empty)
                continue;
            var entity = new EntityRef(type, id);
            // The same id already found as an explicit ref of another type is not a second thing.
            if (found.Any(x => x.Id == id)) continue;
            found.Add(entity);
        }
        return found.Count is 0 or > MaxRefs ? [] : found.Select(x => x.ToString()).ToArray();
    }
}
