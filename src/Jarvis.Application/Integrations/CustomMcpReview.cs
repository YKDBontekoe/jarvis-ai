namespace Jarvis.Application.Integrations;

public sealed record ReviewLine(string Kind, string Text);

/// <summary>What the owner is about to approve, set against what is running now.</summary>
public sealed record CustomMcpReview(
    string DraftSha256,
    string? ActiveSha256,
    int? ActiveRevision,
    int DraftRevision,
    bool FirstVersion,
    int Added,
    int Removed,
    bool Truncated,
    IReadOnlyList<ReviewLine> Changes,
    IReadOnlyList<string> DependenciesAdded,
    IReadOnlyList<string> DependenciesRemoved,
    IReadOnlyList<string> Keys,
    string TestStatus,
    string TestLabel,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> Limits);

public static class CustomMcpReviewBuilder
{
    public const int MaxLines = 400;
    public const int MaxShown = 80;

    public const string TestNone = "none";
    public const string TestStale = "stale";
    public const string TestFailed = "failed";
    public const string TestPassed = "passed";

    public static CustomMcpReview Build(CustomMcpServerDetail detail)
    {
        var server = detail.Server;
        var draft = detail.DraftSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var active = detail.ActiveSource?.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') ?? [];
        var first = detail.ActiveSource is null;

        var diff = first ? draft.Select(line => new ReviewLine("add", line)).ToList() : Diff(active, draft);
        var added = diff.Count(line => line.Kind == "add");
        var removed = diff.Count(line => line.Kind == "remove");
        var changes = diff.Where(line => line.Kind != "same").Take(MaxShown).ToArray();

        var draftDependencies = CustomMcpScriptValidator.Validate(detail.DraftSource).Dependencies;
        var activeDependencies = detail.ActiveSource is null
            ? []
            : CustomMcpScriptValidator.Validate(detail.ActiveSource).Dependencies;

        var test = server.LastTest;
        var status = test is null ? TestNone
            : !string.Equals(test.Sha256, server.DraftSha256, StringComparison.OrdinalIgnoreCase) ? TestStale
            : test.Ok ? TestPassed : TestFailed;
        var label = status switch
        {
            TestPassed when test!.ToolResult is null =>
                "Startup test: the app starts and lists its tools. It does not prove each tool works.",
            TestPassed => "Tool test: one tool was called with the arguments you gave. Other tools are untested.",
            TestFailed => "The last test of this code failed.",
            TestStale => "The last test was of different code. Test this version.",
            _ => "Not tested yet."
        };

        return new CustomMcpReview(server.DraftSha256, server.ActiveSha256, server.ActiveRevision, server.Revision,
            first, added, removed, diff.Count(line => line.Kind != "same") > MaxShown, changes,
            draftDependencies.Except(activeDependencies).Order().ToArray(),
            activeDependencies.Except(draftDependencies).Order().ToArray(),
            server.Secrets.Select(secret => secret.Key).ToArray(), status, label,
            status == TestPassed ? test!.Tools : [],
            [
                "Reading the code is what the approval rests on. A passing test is not a guarantee.",
                "A test that calls a tool that sends or deletes would act on the real service; it needs your decision first."
            ]);
    }

    /// <summary>A line diff. Large files fall back to comparing lines as sets so the screen stays quick.</summary>
    public static List<ReviewLine> Diff(string[] before, string[] after)
    {
        if (before.Length > MaxLines || after.Length > MaxLines)
        {
            var beforeSet = before.ToHashSet();
            var afterSet = after.ToHashSet();
            return [.. after.Where(line => !beforeSet.Contains(line)).Select(line => new ReviewLine("add", line)),
                .. before.Where(line => !afterSet.Contains(line)).Select(line => new ReviewLine("remove", line))];
        }

        var table = new int[before.Length + 1, after.Length + 1];
        for (var i = before.Length - 1; i >= 0; i--)
            for (var j = after.Length - 1; j >= 0; j--)
                table[i, j] = before[i] == after[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);

        var lines = new List<ReviewLine>();
        int x = 0, y = 0;
        while (x < before.Length && y < after.Length)
        {
            if (before[x] == after[y]) { lines.Add(new ReviewLine("same", before[x])); x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) lines.Add(new ReviewLine("remove", before[x++]));
            else lines.Add(new ReviewLine("add", after[y++]));
        }
        while (x < before.Length) lines.Add(new ReviewLine("remove", before[x++]));
        while (y < after.Length) lines.Add(new ReviewLine("add", after[y++]));
        return lines;
    }
}

public sealed record BuilderExample(string Id, string Title, string Purpose, string Starter);

/// <summary>A prompt Jarvis can build from in chat: the smallest useful contract for a personal app.</summary>
public static class CustomAppStarter
{
    public const int MaxAnswer = 400;

    public static readonly IReadOnlyList<BuilderExample> Examples =
    [
        new("tracker", "A personal tracker", "Log something each day and see the trend.",
            "A tracker for my water intake"),
        new("lookup", "A lookup on one source", "Ask one website or API a narrow question.",
            "Look up today's exchange rate from a public API"),
        new("helper", "A project helper", "Do a small repeated chore for one project.",
            "Turn my meeting notes into a list of follow-ups"),
        new("calculator", "A comparison calculator", "Compare two options with your own numbers.",
            "Compare the monthly cost of two phone plans over 24 months")
    ];

    public sealed record Answers(string? Purpose, string? Inputs, string? Data, string? Result, bool ChangesOutside);

    public static (string? Prompt, string? Field, string? Message) Compose(Answers answers)
    {
        var purpose = Clean(answers.Purpose);
        if (purpose is null) return (null, "purpose", "Say what the app should do.");
        var lines = new List<string>
        {
            $"Build me a small personal app (a custom MCP server) that does this: {purpose}"
        };
        if (Clean(answers.Inputs) is { } inputs) lines.Add($"What I will give it: {inputs}");
        lines.Add(Clean(answers.Data) is { } data
            ? $"Where its data comes from: {data}. Check the documentation first; do not invent endpoints."
            : "Where its data comes from: find a documented public API, or use clearly labelled sample data. Do not invent endpoints.");
        if (Clean(answers.Result) is { } result) lines.Add($"What I want back: {result}");
        lines.Add(answers.ChangesOutside
            ? "It may change things outside Jarvis, so each tool that does must be clearly named and ask me first."
            : "It must only read and calculate. It must not send, delete or change anything outside Jarvis.");
        lines.Add("Write it, test it with safe sample data, and show me the result. Do not switch it on: I will review the code first.");
        return (string.Join('\n', lines), null, null);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= MaxAnswer ? text : text[..MaxAnswer].TrimEnd() + "…";
    }
}
