using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;

namespace Jarvis.Api.Endpoints;

public sealed record ModelSettingsDto(string Provider, string? ChatModel, string? FastModel, string? ReasoningModel,
    string? EmbeddingModel, bool OpenRouterKeyConfigured, IReadOnlyList<string> Providers);
public sealed record SaveModelSettingsRequest(string? Provider, string? ChatModel, string? FastModel,
    string? ReasoningModel, string? EmbeddingModel);
public sealed record CodexModelDto(string Id, string Model, string DisplayName, string? Description, bool IsDefault,
    bool Hidden, bool SupportsImages, IReadOnlyList<string> InputModalities);
public sealed record CodexInstallationDto(string? InstalledVersion, string? LatestVersion, bool UpdateAvailable,
    bool CanUpdate, bool UsingManagedInstall, string? UpdateBlockedReason, string? Error,
    IReadOnlyList<CodexModelDto> Models);

internal static class ModelSettingsEndpoints
{
    public static RouteGroupBuilder MapModelSettingsEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var models = api.MapGroup("/settings/models");

        models.MapGet("", async (IOwnerSettingsStore settings, IIntegrationCredentialStore credentials,
                ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await ToDtoAsync(currentUser.OwnerId, settings, credentials, ct)))
            .WithName("GetModelSettings");

        models.MapPut("", async (SaveModelSettingsRequest request, IOwnerSettingsStore settings,
            IIntegrationCredentialStore credentials, IAuditEventStore audit, ICurrentUser currentUser,
            CodexInstallation codex, CancellationToken ct) =>
        {
            ModelSettings normalized;
            try
            {
                normalized = new ModelSettings(request.Provider ?? ModelSettings.Codex, request.ChatModel,
                    request.FastModel, request.ReasoningModel, request.EmbeddingModel).Normalize();
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("provider", exception.Message);
            }
            if (normalized.Provider == ModelSettings.Codex)
            {
                var catalog = await codex.GetStatusAsync(ct);
                if (catalog.Error is null)
                {
                    if (normalized.ChatModel is not null && !catalog.SupportsModel(normalized.ChatModel))
                        return EndpointHelpers.Invalid("chatModel",
                            $"The installed Codex CLI does not support '{normalized.ChatModel}'. Choose a listed model or update Codex.");
                    if (normalized.ReasoningModel is not null && !catalog.SupportsModel(normalized.ReasoningModel))
                        return EndpointHelpers.Invalid("reasoningModel",
                            $"The installed Codex CLI does not support '{normalized.ReasoningModel}'. Choose a listed model or update Codex.");
                }
            }
            if (normalized.UsesOpenRouter && !await HasOpenRouterKeyAsync(currentUser.OwnerId, credentials, ct))
                return EndpointHelpers.Invalid("provider", "Save an OpenRouter API key before selecting OpenRouter.");
            if (normalized.UsesOpenRouterEmbeddings && !await HasOpenRouterKeyAsync(currentUser.OwnerId, credentials, ct))
                return EndpointHelpers.Invalid("embeddingModel",
                    "Save an OpenRouter API key before choosing an embedding model.");
            await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Models, normalized, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "settings",
                "models.updated", "moderate", true, null,
                JsonSerializer.Serialize(new { provider = normalized.Provider, chatModel = normalized.ChatModel }), ct);
            return Results.Ok(await ToDtoAsync(currentUser.OwnerId, settings, credentials, ct));
        }).WithName("SaveModelSettings");

        models.MapPut("/openrouter-key", async (SaveIntegrationSecretRequest request,
            IIntegrationCredentialStore credentials, IOwnerSettingsStore settings, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var value = request.Value?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 400 || value.Any(char.IsWhiteSpace))
                return EndpointHelpers.Invalid("value", "Paste the OpenRouter API key without spaces.");
            await credentials.SaveSecretAsync(currentUser.OwnerId, IntegrationCredentialProviders.OpenRouter,
                ModelSettings.OpenRouterKeySecret, value, ct);
            return Results.Ok(await ToDtoAsync(currentUser.OwnerId, settings, credentials, ct));
        }).WithName("SaveOpenRouterKey");

        models.MapDelete("/openrouter-key", async (IIntegrationCredentialStore credentials,
            IOwnerSettingsStore settings, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await credentials.DeleteAsync(currentUser.OwnerId, IntegrationCredentialProviders.OpenRouter, ct);
            var current = await settings.GetAsync<ModelSettings>(currentUser.OwnerId, SettingsSections.Models, ct);
            if (current is not null)
            {
                var updated = current.UsesOpenRouter
                    ? current with
                    {
                        Provider = ModelSettings.Codex,
                        ChatModel = null,
                        FastModel = null,
                        ReasoningModel = null
                    }
                    : current;
                if (updated.UsesOpenRouterEmbeddings)
                    updated = updated with { EmbeddingModel = null };
                if (updated != current)
                    await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Models, updated, ct);
            }
            return Results.Ok(await ToDtoAsync(currentUser.OwnerId, settings, credentials, ct));
        }).WithName("DeleteOpenRouterKey");

        models.MapGet("/codex", async (CodexInstallation codex, CancellationToken ct) =>
            Results.Ok(ToCodexDto(await codex.GetStatusAsync(ct))))
            .WithName("GetCodexInstallation");

        models.MapPost("/codex/update", async (CodexInstallation codex, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                var status = await codex.UpdateAsync(ct);
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "settings",
                    "codex.updated", "moderate", true, null,
                    JsonSerializer.Serialize(new { version = status.InstalledVersion }), ct);
                return Results.Ok(ToCodexDto(status));
            }
            catch (InvalidOperationException exception)
            {
                return EndpointHelpers.Invalid("codex", exception.Message);
            }
        }).WithName("UpdateCodex");

        models.MapGet("/openrouter/catalog", async (string? search, OpenRouterCatalog catalog, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await catalog.ListAsync(search, ct));
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Could not load the OpenRouter model catalog.");
                return Results.Problem("OpenRouter's model list is temporarily unavailable.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).WithName("ListOpenRouterModels");

        models.MapPost("/test", async (IChatClientResolver resolver, IOwnerSettingsStore settings,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var current = await settings.GetAsync<ModelSettings>(currentUser.OwnerId, SettingsSections.Models, ct)
                          ?? ModelSettings.Default;
            var client = await resolver.GetChatClientAsync(currentUser.OwnerId, ModelPurpose.Chat, ct);
            return Results.Ok(await OpenRouterCatalog.TestAsync(client, current.Provider, current.ChatModel, ct));
        }).WithName("TestModelSettings");

        return api;
    }

    private static async Task<ModelSettingsDto> ToDtoAsync(Guid ownerId, IOwnerSettingsStore settings,
        IIntegrationCredentialStore credentials, CancellationToken ct)
    {
        var current = await settings.GetAsync<ModelSettings>(ownerId, SettingsSections.Models, ct) ?? ModelSettings.Default;
        return new ModelSettingsDto(current.Provider, current.ChatModel, current.FastModel, current.ReasoningModel,
            current.EmbeddingModel,
            await HasOpenRouterKeyAsync(ownerId, credentials, ct), ModelSettings.Providers);
    }

    private static CodexInstallationDto ToCodexDto(CodexInstallationStatus status) =>
        new(status.InstalledVersion, status.LatestVersion, status.UpdateAvailable, status.CanUpdate,
            status.UsingManagedInstall, status.UpdateBlockedReason, status.Error,
            status.Models.Select(model => new CodexModelDto(model.Id, model.Model, model.DisplayName, model.Description,
                model.IsDefault, model.Hidden, model.SupportsImages, model.InputModalities)).ToArray());

    private static async Task<bool> HasOpenRouterKeyAsync(Guid ownerId, IIntegrationCredentialStore credentials,
        CancellationToken ct) =>
        (await credentials.GetStatusAsync(ownerId, IntegrationCredentialProviders.OpenRouter, ct))
        ?.SecretNames.Contains(ModelSettings.OpenRouterKeySecret) == true;
}
