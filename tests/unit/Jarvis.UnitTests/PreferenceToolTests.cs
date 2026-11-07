using Jarvis.Agents.Preferences;
using Jarvis.Application.Conversations;
using Jarvis.Application.Preferences;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PreferenceToolTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class Preferences : IOwnerPreferenceService
    {
        public (Guid Owner, string? Zone)? Saved;

        public Task<PreferencesView> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesView(Saved?.Zone ?? "UTC", "chosen", Saved is not null, "2026-11-07 10:00", null));

        public Task<PreferenceOperation> SetHomeZoneAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken)
        {
            if (ZonePolicy.Validate(timeZoneId) is not { } id)
                return Task.FromResult(new PreferenceOperation(false, null, "timeZoneId", "That is not a time zone Jarvis knows."));
            Saved = (ownerId, id);
            return Task.FromResult(new PreferenceOperation(true, new PreferencesView(id, "chosen", true, "2026-11-07 10:00", null)));
        }

        public Task<ZonePreview?> PreviewAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken) =>
            Task.FromResult<ZonePreview?>(null);
    }

    [Fact]
    public async Task Telling_the_assistant_where_you_live_saves_the_zone_for_that_owner()
    {
        var store = new Preferences();
        var tools = new PreferenceAgentTools(store, new User());

        var reply = await tools.SetHomeTimeZoneAsync(" Europe/Lisbon ");

        Assert.Contains("Europe/Lisbon", reply, StringComparison.Ordinal);
        Assert.Equal((Owner, "Europe/Lisbon"), store.Saved);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_zone_that_does_not_exist_or_is_missing_saves_nothing(string zone)
    {
        var store = new Preferences();
        var tools = new PreferenceAgentTools(store, new User());

        var reply = await tools.SetHomeTimeZoneAsync(zone);

        Assert.Null(store.Saved);
        Assert.True(reply.StartsWith("I could not set it", StringComparison.Ordinal) || reply.StartsWith("Which time zone", StringComparison.Ordinal), reply);
    }
}
