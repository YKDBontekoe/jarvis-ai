using System.Text.Json;

namespace Jarvis.Application.Surfaces;

/// <summary>
/// The building blocks Jarvis can compose into a custom card. The app draws each one natively with its own
/// tokens, so a card can be shaped freely without running code or loading anything remote.
/// </summary>
public static class UiSurfaceBlocks
{
    public const int MaxBlocks = 24;
    public const int MaxDepth = 2;
    public const int MaxChildren = 3;
    public const int MaxEntries = 8;

    public static readonly HashSet<string> Types =
    [
        "heading", "text", "stats", "progress", "bars", "steps", "pairs", "chips", "callout", "divider", "columns"
    ];

    /// <summary>Icon names the app knows; anything else is dropped.</summary>
    public static readonly HashSet<string> Icons =
    [
        "alarm", "bell", "book", "brain", "calendar", "car", "chart", "check", "clock", "fire", "food", "gift",
        "globe", "heart", "home", "idea", "info", "key", "lightning", "list", "location", "lock", "money", "phone",
        "plane", "receipt", "rocket", "shopping", "sparkle", "star", "target", "timer", "trophy", "trend-up",
        "trend-down", "user", "users", "warning", "work"
    ];

    private static readonly HashSet<string> Tones = ["neutral", "info", "success", "warning", "danger"];
    private static readonly HashSet<string> Trends = ["up", "down", "flat"];

    public static object[] Read(JsonElement? blocks)
    {
        if (blocks is not { ValueKind: JsonValueKind.Array } array) return [];
        var budget = MaxBlocks;
        var result = ReadList(array, 1, ref budget);
        if (budget < 0) throw new ArgumentException($"Use at most {MaxBlocks} blocks, counting the ones in columns.");
        return result;
    }

    public static string? Icon(JsonElement element, string name = "icon")
    {
        var icon = ReadString(element, name, 20)?.ToLowerInvariant();
        return icon is not null && Icons.Contains(icon) ? icon : null;
    }

    private static object[] ReadList(JsonElement array, int depth, ref int budget)
    {
        var result = new List<object>();
        foreach (var block in array.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (--budget < 0) return [.. result];
            var type = (ReadString(block, "type", 20) ?? string.Empty).ToLowerInvariant();
            if (!Types.Contains(type))
                throw new ArgumentException(
                    $"Unknown block type '{type}'. Use {string.Join(", ", Types.Order())}.");
            result.Add(type switch
            {
                "heading" => new { type, text = Required(block, "text", 80, type) },
                "text" => new { type, text = Required(block, "text", 600, type), tone = Tone(block) },
                "stats" => new { type, items = Entries(block, type, ReadStat) },
                "progress" => new
                {
                    type,
                    label = Required(block, "label", 80, type),
                    value = Math.Clamp(ReadNumber(block, "value") ?? 0, 0, 1),
                    detail = ReadString(block, "detail", 80),
                    tone = Tone(block)
                },
                "bars" => new { type, unit = ReadString(block, "unit", 12), items = Entries(block, type, ReadBar) },
                "steps" => new { type, items = Entries(block, type, ReadStep) },
                "pairs" => new { type, items = Entries(block, type, ReadPair) },
                "chips" => new { type, labels = Labels(block) },
                "callout" => new
                {
                    type,
                    text = Required(block, "text", 300, type),
                    tone = Tone(block) ?? "info",
                    icon = Icon(block)
                },
                "divider" => new { type },
                "columns" => Columns(block, depth, ref budget),
                _ => throw new ArgumentException($"Unknown block type '{type}'.")
            });
        }
        return [.. result];
    }

    private static object Columns(JsonElement block, int depth, ref int budget)
    {
        if (depth >= MaxDepth) throw new ArgumentException("Columns cannot hold other columns.");
        if (!block.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array ||
            children.GetArrayLength() is 0 or > MaxChildren)
            throw new ArgumentException($"Columns need 1 to {MaxChildren} children.");
        return new { type = "columns", children = ReadList(children, depth + 1, ref budget) };
    }

    private static object[] Entries(JsonElement block, string type, Func<JsonElement, object> read)
    {
        if (!block.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
            throw new ArgumentException($"A {type} block needs items.");
        return items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Take(MaxEntries)
            .Select(read)
            .ToArray();
    }

    private static object ReadStat(JsonElement item)
    {
        var trend = ReadString(item, "trend", 8)?.ToLowerInvariant();
        return new
        {
            label = Required(item, "label", 40, "stat"),
            value = Required(item, "value", 24, "stat"),
            delta = ReadString(item, "delta", 24),
            trend = trend is not null && Trends.Contains(trend) ? trend : null
        };
    }

    private static object ReadBar(JsonElement item) => new
    {
        label = Required(item, "label", 40, "bar"),
        value = Math.Max(0, ReadNumber(item, "value") ?? 0)
    };

    private static object ReadStep(JsonElement item) => new
    {
        title = Required(item, "title", 80, "step"),
        detail = ReadString(item, "detail", 160),
        done = item.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True
    };

    private static object ReadPair(JsonElement item) => new
    {
        label = Required(item, "label", 40, "pair"),
        value = Required(item, "value", 120, "pair")
    };

    private static string[] Labels(JsonElement block)
    {
        if (!block.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("A chips block needs labels.");
        var result = labels.EnumerateArray()
            .Select(label => label.ValueKind == JsonValueKind.String ? label.GetString()?.Trim() : null)
            .Where(label => !string.IsNullOrEmpty(label))
            .Select(label => label!.Length <= 32 ? label : label[..32])
            .Take(MaxEntries * 2)
            .ToArray();
        return result.Length > 0 ? result : throw new ArgumentException("A chips block needs labels.");
    }

    private static string? Tone(JsonElement element)
    {
        var tone = ReadString(element, "tone", 12)?.ToLowerInvariant();
        return tone is not null && Tones.Contains(tone) ? tone : null;
    }

    private static string Required(JsonElement element, string name, int max, string type) =>
        ReadString(element, name, max) ?? throw new ArgumentException($"A {type} needs a {name}.");

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed
            : null;
    }

    internal static string? ReadString(JsonElement element, string name, int max)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= max ? text : text[..max];
    }
}
