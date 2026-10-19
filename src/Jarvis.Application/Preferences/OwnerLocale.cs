using Jarvis.Application.Settings;

namespace Jarvis.Application.Preferences;

/// <summary>Only what the owner chose. Nothing is guessed from the phone or from the language of a message.</summary>
public sealed record OwnerLocale(string? Language = null, string? BaseCurrency = null);

public sealed record LanguageOption(string Code, string Name);

public sealed record LocaleView(string? Language, string? LanguageName, string? BaseCurrency,
    IReadOnlyList<LanguageOption> Languages, IReadOnlyList<string> Currencies, string Explanation);

public static class LocaleRules
{
    public static readonly IReadOnlyList<LanguageOption> Languages =
        [new("en", "English"), new("nl", "Nederlands"), new("de", "Deutsch"), new("fr", "Français"), new("es", "Español")];

    public static readonly IReadOnlyList<string> Currencies = ["EUR", "USD", "GBP", "CHF", "SEK", "NOK", "DKK", "PLN", "CAD", "AUD"];

    public static string? NormalizeLanguage(string? code) =>
        Languages.FirstOrDefault(l => l.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase))?.Code;

    public static string? NormalizeCurrency(string? code) =>
        Currencies.FirstOrDefault(c => c.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public const string Explanation =
        "Jarvis replies in your language unless you write in another one or the profile in use sets its own. The base currency is the default for new budgets and trips; nothing is converted.";

    /// <summary>
    /// One precedence for reply language: the active profile's own language, then the owner's choice, otherwise none
    /// (Jarvis follows the message). Returns the language name to ask for, or null for no instruction.
    /// </summary>
    public static string? ReplyLanguageNote(string? profileLanguage, string? ownerLanguage)
    {
        if (!string.IsNullOrWhiteSpace(profileLanguage)) return null;
        var name = Languages.FirstOrDefault(l => l.Code == ownerLanguage)?.Name;
        return name is null
            ? null
            : $"The user prefers replies in {name}. Reply in {name} unless the user writes in another language or asks for one.";
    }
}

public interface IOwnerLocaleService
{
    Task<LocaleView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Saves what is given; an empty string clears that choice. Null leaves it as it was.</summary>
    Task<(LocaleView? View, string? Field, string? Error)> SetAsync(Guid ownerId, string? language, string? currency,
        CancellationToken cancellationToken);
}

public sealed class OwnerLocaleService(IOwnerSettingsStore settings) : IOwnerLocaleService
{
    public async Task<LocaleView> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        ToView(await LoadAsync(ownerId, cancellationToken));

    public async Task<(LocaleView? View, string? Field, string? Error)> SetAsync(Guid ownerId, string? language,
        string? currency, CancellationToken cancellationToken)
    {
        var stored = await LoadAsync(ownerId, cancellationToken);
        var nextLanguage = stored.Language;
        var nextCurrency = stored.BaseCurrency;
        if (language is not null)
        {
            if (language.Trim().Length == 0) nextLanguage = null;
            else if (LocaleRules.NormalizeLanguage(language) is { } code) nextLanguage = code;
            else return (null, "language", "Choose one of the listed languages.");
        }
        if (currency is not null)
        {
            if (currency.Trim().Length == 0) nextCurrency = null;
            else if (LocaleRules.NormalizeCurrency(currency) is { } code) nextCurrency = code;
            else return (null, "currency", "Choose one of the listed currencies.");
        }
        var saved = new OwnerLocale(nextLanguage, nextCurrency);
        await settings.SaveAsync(ownerId, SettingsSections.Locale, saved, cancellationToken);
        return (ToView(saved), null, null);
    }

    private async Task<OwnerLocale> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<OwnerLocale>(ownerId, SettingsSections.Locale, cancellationToken) ?? new OwnerLocale();

    private static LocaleView ToView(OwnerLocale locale) => new(locale.Language,
        LocaleRules.Languages.FirstOrDefault(l => l.Code == locale.Language)?.Name, locale.BaseCurrency,
        LocaleRules.Languages, LocaleRules.Currencies, LocaleRules.Explanation);
}
