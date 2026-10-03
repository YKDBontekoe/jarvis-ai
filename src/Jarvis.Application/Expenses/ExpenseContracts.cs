using System.Globalization;
using System.Text;
using Jarvis.Domain.Expenses;

namespace Jarvis.Application.Expenses;

public static class ExpenseCategories
{
    public const string Groceries = "groceries";
    public const string Dining = "dining";
    public const string Transport = "transport";
    public const string Shopping = "shopping";
    public const string Housing = "housing";
    public const string Bills = "bills";
    public const string Health = "health";
    public const string Entertainment = "entertainment";
    public const string Travel = "travel";
    public const string Subscriptions = "subscriptions";
    public const string Other = "other";

    /// <summary>Every category, in the order the app shows them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Groceries, Dining, Transport, Shopping, Housing, Bills, Health, Entertainment, Travel, Subscriptions, Other
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? category) =>
        category is not null && Known.Contains(category);

    // Dutch and English keywords. Keywords shorter than five characters match whole words only ("ns", "bar");
    // longer ones also match the start of a word ("supermarkt" in "supermarktje").
    private static readonly (string Category, string[] Words)[] Keywords =
    [
        (Groceries, ["albert heijn", "ah to go", "jumbo", "lidl", "aldi", "plus", "dirk", "coop", "spar", "picnic",
            "ekoplaza", "vomar", "hoogvliet", "deen", "boodschap", "grocer", "supermark", "supermarket", "groente",
            "bakker", "slager", "markt"]),
        (Dining, ["lunch", "dinner", "diner", "ontbijt", "breakfast", "restaurant", "cafe", "café", "koffie",
            "coffee", "starbucks", "mcdonald", "burger", "pizza", "sushi", "thuisbezorgd", "uber eats", "deliveroo",
            "eten", "borrel", "bar", "bier", "drinks", "snack", "kebab", "döner", "lunchroom", "takeaway"]),
        (Transport, ["ns", "ov", "ovchipkaart", "trein", "train", "bus", "tram", "metro", "taxi", "uber", "bolt",
            "tank", "benzine", "diesel", "fuel", "shell", "esso", "bp", "total", "tinq", "parkeren", "parking",
            "laadpaal", "charging", "fiets", "swapfiets", "wegenbelasting", "apk", "garage"]),
        (Shopping, ["bol", "bol.com", "amazon", "coolblue", "zalando", "hema", "action", "ikea", "kruidvat", "etos",
            "blokker", "mediamarkt", "primark", "h&m", "zara", "kleding", "clothes", "schoenen", "shoes", "cadeau",
            "gift", "decathlon", "gamma", "praxis", "karwei"]),
        (Housing, ["huur", "rent", "hypotheek", "mortgage", "vve", "meubel", "furniture", "schoonmaak"]),
        (Bills, ["energie", "energy", "stroom", "gas", "water", "eneco", "vattenfall", "essent", "internet", "ziggo",
            "kpn", "odido", "t-mobile", "vodafone", "telefoon", "phone", "verzekering", "insurance", "belasting",
            "gemeente", "tax"]),
        (Health, ["apotheek", "pharmacy", "dokter", "doctor", "huisarts", "tandarts", "dentist", "fysio",
            "ziekenhuis", "hospital", "medicijn", "medicine", "sportschool", "gym", "basic-fit", "basic fit",
            "opticien", "bril"]),
        (Entertainment, ["bioscoop", "cinema", "pathe", "pathé", "film", "movie", "concert", "theater", "museum",
            "ticket", "kaartje", "festival", "game", "steam", "playstation", "xbox", "nintendo", "boek", "book",
            "bowling", "pretpark"]),
        (Travel, ["hotel", "airbnb", "booking", "vlucht", "flight", "klm", "transavia", "ryanair", "easyjet",
            "vakantie", "holiday", "vacation", "camping", "schiphol"]),
        (Subscriptions, ["netflix", "spotify", "disney", "videoland", "hbo", "youtube premium", "icloud",
            "google one", "chatgpt", "openai", "abonnement", "subscription", "patreon", "nyt", "krant"]),
    ];

    /// <summary>
    /// Maps a model or user supplied category ("food", "Boodschappen", "eten") to a known one; null when nothing
    /// fits, so callers can fall back to <see cref="Guess"/>.
    /// </summary>
    public static string? Normalize(string? category)
    {
        var key = ExpenseRules.Key(category);
        if (key.Length == 0) return null;
        if (Known.Contains(key)) return key;
        return key switch
        {
            "food" or "boodschappen" or "grocery" or "supermarket" or "supermarkt" => Groceries,
            "restaurant" or "restaurants" or "eating out" or "uit eten" or "eten" or "horeca" or "drinks" or "coffee"
                or "lunch" or "dinner" => Dining,
            "vervoer" or "reizen" or "car" or "auto" or "fuel" or "ov" or "travel local" or "commute" => Transport,
            "winkelen" or "kleding" or "clothes" or "clothing" or "electronics" or "household" or "huishouden" =>
                Shopping,
            "wonen" or "rent" or "huur" or "home" => Housing,
            "vaste lasten" or "utilities" or "bill" or "rekeningen" or "insurance" or "verzekering" => Bills,
            "gezondheid" or "zorg" or "medical" or "pharmacy" or "sport" or "fitness" => Health,
            "uitjes" or "vrije tijd" or "leisure" or "fun" or "hobby" or "hobbies" => Entertainment,
            "vakantie" or "holiday" or "vacation" or "trip" => Travel,
            "abonnementen" or "subscription" or "streaming" => Subscriptions,
            "overig" or "misc" or "miscellaneous" or "anders" => Other,
            _ => All.FirstOrDefault(x => key.StartsWith(x, StringComparison.Ordinal))
        };
    }

    /// <summary>
    /// Picks a category from the merchant, then the note ("Albert Heijn" is groceries, "lunch" is dining); other
    /// when unsure.
    /// </summary>
    public static string Guess(string? merchant, string? note) =>
        GuessFrom(merchant) ?? GuessFrom(note) ?? Other;

    private static string? GuessFrom(string? value)
    {
        var key = ExpenseRules.Key(value);
        if (key.Length == 0) return null;
        var text = $" {key} ";
        foreach (var (category, words) in Keywords)
        {
            foreach (var word in words)
            {
                var wordKey = ExpenseRules.Key(word);
                var needle = wordKey.Length < 5 ? $" {wordKey} " : $" {wordKey}";
                if (text.Contains(needle, StringComparison.Ordinal)) return category;
            }
        }
        return null;
    }
}

public static class ExpenseSources
{
    public const string Chat = "chat";
    public const string Receipt = "receipt";
    public const string App = "app";
    public const string Import = "import";

    public static bool IsValid(string? source) => source is Chat or Receipt or App or Import;
}

public static class ExpenseRules
{
    public const decimal MaxAmount = 1_000_000m;
    public const int MaxMerchantLength = 80;
    public const int MaxNoteLength = 200;
    public const string DefaultCurrency = "EUR";

    /// <summary>How far back an expense may be dated. Older spending belongs in a bookkeeping tool.</summary>
    public const int MaxYearsBack = 5;

    public static string? ValidateAmount(decimal? amount)
    {
        if (amount is not { } value) return "Enter an amount.";
        if (value <= 0) return "The amount must be more than zero.";
        return value > MaxAmount ? $"The amount can be at most {MaxAmount:N0}." : null;
    }

    /// <summary>Uppercase ISO 4217 code ("eur", "€" become "EUR"); null when it is not three letters.</summary>
    public static string? NormalizeCurrency(string? currency)
    {
        var clean = currency?.Trim();
        if (string.IsNullOrEmpty(clean)) return null;
        clean = clean switch
        {
            "€" => "EUR",
            "$" => "USD",
            "£" => "GBP",
            "¥" => "JPY",
            _ => clean.ToUpperInvariant()
        };
        return clean.Length == 3 && clean.All(c => c is >= 'A' and <= 'Z') ? clean : null;
    }

    /// <summary>Trims and collapses whitespace; null when nothing is left.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Lowercase words without accents or punctuation, used to compare merchants and match keywords.</summary>
    public static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '&' or '-'
                ? char.ToLowerInvariant(character)
                : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Rounds to cents, the precision stored and shown.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>What a caller wants an expense to be. Null fields fall back to defaults on create.</summary>
public sealed record ExpenseDraft(
    decimal? Amount,
    string? Currency = null,
    string? Merchant = null,
    string? Category = null,
    string? Note = null,
    DateOnly? SpentOn = null,
    Guid? ReceiptFileId = null,
    string Source = ExpenseSources.App);

public enum ExpenseFailure
{
    None,
    NotFound,
    Invalid
}

/// <summary>Outcome of an expense change: the value, or why it failed and which field caused it.</summary>
public sealed record ExpenseOperation<T>(T? Value, ExpenseFailure Failure = ExpenseFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == ExpenseFailure.None;

    public static ExpenseOperation<T> Ok(T value) => new(value);
    public static ExpenseOperation<T> NotFound() => new(default, ExpenseFailure.NotFound);

    public static ExpenseOperation<T> Invalid(string field, string message) =>
        new(default, ExpenseFailure.Invalid, field, message);
}

/// <summary>
/// A newly logged expense, or the existing one (<see cref="IsDuplicate"/>) when the same amount at the same merchant was already logged
/// that day and the caller asked to skip duplicates.
/// </summary>
public sealed record ExpenseCreated(Expense Expense, bool IsDuplicate);

public sealed record CategoryTotal(string Category, decimal Total, int Count);

public sealed record CurrencyTotal(string Currency, decimal Total, int Count);

public sealed record DayTotal(DateOnly Date, decimal Total);

public sealed record MerchantTotal(string Merchant, decimal Total, int Count);

/// <summary>
/// One month of spending in the owner's main currency (the one used most that month). Spending in other
/// currencies is listed separately and never converted.
/// </summary>
public sealed record ExpenseMonthSummary(
    int Year,
    int Month,
    string Currency,
    decimal Total,
    int Count,
    decimal PreviousTotal,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<DayTotal> Days,
    IReadOnlyList<MerchantTotal> TopMerchants,
    IReadOnlyList<CurrencyTotal> OtherCurrencies);

public interface IExpenseRepository
{
    /// <summary>The owner's expenses dated within [from, to], newest first.</summary>
    Task<IReadOnlyList<Expense>> ListAsync(Guid ownerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit, CancellationToken cancellationToken);
    Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(Expense expense, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Expense expense, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IExpenseService
{
    Task<IReadOnlyList<Expense>> ListMonthAsync(Guid ownerId, int year, int month, string? category,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit, CancellationToken cancellationToken);
    Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Logs an expense. A missing date is <paramref name="today"/>, a missing currency is the one the owner used
    /// last (EUR at first), and a missing category is guessed from the merchant and note. With
    /// <paramref name="skipDuplicates"/>, an identical expense (same day, amount and merchant) is returned instead.
    /// </summary>
    Task<ExpenseOperation<ExpenseCreated>> CreateAsync(Guid ownerId, ExpenseDraft draft, DateOnly today,
        bool skipDuplicates, CancellationToken cancellationToken);

    /// <summary>Changes the fields that are set in <paramref name="draft"/>; null fields keep their value.</summary>
    Task<ExpenseOperation<Expense>> UpdateAsync(Guid id, Guid ownerId, ExpenseDraft draft, DateOnly today,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<ExpenseMonthSummary> SummarizeMonthAsync(Guid ownerId, int year, int month,
        CancellationToken cancellationToken);
}

/// <summary>What a receipt photo says, read by a model. Every field may be missing when the photo is unclear.</summary>
public sealed record ReceiptReading(
    decimal? Amount,
    string? Currency,
    string? Merchant,
    string? Category,
    DateOnly? SpentOn,
    string? Note);

public enum ReceiptReadFailure
{
    None,
    NotFound,
    NotAnImage,
    Unreadable
}

public sealed record ReceiptReadResult(ReceiptReading? Reading, ReceiptReadFailure Failure = ReceiptReadFailure.None)
{
    public bool Succeeded => Failure == ReceiptReadFailure.None;
}

/// <summary>Reads the total, shop, and date from a photo of a receipt the owner uploaded.</summary>
public interface IReceiptReader
{
    Task<ReceiptReadResult> ReadAsync(Guid ownerId, Guid fileId, CancellationToken cancellationToken);
}

/// <summary>Told about every new expense, so budgets can warn the moment a limit is crossed.</summary>
public interface IExpenseObserver
{
    Task OnExpenseCreatedAsync(Expense expense, CancellationToken cancellationToken);
}
