using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;

namespace Jarvis.Api.Endpoints;

public sealed record SaveAssistantProfileRequest(
    string? Name,
    string? Description,
    string? PersonaInstructions,
    string? PreferredName,
    string? ReplyLanguage,
    bool IncludeOwnerPersona = true,
    bool RestrictSkills = false,
    IReadOnlyList<Guid>? EnabledSkillIds = null,
    bool RestrictFiles = false,
    IReadOnlyList<Guid>? AllowedCollectionIds = null,
    string? ModelClass = null,
    string? ChatModel = null,
    string? FastModel = null,
    string? ReasoningEffort = null,
    string? MemoryScope = null,
    bool IncludePinnedMemories = true,
    bool ContributeToLearning = true,
    bool AllowPersonaLearning = true,
    bool AllowRemember = true,
    bool? IsDefault = null);

public sealed record SwitchConversationProfileRequest(Guid ProfileId, bool Confirm = false);

internal static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/profiles");

        group.MapGet("", async (IAssistantProfileService profiles, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await profiles.ListAsync(currentUser.OwnerId, ct)))
            .WithName("ListAssistantProfiles");

        group.MapGet("/{id:guid}", async (Guid id, IAssistantProfileService profiles, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var profile = await profiles.GetAsync(id, currentUser.OwnerId, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        }).WithName("GetAssistantProfile");

        group.MapPost("", async (SaveAssistantProfileRequest request, IAssistantProfileService profiles,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var created = await profiles.CreateAsync(currentUser.OwnerId, ToDraft(request), ct);
                return Results.Created($"/api/v1/profiles/{created.Id}", created);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profile", exception.Message);
            }
        }).WithName("CreateAssistantProfile");

        group.MapPut("/{id:guid}", async (Guid id, SaveAssistantProfileRequest request,
            IAssistantProfileService profiles, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var updated = await profiles.UpdateAsync(id, currentUser.OwnerId, ToDraft(request), ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profile", exception.Message);
            }
        }).WithName("UpdateAssistantProfile");

        group.MapDelete("/{id:guid}", async (Guid id, IAssistantProfileService profiles, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                return await profiles.DeleteAsync(id, currentUser.OwnerId, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profile", exception.Message);
            }
        }).WithName("DeleteAssistantProfile");

        return api;
    }

    public static AssistantProfileDraft ToDraft(SaveAssistantProfileRequest request) => new(
        request.Name ?? "",
        request.Description,
        request.PersonaInstructions,
        request.PreferredName,
        request.ReplyLanguage,
        request.IncludeOwnerPersona,
        request.RestrictSkills,
        request.EnabledSkillIds,
        request.RestrictFiles,
        request.AllowedCollectionIds,
        request.ModelClass,
        request.ChatModel,
        request.FastModel,
        request.ReasoningEffort,
        request.MemoryScope ?? Jarvis.Domain.Profiles.MemoryScopes.All,
        request.IncludePinnedMemories,
        request.ContributeToLearning,
        request.AllowPersonaLearning,
        request.AllowRemember,
        request.IsDefault);
}
