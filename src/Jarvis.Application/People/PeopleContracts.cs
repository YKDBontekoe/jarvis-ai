using System.Globalization;
using System.Text;
using Jarvis.Application.Memory;
using Jarvis.Domain.People;

namespace Jarvis.Application.People;

public static class PeopleRules
{
    public const int MaxNameLength = 80;
    public const int MaxRelationshipLength = 40;
    public const int MaxNotesLength = 2_000;
    public const int MaxPeople = 500;
    public const int MinContactEveryDays = 1;
    public const int MaxContactEveryDays = 365;

    /// <summary>Trims and collapses runs of whitespace; null when nothing is left.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Trims notes but keeps their line breaks.</summary>
    public static string? CleanNotes(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lowercase letters and digits without accents, so "Zoë" and "zoe" are the same person.</summary>
    public static string NameKey(string? name) => string.Concat(Words(name));

    /// <summary>The lowercase, accent-free words of a name.</summary>
    public static IReadOnlyList<string> Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>A birthday as month and day, with the year when it is known.</summary>
public readonly record struct Birthday(int Month, int Day, int? Year)
{
    private static readonly Dictionary<string, int> MonthNames = new(StringComparer.Ordinal)
    {
        ["january"] = 1, ["jan"] = 1, ["januari"] = 1, ["february"] = 2, ["feb"] = 2, ["februari"] = 2,
        ["march"] = 3, ["mar"] = 3, ["maart"] = 3, ["mrt"] = 3, ["april"] = 4, ["apr"] = 4, ["may"] = 5,
        ["mei"] = 5, ["june"] = 6, ["jun"] = 6, ["juni"] = 6, ["july"] = 7, ["jul"] = 7, ["juli"] = 7,
        ["august"] = 8, ["aug"] = 8, ["augustus"] = 8, ["september"] = 9, ["sep"] = 9, ["sept"] = 9,
        ["october"] = 10, ["oct"] = 10, ["oktober"] = 10, ["okt"] = 10, ["november"] = 11, ["nov"] = 11,
        ["december"] = 12, ["dec"] = 12
    };

    public static bool IsValid(int month, int day, int? year)
    {
        if (month is < 1 or > 12 || day < 1) return false;
        // Allow 29 February without a year; it is celebrated on the 28th in other years.
        if (day > DateTime.DaysInMonth(year ?? 2000, month)) return false;
        return year is null || (year >= 1900 && year <= DateTime.UtcNow.Year);
    }

    /// <summary>
    /// Reads "1990-03-14", "--03-14", "14-03-1990", "14/3", "14 maart 1990", "March 14", and similar. Numeric
    /// day/month orders are read day first, as in Dutch and most of Europe, unless the first number cannot be a day.
    /// </summary>
    public static Birthday? Parse(string? text)
    {
        var value = PeopleRules.Clean(text)?.ToLowerInvariant().Replace(",", " ").Replace(".", " ");
        if (value is null) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var iso))
            return Create(iso.Month, iso.Day, iso.Year);
        if (value.StartsWith("--", StringComparison.Ordinal)) value = value[2..];

        var parts = value.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>();
        int? month = null;
        foreach (var part in parts)
        {
            if (int.TryParse(StripOrdinal(part), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                numbers.Add(number);
            else if (MonthNames.TryGetValue(part, out var named)) month = named;
            else if (part is not ("of" or "van" or "de" or "the")) return null;
        }

        if (month is { } namedMonth)
        {
            return numbers.Count switch
            {
                1 => Create(namedMonth, numbers[0], null),
                2 when numbers[1] > 31 => Create(namedMonth, numbers[0], numbers[1]),
                2 when numbers[0] > 31 => Create(namedMonth, numbers[1], numbers[0]),
                _ => null
            };
        }

        return numbers.Count switch
        {
            2 when numbers[0] > 31 => null,
            2 => numbers[0] > 12 || numbers[1] <= 12 ? Create(numbers[1], numbers[0], null) : Create(numbers[0], numbers[1], null),
            3 when numbers[0] > 31 => Create(numbers[1], numbers[2], numbers[0]),
            3 => numbers[0] > 12 || numbers[1] <= 12
                ? Create(numbers[1], numbers[0], numbers[2])
                : Create(numbers[0], numbers[1], numbers[2]),
            _ => null
        };
    }

    // "14th", "1st", "14e", "1ste".
    private static string StripOrdinal(string part)
    {
        foreach (var suffix in OrdinalSuffixes)
        {
            if (part.Length > suffix.Length && part.EndsWith(suffix, StringComparison.Ordinal) &&
                char.IsAsciiDigit(part[^(suffix.Length + 1)]))
                return part[..^suffix.Length];
        }
        return part;
    }

    private static readonly string[] OrdinalSuffixes = ["ste", "st", "nd", "rd", "th", "de", "e"];

    private static Birthday? Create(int month, int day, int? year) =>
        IsValid(month, day, year) ? new Birthday(month, day, year) : null;

    public override string ToString()
    {
        var name = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month);
        return Year is { } year ? $"{Day} {name} {year}" : $"{Day} {name}";
    }
}

/// <summary>Birthday and keep-in-touch dates in the owner's local calendar.</summary>
public static class PeopleCalendar
{
    /// <summary>The date the birthday falls on in <paramref name="year"/>; 29 February moves to the 28th.</summary>
    public static DateOnly OccurrenceIn(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>The next birthday on or after <paramref name="today"/>; null when the birthday is unknown.</summary>
    public static DateOnly? NextBirthday(Person person, DateOnly today)
    {
        if (person.BirthdayMonth is not { } month || person.BirthdayDay is not { } day) return null;
        var thisYear = OccurrenceIn(today.Year, month, day);
        return thisYear >= today ? thisYear : OccurrenceIn(today.Year + 1, month, day);
    }

    public static int? DaysUntilBirthday(Person person, DateOnly today) =>
        NextBirthday(person, today) is { } next ? next.DayNumber - today.DayNumber : null;

    /// <summary>The age the person turns on <paramref name="birthday"/>, when the birth year is known.</summary>
    public static int? AgeOn(Person person, DateOnly birthday) =>
        person.BirthYear is { } year && birthday.Year > year ? birthday.Year - year : null;

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>Whole days since the owner was last in touch; null when that was never recorded.</summary>
    public static int? DaysSinceContact(Person person, DateOnly today, TimeZoneInfo zone) =>
        person.LastContactedAt is { } at ? Math.Max(0, today.DayNumber - LocalDate(at, zone).DayNumber) : null;

    /// <summary>
    /// The day the owner wants to be in touch again: the last contact (or the day the person was added) plus the
    /// cadence. Null when no cadence is set.
    /// </summary>
    public static DateOnly? ContactDueOn(Person person, TimeZoneInfo zone)
    {
        if (person.ContactEveryDays is not { } every) return null;
        return LocalDate(person.LastContactedAt ?? person.CreatedAt, zone).AddDays(every);
    }

    public static bool IsContactDue(Person person, DateOnly today, TimeZoneInfo zone) =>
        ContactDueOn(person, zone) is { } due && due <= today;

    /// <summary>"every day", "every week", "every 2 weeks", "every month", or "every 10 days".</summary>
    public static string DescribeCadence(int days) => days switch
    {
        1 => "every day",
        7 => "every week",
        _ when days % 7 == 0 && days <= 56 => $"every {days / 7} weeks",
        30 or 31 => "every month",
        _ when days % 30 == 0 && days <= 330 => $"every {days / 30} months",
        365 => "every year",
        _ => $"every {days} days"
    };

    /// <summary>"today", "yesterday", "3 days ago", "5 weeks ago".</summary>
    public static string DescribeDaysAgo(int days) => days switch
    {
        0 => "today",
        1 => "yesterday",
        < 14 => $"{days} days ago",
        < 60 => $"{days / 7} weeks ago",
        < 730 => $"{days / 30} months ago",
        _ => $"{days / 365} years ago"
    };
}

/// <summary>What the owner can set on a person. Null birthday fields clear the birthday.</summary>
public sealed record PersonInput(
    string? Name,
    string? Relationship,
    int? BirthdayMonth,
    int? BirthdayDay,
    int? BirthYear,
    string? Notes,
    int? ContactEveryDays,
    DateTimeOffset? LastContactedAt = null,
    Guid? GraphEntityId = null);

public enum PeopleFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>Outcome of a change to a person: the value, or why it failed and which field caused it.</summary>
public sealed record PeopleOperation<T>(T? Value, PeopleFailure Failure = PeopleFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == PeopleFailure.None;

    public static PeopleOperation<T> Ok(T value) => new(value);
    public static PeopleOperation<T> NotFound() => new(default, PeopleFailure.NotFound);

    public static PeopleOperation<T> Invalid(string field, string message) =>
        new(default, PeopleFailure.Invalid, field, message);

    public static PeopleOperation<T> Conflict(string field, string message) =>
        new(default, PeopleFailure.Conflict, field, message);
}

/// <summary>
/// A person the knowledge graph learned about who is not on the owner's people list yet, with the relationship and
/// birthday the graph knows, if any.
/// </summary>
public sealed record PersonSuggestion(Guid GraphEntityId, string Name, string? Relationship, Birthday? Birthday,
    string? Summary);

/// <summary>A current fact from the knowledge graph about a linked person.</summary>
public sealed record PersonFact(string Predicate, string Value);

public interface IPeopleRepository
{
    /// <summary>The owner's people, sorted by name.</summary>
    Task<IReadOnlyList<Person>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<Person?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(Person person, CancellationToken cancellationToken);

    /// <summary>Saves every field of <paramref name="person"/>; false when it no longer exists.</summary>
    Task<bool> UpdateAsync(Person person, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Owners with at least one person who has a birthday or a keep-in-touch cadence.</summary>
    Task<IReadOnlyList<Guid>> ListOwnersWithCheckInsAsync(CancellationToken cancellationToken);

    /// <summary>Records that the check-in notified about these people, so they are not notified twice.</summary>
    Task MarkNotifiedAsync(Guid ownerId, IReadOnlyCollection<Guid> birthdayIds, int birthdayYear,
        IReadOnlyCollection<Guid> nudgedIds, DateTimeOffset nudgedAt, CancellationToken cancellationToken);
}

public interface IPeopleService
{
    Task<IReadOnlyList<Person>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Person?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a person by a spoken or typed name: the full name, a relationship ("mama"), or a unique first name.
    /// </summary>
    Task<Person?> FindAsync(Guid ownerId, string name, CancellationToken cancellationToken);

    Task<PeopleOperation<Person>> CreateAsync(Guid ownerId, PersonInput input, CancellationToken cancellationToken);

    Task<PeopleOperation<Person>> UpdateAsync(Guid id, Guid ownerId, PersonInput input,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Records that the owner was in touch, now or at <paramref name="at"/>.</summary>
    Task<PeopleOperation<Person>> LogContactAsync(Guid id, Guid ownerId, DateTimeOffset? at,
        CancellationToken cancellationToken);

    /// <summary>People Jarvis learned about in memory who are not on the list yet.</summary>
    Task<IReadOnlyList<PersonSuggestion>> SuggestAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Current knowledge-graph facts about a linked person; empty when the person is not linked.</summary>
    Task<IReadOnlyList<PersonFact>> GetFactsAsync(Person person, CancellationToken cancellationToken);

    /// <summary>The owner's time zone for birthdays and check-ins (the daily briefing zone, UTC otherwise).</summary>
    Task<TimeZoneInfo> GetTimeZoneAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>Starts the owner's daily people check-in workflow; starting it again is a no-op.</summary>
public interface IPeopleCheckInScheduler
{
    Task SchedulePeopleCheckInAsync(Guid ownerId, CancellationToken cancellationToken);
}

public static class PeopleCheckInWorkflowIds
{
    public static string For(Guid ownerId) => $"jarvis:people:{ownerId:N}";
}

public sealed record PeopleCheckInInput(Guid OwnerId);

/// <summary>
/// Result of one check-in pass: whether the workflow should keep running and when to look again.
/// </summary>
public sealed record PeopleCheckInResult(bool Continue, DateTimeOffset NextRunAt, int BirthdaysNotified,
    int CheckInsNotified);

public interface IPeopleCheckInService
{
    Task<PeopleCheckInResult> RunAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>Maps knowledge-graph predicates to people fields.</summary>
public static class PeopleGraph
{
    public static readonly IReadOnlySet<string> BirthdayPredicates = new HashSet<string>(StringComparer.Ordinal)
        { "birthday", "born_on", "date_of_birth", "birth_date", "birthdate", "verjaardag", "geboren_op" };

    /// <summary>
    /// A readable relationship from a graph predicate between the user and a person: "Anna sister_of user" gives
    /// "Sister", "user has_mother Mama" gives "Mother". Null for predicates that are not family or social ties.
    /// </summary>
    public static string? Relationship(string predicate, bool personIsSubject)
    {
        var words = predicate.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0) return null;
        if (personIsSubject)
        {
            // "sister_of", "is_sister_of", "best_friend_of".
            if (words[0] == "is") words.RemoveAt(0);
            if (words.Count < 2 || words[^1] != "of") return null;
            words.RemoveAt(words.Count - 1);
        }
        else
        {
            // "has_sister", "has_best_friend".
            if (words.Count < 2 || words[0] != "has") return null;
            words.RemoveAt(0);
        }
        var label = string.Join(' ', words);
        if (!KnownTies.Contains(label)) return null;
        return char.ToUpperInvariant(label[0]) + label[1..];
    }

    private static readonly HashSet<string> KnownTies = new(StringComparer.Ordinal)
    {
        "mother", "father", "parent", "sister", "brother", "sibling", "son", "daughter", "child", "grandmother",
        "grandfather", "grandparent", "grandson", "granddaughter", "aunt", "uncle", "cousin", "niece", "nephew",
        "wife", "husband", "partner", "girlfriend", "boyfriend", "spouse", "fiance", "fiancee", "friend",
        "best friend", "colleague", "coworker", "manager", "boss", "neighbor", "neighbour", "roommate",
        "mother in law", "father in law", "sister in law", "brother in law", "stepmother", "stepfather",
        "godmother", "godfather", "mentor", "classmate", "teammate", "doctor", "dentist"
    };

    public static bool IsPerson(GraphEntityRecord entity) =>
        entity.Type == "person" && GraphNames.Key(entity.Name) != GraphNames.UserKey;
}
