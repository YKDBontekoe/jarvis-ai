using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;

using Jarvis.Api.Errors;

namespace Jarvis.Api.Endpoints;

public sealed record ModelSettingsDto(string Provider, string? ChatModel, string? FastModel, string? ReasoningEffort,
    string? EmbeddingModel, bool OpenRouterKeyConfigured, IReadOnlyList<string> Providers);
/// <summary>Which embedding model serves semantic memory search ("local", "openrouter" or "none") and its indexing progress.</summary>
public sealed record EmbeddingStatusDto(string Source, string? Model, int ActiveMemories, int EmbeddedMemories,
    bool? Reachable = null);
public sealed record SaveModelSettingsRequest(string? Provider, string? ChatModel, string? FastModel,
    string? ReasoningEffort, string? EmbeddingModel);
public sealed record CodexModelDto(string Id, string Model, string DisplayName, string? Description, bool IsDefault,
    bool Hidden, bool SupportsImages, IReadOnlyList<string> InputModalities,
    IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts, string? DefaultReasoningEffort);
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

        models.MapGet("/embedding", async (IChatClientResolver resolver, IMemoryIndexRepository index,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                var model = await resolver.GetEmbeddingModelAsync(currentUser.OwnerId, ct);
                return Results.Ok(ToEmbeddingStatus(model?.Name, await index.GetStatusAsync(currentUser.OwnerId, ct),
                    model is null ? null : await IsReachableAsync(model, ct)));
            }).WithName("GetEmbeddingStatus");

        models.MapPut("", async (SaveModelSettingsRequest request, IOwnerSettingsStore settings,
            IIntegrationCredentialStore credentials, IAuditEventStore audit, ICurrentUser currentUser,
            CodexInstallation codex, CancellationToken ct) =>
        {
            ModelSettings normalized;
            try
            {
                normalized = new ModelSettings(request.Provider ?? ModelSettings.Codex, request.ChatModel,
                    request.FastModel, request.ReasoningEffort, request.EmbeddingModel).Normalize();
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
                    var selectedModel = normalized.ChatModel is null
                        ? catalog.Models.FirstOrDefault(model => model.IsDefault)
                        : catalog.Models.FirstOrDefault(model => model.Model == normalized.ChatModel ||
                            model.Id == normalized.ChatModel);
                    if (normalized.ReasoningEffort is not null && selectedModel is not null &&
                        selectedModel.SupportedReasoningEfforts.All(effort =>
                            !effort.ReasoningEffort.Equals(normalized.ReasoningEffort, StringComparison.OrdinalIgnoreCase)))
                        return EndpointHelpers.Invalid("reasoningEffort",
                            $"The selected Codex model does not support '{normalized.ReasoningEffort}' reasoning effort.");
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
                        ReasoningEffort = null
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

        // ChatGPT sign-in for the server's Codex CLI, so nobody needs a shell on the host. Starting is only possible
        // while Codex is signed out.
        models.MapGet("/codex/sign-in", async (CodexSignIn signIn, CancellationToken ct) =>
            Results.Ok(await signIn.GetStatusAsync(ct)))
            .WithName("GetCodexSignIn");

        models.MapPost("/codex/sign-in", async (CodexSignIn signIn, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                var status = await signIn.StartAsync(ct);
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "settings",
                    "codex.sign_in_started", "moderate", true, null, null, ct);
                return Results.Ok(status);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = "codex_sign_in", message = exception.Message });
            }
        }).WithName("StartCodexSignIn");

        models.MapGet("/openrouter/catalog", async (string? search, OpenRouterCatalog catalog, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await catalog.ListAsync(search, ct));
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Could not load the OpenRouter model catalog.");
                return ApiProblemResults.BadGateway("OpenRouter's model list is temporarily unavailable.");
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

    /// <summary>
    /// "server:" models come from the deployment's own embedding endpoint (the bundled local model), "openrouter:" ones
    /// from the owner's OpenRouter key; the owner's choice wins over the server default.
    /// </summary>
    internal static EmbeddingStatusDto ToEmbeddingStatus(string? modelName, MemoryIndexStatus status,
        bool? reachable = null)
    {
        const string Local = "server:";
        const string OpenRouter = "openrouter:";
        if (modelName is null) return new EmbeddingStatusDto("none", null, status.Active, status.Embedded, reachable);
        return modelName.StartsWith(Local, StringComparison.Ordinal)
            ? new EmbeddingStatusDto("local", modelName[Local.Length..], status.Active, status.Embedded, reachable)
            : new EmbeddingStatusDto("openrouter", modelName.StartsWith(OpenRouter, StringComparison.Ordinal)
                ? modelName[OpenRouter.Length..] : modelName, status.Active, status.Embedded, reachable);
    }

    /// <summary>Embeds one word so Settings can tell "still indexing" from "the embedding server does not answer".</summary>
    private static async Task<bool> IsReachableAsync(EmbeddingModel model, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await model.Generator.GenerateAsync(["ping"], cancellationToken: timeout.Token);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<ModelSettingsDto> ToDtoAsync(Guid ownerId, IOwnerSettingsStore settings,
        IIntegrationCredentialStore credentials, CancellationToken ct)
    {
        var current = await settings.GetAsync<ModelSettings>(ownerId, SettingsSections.Models, ct) ?? ModelSettings.Default;
        return new ModelSettingsDto(current.Provider, current.ChatModel, current.FastModel, current.ReasoningEffort,
            current.EmbeddingModel,
            await HasOpenRouterKeyAsync(ownerId, credentials, ct), ModelSettings.Providers);
    }

    private static CodexInstallationDto ToCodexDto(CodexInstallationStatus status) =>
        new(status.InstalledVersion, status.LatestVersion, status.UpdateAvailable, status.CanUpdate,
            status.UsingManagedInstall, status.UpdateBlockedReason, status.Error,
            status.Models.Select(model => new CodexModelDto(model.Id, model.Model, model.DisplayName, model.Description,
                model.IsDefault, model.Hidden, model.SupportsImages, model.InputModalities,
                model.SupportedReasoningEfforts, model.DefaultReasoningEffort)).ToArray());

    private static async Task<bool> HasOpenRouterKeyAsync(Guid ownerId, IIntegrationCredentialStore credentials,
        CancellationToken ct) =>
        (await credentials.GetStatusAsync(ownerId, IntegrationCredentialProviders.OpenRouter, ct))
        ?.SecretNames.Contains(ModelSettings.OpenRouterKeySecret) == true;
}
