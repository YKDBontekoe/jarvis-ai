using Jarvis.Application.Conversations;
using Jarvis.Application.Preferences;

namespace Jarvis.Api.Endpoints;

public sealed record HomeZoneRequest(string? TimeZoneId);

public sealed record LocaleRequest(string? Language, string? Currency);

/// <summary>
/// The owner's own preferences. The home time zone is what the owner chose here; a device's zone is only ever offered,
/// never trusted to change anything. Changing it moves nothing that is already scheduled.
/// </summary>
internal static class PreferenceEndpoints
{
    public static RouteGroupBuilder MapPreferenceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/preferences").WithTags("Preferences");

        group.MapGet("", async (IOwnerPreferenceService preferences, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await preferences.GetAsync(user.OwnerId, ct))).WithName("GetPreferences");

        group.MapPut("/home-zone", async (HomeZoneRequest request, IOwnerPreferenceService preferences,
            ICurrentUser user, CancellationToken ct) =>
        {
            var result = await preferences.SetHomeZoneAsync(user.OwnerId, request.TimeZoneId, ct);
            return result.Succeeded
                ? Results.Ok(result.View)
                : EndpointHelpers.Invalid(result.Field ?? "timeZoneId", result.Message ?? "The request is invalid.");
        }).WithName("SetHomeZone");

        group.MapPost("/home-zone/preview", async (HomeZoneRequest request, IOwnerPreferenceService preferences,
            ICurrentUser user, CancellationToken ct) =>
            await preferences.PreviewAsync(user.OwnerId, request.TimeZoneId, ct) is { } preview
                ? Results.Ok(preview)
                : EndpointHelpers.Invalid("timeZoneId", "That is not a time zone Jarvis knows."))
            .WithName("PreviewHomeZone");

        group.MapGet("/locale", async (IOwnerLocaleService locale, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await locale.GetAsync(user.OwnerId, ct))).WithName("GetLocale");

        group.MapPut("/locale", async (LocaleRequest request, IOwnerLocaleService locale, ICurrentUser user,
            CancellationToken ct) =>
        {
            var (view, field, error) = await locale.SetAsync(user.OwnerId, request.Language, request.Currency, ct);
            return view is not null ? Results.Ok(view) : EndpointHelpers.Invalid(field ?? "locale", error ?? "The request is invalid.");
        }).WithName("SetLocale");

        return api;
    }
}
