using System.Globalization;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Trips;

public static class TripKinds
{
    public const string Transport = "transport";
    public const string Stay = "stay";
    public const string Activity = "activity";
    public static readonly string[] OptionKinds = [Transport, Stay, Activity];

    public const string Travel = "travel";
    public const string Meal = "meal";
    public static readonly string[] ItemKinds = [Travel, Stay, Activity, Meal];
}

public static class Availability
{
    public const string Unknown = "unknown";
    public const string Available = "available";
    public const string SoldOut = "sold_out";
    public static readonly string[] All = [Unknown, Available, SoldOut];
}

/// <summary>A transport, stay or activity under consideration. A price is only a fact with a quote date and source.</summary>
public sealed record TripOption(
    string Id,
    string Kind,
    string Title,
    decimal? Price,
    string? Currency,
    DateOnly? QuotedOn,
    DateOnly? ForStart,
    DateOnly? ForEnd,
    string? SourceUrl,
    string? Cancellation,
    DateOnly? CancelBy,
    string Availability,
    string? Notes,
    bool Chosen);

public sealed record TripItem(
    string Id,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string Title,
    string Kind,
    string? Place,
    int TravelMinutesBefore,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    bool Verified,
    string? SourceUrl);

public sealed record Trip(
    Guid Id,
    string Title,
    string Destination,
    string? Origin,
    DateOnly Start,
    DateOnly End,
    int Travelers,
    bool Abroad,
    decimal? Budget,
    string Currency,
    string? Preferences,
    string? Constraints,
    IReadOnlyList<TripOption> Options,
    IReadOnlyList<TripItem> Items,
    IReadOnlyList<string> ChecklistDone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Owner settings section <see cref="Section"/>: the owner's trips.</summary>
public sealed record TripState(IReadOnlyList<Trip>? Trips = null)
{
    public const string Section = "trips";
    public IReadOnlyList<Trip> Items => Trips ?? [];
}

public static class TripRules
{
    public const int MaxTrips = 10;
    public const int MaxOptions = 12;
    public const int MaxItems = 80;
    public const int MaxDays = 60;
    public const int MaxText = 300;
    public const int QuoteFreshDays = 7;
    public const int MinBufferMinutes = 15;
    public const int MinFreeMinutesPerDay = 90;
    public static readonly TimeOnly DayStarts = new(9, 0);
    public static readonly TimeOnly DayEnds = new(21, 0);

    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public static string? Clean(string? value, int max = MaxText)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    public static string? NormalizeCurrency(string? value)
    {
        var upper = value?.Trim().ToUpperInvariant();
        return upper is not null && CurrencyPattern.IsMatch(upper) ? upper : null;
    }

    public static string? CleanUrl(string? value)
    {
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text) && text.Length <= 500 &&
               Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;
    }

    public static string Money(decimal amount, string currency) =>
        $"{currency} {amount.ToString("0.00", CultureInfo.InvariantCulture)}";
}

public sealed record TripInput(
    string? Title,
    string? Destination,
    string? Origin,
    DateOnly? Start,
    DateOnly? End,
    int? Travelers,
    bool? Abroad,
    decimal? Budget,
    string? Currency,
    string? Preferences,
    string? Constraints);

public sealed record OptionInput(
    string? Kind,
    string? Title,
    decimal? Price,
    string? Currency,
    DateOnly? QuotedOn,
    DateOnly? ForStart,
    DateOnly? ForEnd,
    string? SourceUrl,
    string? Cancellation,
    DateOnly? CancelBy,
    string? Availability,
    string? Notes);

public sealed record ItemInput(
    DateOnly? Date,
    TimeOnly? Start,
    TimeOnly? End,
    string? Title,
    string? Kind,
    string? Place,
    int? TravelMinutesBefore,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    bool? Verified,
    string? SourceUrl);

public enum TripFailure { None, NotFound, Invalid }

public sealed record TripOperation<T>(T? Value, TripFailure Failure = TripFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == TripFailure.None;
    public static TripOperation<T> Ok(T value) => new(value);
    public static TripOperation<T> NotFound() => new(default, TripFailure.NotFound);
    public static TripOperation<T> Invalid(string field, string message) => new(default, TripFailure.Invalid, field, message);
}
