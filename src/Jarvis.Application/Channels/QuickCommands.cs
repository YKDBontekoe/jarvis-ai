using System.Globalization;

namespace Jarvis.Application.Channels;

/// <summary>A quick chat command that needs no model: habit check-in, a spending note, or today's plan.</summary>
public abstract record QuickCommand
{
    public sealed record CheckIn(IReadOnlyList<string> Habits) : QuickCommand;

    public sealed record Spent(decimal Amount, string? Currency, string? Description) : QuickCommand;

    public sealed record Journal(string Text) : QuickCommand;

    public sealed record ShowHabits : QuickCommand;

    public sealed record Month : QuickCommand;

    public sealed record Today : QuickCommand;

    public sealed record Usage(string Message) : QuickCommand;
}

/// <summary>
/// <c>/checkin meditate, read</c>, <c>/spent 12.50 lunch with Sam</c> (also <c>€12,50</c>) and <c>/today</c>. Other
/// messages are not quick commands (null).
/// </summary>
public static class QuickCommands
{
    public const string CheckInUsage = "Check in with /checkin meditate (several habits: /checkin meditate, read).";
    public const string SpentUsage = "Log spending with /spent 12.50 lunch (also €12,50 or 12 usd).";
    public const string JournalUsage = "Write to today's journal with /journal followed by what you want to say.";
    public const int MaxJournalLength = 4000;
    public const decimal MaxAmount = 1_000_000m;

    public static QuickCommand? Parse(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith('/')) return null;
        var space = trimmed.IndexOfAny([' ', '\n']);
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        switch (command)
        {
            case "/today":
                return new QuickCommand.Today();
            case "/habits":
                return new QuickCommand.ShowHabits();
            case "/month":
                return new QuickCommand.Month();
            case "/checkin":
                var habits = rest.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Flat).Where(name => name.Length > 0).Take(5).ToArray();
                return habits.Length == 0 ? new QuickCommand.Usage(CheckInUsage) : new QuickCommand.CheckIn(habits);
            case "/spent":
                return ParseSpent(rest);
            case "/journal":
                var entry = new string(rest.Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
                if (entry.Length == 0) return new QuickCommand.Usage(JournalUsage);
                return new QuickCommand.Journal(entry.Length <= MaxJournalLength ? entry : entry[..MaxJournalLength].TrimEnd());
            default:
                return null;
        }
    }

    private static QuickCommand ParseSpent(string rest)
    {
        if (rest.Length == 0) return new QuickCommand.Usage(SpentUsage);
        string? currency = null;
        var body = rest;
        if (body.Length > 0 && CurrencySymbol(body[0]) is { } symbol)
        {
            currency = symbol;
            body = body[1..].TrimStart();
        }
        var space = body.IndexOf(' ');
        var token = space < 0 ? body : body[..space];
        var after = space < 0 ? "" : body[(space + 1)..].Trim();
        if (!TryAmount(token, out var amount)) return new QuickCommand.Usage(SpentUsage);
        // "12 eur lunch" or "12 € lunch": a currency word right after the number.
        if (currency is null && after.Length > 0)
        {
            var next = after.Split(' ', 2)[0].ToLowerInvariant();
            var named = next switch { "eur" or "euro" or "€" => "EUR", "usd" or "$" => "USD", "gbp" or "£" => "GBP", _ => null };
            if (named is not null)
            {
                currency = named;
                after = after.Contains(' ') ? after[(after.IndexOf(' ') + 1)..].Trim() : "";
            }
        }
        var description = Flat(after);
        return new QuickCommand.Spent(amount, currency, description.Length == 0 ? null : description);
    }

    private static string? CurrencySymbol(char c) => c switch { '€' => "EUR", '$' => "USD", '£' => "GBP", _ => null };

    private static bool TryAmount(string token, out decimal amount)
    {
        amount = 0;
        var text = token.Trim().TrimEnd('€', '$', '£');
        // Dutch "12,50" is 12.50; "1,234.50" keeps its comma as a thousands separator.
        if (text.Count(c => c == ',') == 1 && !text.Contains('.')) text = text.Replace(',', '.');
        else text = text.Replace(",", "");
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) &&
               amount > 0 && amount <= MaxAmount;
    }

    private static string Flat(string raw)
    {
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= 120 ? clean : clean[..120].TrimEnd();
    }
}
