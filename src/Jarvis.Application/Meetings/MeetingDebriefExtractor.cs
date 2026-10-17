using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jarvis.Application.Search;

namespace Jarvis.Application.Meetings;

/// <summary>
/// Reads a debrief (typed, pasted or dictated) and proposes what it seems to say: decisions, what the owner owes, what
/// someone else owes, and questions left open. Rule-based, English and Dutch, so it works with no model and is the same
/// every time. The text is untrusted data: it can only ever produce proposals, and a proposal does nothing until the
/// owner accepts it.
/// </summary>
public static partial class MeetingDebriefExtractor
{
    private const int MaxSentenceLength = 300;

    private static readonly string[] DecisionCues =
    [
        "we decided", "we've decided", "we have decided", "decided to", "we agreed", "agreed to", "agreed that",
        "the decision is", "we'll go with", "we will go with", "we're going with", "we are going with", "we go with",
        "we hebben besloten", "besloten", "afgesproken", "we gaan voor", "we kiezen", "het besluit is"
    ];

    private static readonly string[] TheyOweVerbs =
    [
        "will", "'ll", "is going to", "is gonna", "can", "to", "zal", "gaat", "stuurt", "regelt", "belt", "mailt",
        "maakt", "neemt", "plant", "zorgt"
    ];

    private static readonly (string Name, DayOfWeek Day)[] Weekdays =
    [
        ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday),
        ("sunday", DayOfWeek.Sunday), ("maandag", DayOfWeek.Monday), ("dinsdag", DayOfWeek.Tuesday),
        ("woensdag", DayOfWeek.Wednesday), ("donderdag", DayOfWeek.Thursday), ("vrijdag", DayOfWeek.Friday),
        ("zaterdag", DayOfWeek.Saturday), ("zondag", DayOfWeek.Sunday)
    ];

    private static readonly string[] Months =
    [
        "january", "february", "march", "april", "may", "june", "july", "august", "september", "october",
        "november", "december"
    ];

    private static readonly string[] DutchMonths =
    [
        "januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober",
        "november", "december"
    ];

    private static readonly string[] VagueDates =
    [
        "next week", "this week", "volgende week", "deze week", "end of the month", "end of month", "eom",
        "next month", "volgende maand", "end of the week", "eind van de week", "eind van de maand", "soon",
        "binnenkort", "asap", "zsm", "later this month"
    ];

    public static IReadOnlyList<DebriefCandidate> Extract(string? text, IReadOnlyList<string> participantNames,
        DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var names = participantNames
            .Select(name => name.Trim()).Where(name => name.Length > 0)
            .Select(name => (Name: name, Key: SearchRanking.NormalizeQuery(name))).ToArray();
        var result = new List<DebriefCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sentence in Sentences(text))
        {
            var candidate = Classify(sentence, names, today);
            if (candidate is null || !seen.Add(candidate.Id)) continue;
            result.Add(candidate);
            if (result.Count >= MeetingRules.MaxCandidates) break;
        }
        return result;
    }

    private static IEnumerable<string> Sentences(string text)
    {
        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = BulletPrefix().Replace(line.Trim(), string.Empty);
            foreach (var part in SentenceBreak().Split(cleaned))
            {
                var sentence = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (sentence.Length < 8) continue;
                yield return sentence.Length <= MaxSentenceLength ? sentence : sentence[..(MaxSentenceLength - 1)].TrimEnd() + "…";
            }
        }
    }

    private static DebriefCandidate? Classify(string sentence, (string Name, string Key)[] names, DateOnly today)
    {
        var key = SearchRanking.NormalizeQuery(sentence);
        var padded = " " + new string(key.Select(c => char.IsLetterOrDigit(c) || c == '\'' ? c : ' ').ToArray()) + " ";

        if (sentence.EndsWith('?'))
            return Build(CandidateKinds.Question, sentence, null, null, null, key);
        if (DecisionCues.Any(cue => padded.Contains(" " + cue + " ", StringComparison.Ordinal) ||
                                    cue.Contains(' ') && key.Contains(cue, StringComparison.Ordinal)))
        {
            var (dueOn, dueText) = Due(sentence, today);
            return Build(CandidateKinds.Decision, sentence, null, dueOn, dueText, key);
        }
        if (IOwePattern().IsMatch(sentence) || DutchIOwePattern().IsMatch(sentence))
        {
            var (dueOn, dueText) = Due(sentence, today);
            var who = names.FirstOrDefault(name => padded.Contains(" " + name.Key + " ", StringComparison.Ordinal));
            var counterparty = who.Name ?? (names.Length == 1 ? names[0].Name : null);
            return Build(CandidateKinds.IOwe, sentence, counterparty, dueOn, dueText, key);
        }
        foreach (var (name, nameKey) in names)
        {
            foreach (var verb in TheyOweVerbs)
            {
                var needle = verb.StartsWith('\'') ? " " + nameKey + verb + " " : " " + nameKey + " " + verb + " ";
                if (!padded.Contains(needle, StringComparison.Ordinal)) continue;
                var (dueOn, dueText) = Due(sentence, today);
                return Build(CandidateKinds.TheyOwe, sentence, name, dueOn, dueText, key);
            }
        }
        return null;
    }

    private static DebriefCandidate Build(string kind, string sentence, string? counterparty, DateOnly? dueOn,
        string? dueText, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + key));
        return new DebriefCandidate($"{kind}-{Convert.ToHexString(hash)[..10].ToLowerInvariant()}", kind, sentence,
            counterparty, dueOn, dueText, sentence);
    }

    /// <summary>
    /// A date the sentence names, or its wording when it cannot be resolved ("next week"), so the owner can pick a day
    /// instead of Jarvis guessing one.
    /// </summary>
    public static (DateOnly? DueOn, string? DueText) Due(string sentence, DateOnly today)
    {
        var lower = SearchRanking.NormalizeQuery(sentence);
        if (IsoDate().Match(lower) is { Success: true } iso &&
            DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return (exact, iso.Value);
        foreach (var vague in VagueDates)
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(vague)}\b")) return (null, vague);
        if (Regex.IsMatch(lower, @"\b(today|vandaag)\b")) return (today, "today");
        if (Regex.IsMatch(lower, @"\b(tomorrow|morgen)\b")) return (today.AddDays(1), "tomorrow");
        foreach (var (name, day) in Weekdays)
        {
            if (!Regex.IsMatch(lower, $@"\b{name}\b")) continue;
            var ahead = ((int)day - (int)today.DayOfWeek + 7) % 7;
            return (today.AddDays(ahead == 0 ? 7 : ahead), name);
        }
        var monthMatch = DayMonth().Match(lower);
        if (monthMatch.Success)
        {
            var monthName = monthMatch.Groups["month"].Value;
            var month = Array.IndexOf(Months, monthName) + 1;
            if (month == 0) month = Array.IndexOf(DutchMonths, monthName) + 1;
            if (month == 0)
                month = Array.FindIndex(Months, m => m.StartsWith(monthName, StringComparison.Ordinal)) + 1;
            var day = int.Parse(monthMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
            if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(today.Year + 1, month))
            {
                var year = today.Year;
                var thisYear = day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : (DateOnly?)null;
                if (thisYear is null || thisYear < today) year++;
                if (day <= DateTime.DaysInMonth(year, month))
                    return (new DateOnly(year, month, day), monthMatch.Value);
            }
            return (null, monthMatch.Value);
        }
        return (null, null);
    }

    [GeneratedRegex(@"^\s*(?:[-*•]+|\d+[.)])\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPrefix();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\bI(?:'ll|’ll| will| shall| can| am going to|'m going to|’m going to)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IOwePattern();

    [GeneratedRegex(@"\bik\s+(?:zal|ga|stuur|regel|bel|mail|maak|neem|plan|zorg)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DutchIOwePattern();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(?:(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<month>[a-z]{3,9})|(?<month>[a-z]{3,9})\s+(?<day>\d{1,2})(?:st|nd|rd|th)?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DayMonth();
}
