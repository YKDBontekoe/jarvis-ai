using System.Globalization;
using System.Text;

namespace Jarvis.Application.Finance;

/// <summary>
/// Reads a bank's CSV export without knowing the bank: it looks for the date, amount, description and currency
/// columns by their English and Dutch header names, accepts , ; or tab as separator, and understands signed amounts,
/// separate debit/credit columns and "Af/Bij" columns. Only money going out becomes spending.
/// </summary>
public static class BankCsvParser
{
    private static readonly string[] DateHeaders = ["date", "datum", "booking", "boekdatum", "transaction date", "posted"];
    private static readonly string[] AmountHeaders = ["amount", "bedrag", "value", "transaction amount", "bedrag (eur)"];
    private static readonly string[] DescriptionHeaders =
        ["description", "omschrijving", "naam", "name", "merchant", "payee", "counterparty", "mededelingen", "details"];
    private static readonly string[] DirectionHeaders = ["af bij", "af/bij", "debit/credit", "credit debit", "dc", "type"];
    private static readonly string[] CurrencyHeaders = ["currency", "valuta", "munt", "ccy"];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyyMMdd", "dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy", "d-M-yyyy", "d/M/yyyy", "d.M.yyyy",
        "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss"
    ];

    public static CsvParseResult Parse(string? csv, string? defaultCurrency)
    {
        var fallback = ExpensesCurrency(defaultCurrency);
        var lines = (csv ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (lines.Length < 2) return new CsvParseResult([], 0, 0, "The file needs a header row and at least one row.");

        var separator = new[] { ';', ',', '\t' }.MaxBy(c => lines[0].Count(x => x == c));
        var header = Split(lines[0], separator).Select(x => x.Trim().ToLowerInvariant()).ToArray();
        var date = Find(header, DateHeaders);
        var amount = Find(header, AmountHeaders);
        var debit = Array.FindIndex(header, x => x is "debit" or "af" or "uitgaven" or "withdrawal");
        var credit = Array.FindIndex(header, x => x is "credit" or "bij" or "inkomsten" or "deposit");
        if (date < 0 || amount < 0 && debit < 0)
            return new CsvParseResult([], 0, 0,
                "Could not find the date and amount columns. Use a header row such as Date, Amount, Description.");
        var direction = Find(header, DirectionHeaders);
        var currencyColumn = Find(header, CurrencyHeaders);
        var descriptionColumns = header.Select((name, index) => (name, index))
            .Where(x => DescriptionHeaders.Any(h => x.name == h || x.name.Contains(h, StringComparison.Ordinal)) &&
                        x.index != date && x.index != amount)
            .Select(x => x.index).Take(2).ToArray();

        var rows = new List<ImportRow>();
        int income = 0, unreadable = 0;
        foreach (var line in lines.Skip(1))
        {
            var cells = Split(line, separator);
            if (!TryDate(Cell(cells, date), out var day)) { unreadable++; continue; }

            decimal? value;
            if (amount >= 0 && ParseAmount(Cell(cells, amount)) is { } signed)
            {
                value = signed;
                if (direction >= 0)
                {
                    var flag = Cell(cells, direction).Trim().ToLowerInvariant();
                    if (flag is "af" or "debit" or "d" or "db") value = -Math.Abs(signed);
                    else if (flag is "bij" or "credit" or "c" or "cr") value = Math.Abs(signed);
                }
            }
            else if (debit >= 0 && ParseAmount(Cell(cells, debit)) is { } out_ && out_ != 0)
            {
                value = -Math.Abs(out_);
            }
            else if (credit >= 0 && ParseAmount(Cell(cells, credit)) is { } in_ && in_ != 0)
            {
                value = Math.Abs(in_);
            }
            else
            {
                unreadable++;
                continue;
            }

            if (value >= 0) { income++; continue; }
            var text = string.Join(" · ", descriptionColumns.Select(i => Clean(Cell(cells, i)))
                .Where(x => x.Length > 0).Distinct());
            var merchant = CleanMerchant(descriptionColumns.Length == 0 ? "" : Cell(cells, descriptionColumns[0]));
            var currency = Expenses.ExpenseRules.NormalizeCurrency(Cell(cells, currencyColumn)) ?? fallback;
            rows.Add(new ImportRow(day, Math.Round(Math.Abs(value!.Value), 2), currency,
                merchant.Length == 0 ? null : merchant,
                text.Length == 0 ? null : text.Length > 200 ? text[..200] : text));
        }
        return new CsvParseResult(rows, income, unreadable, rows.Count == 0 && income == 0 ? "No rows could be read." : null);
    }

    private static string ExpensesCurrency(string? currency) =>
        Expenses.ExpenseRules.NormalizeCurrency(currency) ?? Expenses.ExpenseRules.DefaultCurrency;

    private static int Find(string[] header, string[] names)
    {
        foreach (var name in names)
        {
            var exact = Array.IndexOf(header, name);
            if (exact >= 0) return exact;
        }
        foreach (var name in names)
        {
            var partial = Array.FindIndex(header, x => x.Contains(name, StringComparison.Ordinal));
            if (partial >= 0) return partial;
        }
        return -1;
    }

    private static string Cell(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index] : string.Empty;

    private static string Clean(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Drops card prefixes and trailing references ("BEA, Apple Pay Albert Heijn 1234,Pas 5") from a payee.</summary>
    internal static string CleanMerchant(string value)
    {
        var text = Clean(value);
        foreach (var prefix in new[] { "BEA, Apple Pay ", "BEA ", "CCV*", "SumUp *", "iDEAL ", "SEPA ", "POS " })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) text = text[prefix.Length..];
        var cut = text.IndexOfAny([',', '|']);
        if (cut > 2) text = text[..cut];
        var words = text.Split(' ').TakeWhile(w => !(w.Length > 3 && w.All(char.IsDigit))).ToArray();
        text = string.Join(' ', words);
        return text.Length > Expenses.ExpenseRules.MaxMerchantLength
            ? text[..Expenses.ExpenseRules.MaxMerchantLength].TrimEnd()
            : text;
    }

    internal static bool TryDate(string text, out DateOnly date)
    {
        var value = text.Trim().Trim('"');
        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        date = default;
        return false;
    }

    /// <summary>Reads "-12,50", "(12.50)", "€ 1.234,56", "1,234.56" and "12.50-"; null when there is no number.</summary>
    public static decimal? ParseAmount(string text)
    {
        var value = text.Trim().Trim('"');
        if (value.Length == 0) return null;
        var negative = value.StartsWith('-') || value.EndsWith('-') || value.StartsWith('(') && value.EndsWith(')');
        var digits = new string(value.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        if (digits.Length == 0 || !digits.Any(char.IsDigit)) return null;
        var lastSeparator = digits.LastIndexOfAny(['.', ',']);
        if (lastSeparator >= 0 && digits.Length - lastSeparator - 1 is 1 or 2)
            digits = digits[..lastSeparator].Replace(".", "").Replace(",", "") + "." + digits[(lastSeparator + 1)..];
        else
            digits = digits.Replace(".", "").Replace(",", "");
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return null;
        return negative ? -number : number;
    }

    private static List<string> Split(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == separator && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }
}
