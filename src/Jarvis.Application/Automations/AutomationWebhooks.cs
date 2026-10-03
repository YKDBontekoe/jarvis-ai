using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.Application.Automations;

/// <summary>
/// A URL the owner can call from another service to start event automations. The token is shown once; only its
/// SHA-256 hash is stored, with the last characters as a hint.
/// </summary>
public sealed record AutomationWebhookRecord(
    Guid Id,
    Guid OwnerId,
    string Name,
    string TokenHash,
    string TokenHint,
    int UseCount,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);

public interface IAutomationWebhookRepository
{
    Task<IReadOnlyList<AutomationWebhookRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(AutomationWebhookRecord webhook, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>The webhook whose token hashes to <paramref name="tokenHash"/>, across all owners.</summary>
    Task<AutomationWebhookRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task RecordUseAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken);
}

public sealed record CreatedWebhook(AutomationWebhookRecord Webhook, string Token);

public interface IAutomationWebhookService
{
    Task<IReadOnlyList<AutomationWebhookRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<(CreatedWebhook? Created, string? Error)> CreateAsync(Guid ownerId, string? name,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <returns>How many runs started, or null when the token is unknown.</returns>
    Task<int?> IngestAsync(string token, string body, CancellationToken cancellationToken);
}

public sealed class AutomationWebhookService(IAutomationWebhookRepository repository, IAutomationEventBus bus,
    TimeProvider? timeProvider = null) : IAutomationWebhookService
{
    public const string TokenPrefix = "jwh_";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<AutomationWebhookRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        repository.ListAsync(ownerId, cancellationToken);

    public async Task<(CreatedWebhook? Created, string? Error)> CreateAsync(Guid ownerId, string? name,
        CancellationToken cancellationToken)
    {
        var clean = AutomationEvent.Clip(name, 60);
        if (clean is null) return (null, "Give the webhook a name.");
        if ((await repository.ListAsync(ownerId, cancellationToken)).Count >= AutomationEventLimits.MaxWebhooksPerOwner)
            return (null, $"You can have at most {AutomationEventLimits.MaxWebhooksPerOwner} webhooks.");

        var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(24));
        var record = new AutomationWebhookRecord(Guid.CreateVersion7(), ownerId, clean, Hash(token), token[^4..], 0,
            null, clock.GetUtcNow());
        await repository.AddAsync(record, cancellationToken);
        return (new CreatedWebhook(record, token), null);
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<int?> IngestAsync(string token, string body, CancellationToken cancellationToken)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) || token.Length > 80) return null;
        var webhook = await repository.FindByHashAsync(Hash(token), cancellationToken);
        if (webhook is null) return null;
        await repository.RecordUseAsync(webhook.Id, clock.GetUtcNow(), cancellationToken);
        var ev = ToEvent(webhook.Name, body, clock.GetUtcNow());
        return await bus.PublishAsync(webhook.OwnerId, ev, cancellationToken);
    }

    /// <summary>
    /// Turns a request body into an event: a JSON object's title/subject/message becomes the title and its
    /// description/body/text the detail; anything else is used as plain text.
    /// </summary>
    public static AutomationEvent ToEvent(string webhookName, string body, DateTimeOffset at)
    {
        string? title = null, detail = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                title = FirstString(document.RootElement, "title", "subject", "event", "name", "message", "text");
                detail = FirstString(document.RootElement, "detail", "description", "body", "text", "message");
                if (detail == title) detail = null;
                if (title is null && detail is null) detail = document.RootElement.GetRawText();
            }
        }
        catch (JsonException)
        {
        }
        if (title is null && detail is null)
        {
            var clean = AutomationEvent.Clip(body, AutomationEventLimits.MaxDetailLength) ?? "Webhook called";
            title = clean.Length > 80 ? clean[..80] : clean;
            detail = clean.Length > 80 ? clean : null;
        }
        return new AutomationEvent(AutomationEventKinds.Webhook, title ?? "Webhook called", detail, webhookName,
            null, at).Normalize();
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();
        return null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
