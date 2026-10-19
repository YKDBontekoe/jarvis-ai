using Jarvis.Application.Preferences;
using Jarvis.Application.Settings;
using Jarvis.Application.Trips;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OwnerLocaleTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fc61");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fc62");

    [Fact]
    public async Task Nothing_is_guessed_until_the_owner_chooses()
    {
        var view = await new OwnerLocaleService(new InMemorySettingsStore()).GetAsync(Owner, default);

        Assert.Null(view.Language);
        Assert.Null(view.BaseCurrency);
        Assert.Contains("nothing is converted", view.Explanation);
    }

    [Fact]
    public async Task Language_and_currency_are_validated_saved_and_cleared_separately()
    {
        var service = new OwnerLocaleService(new InMemorySettingsStore());

        Assert.Equal("language", (await service.SetAsync(Owner, "klingon", null, default)).Field);
        Assert.Equal("currency", (await service.SetAsync(Owner, null, "XYZ", default)).Field);
        var saved = (await service.SetAsync(Owner, "NL", "chf", default)).View!;
        Assert.Equal("nl", saved.Language);
        Assert.Equal("Nederlands", saved.LanguageName);
        Assert.Equal("CHF", saved.BaseCurrency);

        var onlyCurrency = (await service.SetAsync(Owner, null, "", default)).View!;
        Assert.Equal("nl", onlyCurrency.Language);
        Assert.Null(onlyCurrency.BaseCurrency);
        Assert.Null((await service.SetAsync(Owner, "", null, default)).View!.Language);
    }

    [Fact]
    public async Task Choices_are_private_to_each_owner()
    {
        var service = new OwnerLocaleService(new InMemorySettingsStore());
        await service.SetAsync(Owner, "nl", "EUR", default);

        Assert.Null((await service.GetAsync(Other, default)).Language);
    }

    [Fact]
    public void A_profile_language_wins_and_a_message_in_another_language_is_still_answered_in_it()
    {
        Assert.Null(LocaleRules.ReplyLanguageNote("German", "nl"));
        var note = LocaleRules.ReplyLanguageNote(null, "nl")!;
        Assert.Contains("Nederlands", note);
        Assert.Contains("unless the user writes in another language", note);
        Assert.Null(LocaleRules.ReplyLanguageNote(null, null));
        Assert.Null(LocaleRules.ReplyLanguageNote("  ", "xx"));
        Assert.NotNull(LocaleRules.ReplyLanguageNote("  ", "en"));
    }

    [Fact]
    public async Task A_new_trip_uses_the_base_currency_unless_it_names_one()
    {
        var store = new InMemorySettingsStore();
        var locale = new OwnerLocaleService(store);
        var trips = new TripService(store, null, null, locale);
        TripInput Input(string? currency) => new("Lisbon", "Lisbon", null, new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 4), 2,
            true, 900, currency, null, null);

        Assert.Equal("EUR", (await trips.CreateAsync(Owner, Input(null), default)).Value!.Trip.Currency);
        await locale.SetAsync(Owner, null, "SEK", default);
        Assert.Equal("SEK", (await trips.CreateAsync(Owner, Input(null), default)).Value!.Trip.Currency);
        Assert.Equal("USD", (await trips.CreateAsync(Owner, Input("usd"), default)).Value!.Trip.Currency);
    }
}
