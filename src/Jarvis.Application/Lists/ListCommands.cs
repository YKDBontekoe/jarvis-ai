using System.Text;

namespace Jarvis.Application.Lists;

/// <summary>What a short list command typed in a chat app asks for.</summary>
public abstract record ListCommand
{
    public sealed record Show(string? List) : ListCommand;

    public sealed record Add(string List, IReadOnlyList<string> Items) : ListCommand;

    public sealed record Tick(string List, IReadOnlyList<string> Items) : ListCommand;

    public sealed record Clear(string List) : ListCommand;

    public sealed record Rename(string List, string NewName) : ListCommand;

    public sealed record Remove(string List, IReadOnlyList<string> Items) : ListCommand;

    public sealed record Usage(string Message) : ListCommand;
}

/// <summary>
/// Quick list commands for WhatsApp and Signal that need no model: <c>/lists</c>, <c>/list shopping</c>,
/// <c>/add shopping: milk, eggs</c> and <c>/done shopping: milk</c>. Anything else is not a list command (null).
/// </summary>
public static class ListCommands
{
    public const string UsageText =
        "Lists: /lists shows them, /list shopping shows one, /add shopping: milk, eggs adds items, " +
        "/done shopping: milk ticks one off, /remove shopping: milk takes one off, /clear shopping removes the ticked ones.";

    public static ListCommand? Parse(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith('/')) return null;
        var space = trimmed.IndexOfAny([' ', '\n']);
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        switch (command)
        {
            case "/lists":
                return new ListCommand.Show(null);
            case "/list":
                return rest.Length == 0 ? new ListCommand.Show(null) : new ListCommand.Show(rest);
            case "/rename":
                var split = rest.IndexOf(':');
                if (split <= 0 || split == rest.Length - 1) return new ListCommand.Usage(UsageText);
                var from = rest[..split].Trim();
                var to = rest[(split + 1)..].Trim();
                return from.Length == 0 || to.Length == 0 ? new ListCommand.Usage(UsageText) : new ListCommand.Rename(from, to);
            case "/clear":
                return rest.Length == 0 ? new ListCommand.Usage(UsageText) : new ListCommand.Clear(rest);
            case "/add" or "/done" or "/remove":
                var colon = rest.IndexOf(':');
                if (colon <= 0 || colon == rest.Length - 1)
                    return new ListCommand.Usage(UsageText);
                var list = rest[..colon].Trim();
                var items = rest[(colon + 1)..].Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries |
                                                                       StringSplitOptions.TrimEntries);
                if (list.Length == 0 || items.Length == 0) return new ListCommand.Usage(UsageText);
                return command switch
                {
                    "/add" => new ListCommand.Add(list, items),
                    "/remove" => new ListCommand.Remove(list, items),
                    _ => new ListCommand.Tick(list, items),
                };
            default:
                return null;
        }
    }

    /// <summary>One list as a short chat message: its open items first, then what is ticked.</summary>
    public static string Render(ListRecord list)
    {
        var text = new StringBuilder("**").Append(list.Name).Append("**\n");
        if (list.Items.Count == 0) return text.Append("(empty)").ToString();
        foreach (var item in list.Items.Where(i => !i.Done).Take(40)) text.Append("☐ ").Append(item.Text).Append('\n');
        foreach (var item in list.Items.Where(i => i.Done).Take(15)) text.Append("☑ ").Append(item.Text).Append('\n');
        return text.ToString().TrimEnd();
    }
}
