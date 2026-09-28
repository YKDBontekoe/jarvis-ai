using System.Text.Json;
using Jarvis.Application.Learning;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Application.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ModelUsageEventEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Provider { get; set; } = UsageProviders.Codex;
    public string Purpose { get; set; } = UsagePurposes.Chat;
    public string? Model { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long ReasoningOutputTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int DurationMs { get; set; }
    public string Outcome { get; set; } = UsageOutcomes.Completed;
    public int WebSearchActions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModelUsageRecorder(IServiceScopeFactory scopes, ILogger<ModelUsageRecorder> logger) : IModelUsageRecorder
{
    public async Task RecordAsync(ModelUsageDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
            db.ModelUsageEvents.Add(new ModelUsageEventEntity
            {
                Id = Guid.CreateVersion7(),
                OwnerId = draft.OwnerId,
                Provider = Clean(draft.Provider, 20, UsageProviders.Codex),
                Purpose = Clean(draft.Purpose, 20, UsagePurposes.Chat),
                Model = string.IsNullOrWhiteSpace(draft.Model) ? null : Trim(draft.Model, 200),
                InputTokens = Math.Max(0, draft.InputTokens),
                OutputTokens = Math.Max(0, draft.OutputTokens),
                CachedInputTokens = Math.Max(0, draft.CachedInputTokens),
                ReasoningOutputTokens = Math.Max(0, draft.ReasoningOutputTokens),
                EstimatedCostUsd = draft.EstimatedCostUsd is { } cost && cost >= 0
                    ? decimal.Round(cost, 8, MidpointRounding.AwayFromZero) : null,
                DurationMs = Math.Max(0, draft.DurationMs),
                Outcome = draft.Outcome is UsageOutcomes.Completed or UsageOutcomes.Failed
                    or UsageOutcomes.Cancelled or UsageOutcomes.Timeout
                    ? draft.Outcome : UsageOutcomes.Failed,
                WebSearchActions = Math.Max(0, draft.WebSearchActions),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record model usage for {Provider}.", draft.Provider);
        }
    }

    private static string Clean(string? value, int max, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : Trim(value, max);

    private static string Trim(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public sealed class UsageDashboardService(JarvisDbContext db) : IUsageDashboard
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UsageDashboard> GetAsync(Guid ownerId, string period, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var timeZoneId = await db.DailyBriefings.AsNoTracking()
            .Where(briefing => briefing.OwnerId == ownerId)
            .Select(briefing => briefing.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";
        var zone = UsagePeriods.ResolveZone(timeZoneId);
        var from = UsagePeriods.Start(period, now, zone);

        var modelRows = await db.ModelUsageEvents.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.CreatedAt >= from && item.CreatedAt <= now)
            .GroupBy(item => new { item.Provider, item.Purpose, Model = item.Model ?? "" })
            .Select(group => new
            {
                group.Key.Provider,
                group.Key.Purpose,
                group.Key.Model,
                Calls = group.Count(),
                Completed = group.Count(item => item.Outcome == UsageOutcomes.Completed),
                Input = group.Sum(item => item.InputTokens),
                Output = group.Sum(item => item.OutputTokens),
                Cached = group.Sum(item => item.CachedInputTokens),
                Reasoning = group.Sum(item => item.ReasoningOutputTokens),
                Cost = group.Sum(item => item.EstimatedCostUsd ?? 0m),
                Unpriced = group.Count(item => item.EstimatedCostUsd == null && item.InputTokens + item.OutputTokens > 0),
                Searches = group.Sum(item => item.WebSearchActions),
                Duration = group.Sum(item => (long)item.DurationMs)
            })
            .ToListAsync(cancellationToken);
        var models = modelRows.Select(row => new UsageModelAggregate(
            row.Provider, row.Purpose, row.Model, row.Calls, row.Completed, row.Input, row.Output,
            row.Cached, row.Reasoning, row.Cost, row.Unpriced, row.Searches, row.Duration)).ToArray();

        var points = await db.ModelUsageEvents.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.CreatedAt >= from && item.CreatedAt <= now)
            .Select(item => new UsagePoint(
                item.Provider, item.InputTokens + item.OutputTokens, item.EstimatedCostUsd ?? 0m, item.CreatedAt))
            .ToListAsync(cancellationToken);

        var lifetimeRows = await db.ModelUsageEvents.AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .GroupBy(item => item.Provider)
            .Select(group => new
            {
                Provider = group.Key,
                Calls = group.Count(),
                Input = group.Sum(item => item.InputTokens),
                Output = group.Sum(item => item.OutputTokens),
                Cost = group.Sum(item => item.EstimatedCostUsd ?? 0m),
                Unpriced = group.Count(item => item.EstimatedCostUsd == null && item.InputTokens + item.OutputTokens > 0),
                Searches = group.Sum(item => item.WebSearchActions)
            })
            .ToListAsync(cancellationToken);
        var lifetime = lifetimeRows.Select(row => new ProviderLifetime(
            row.Provider, row.Calls, row.Input, row.Output, row.Cost, row.Unpriced, row.Searches)).ToArray();

        var activity = await LoadActivityAsync(ownerId, from, now, cancellationToken);
        var personalization = await LoadPersonalizationAsync(ownerId, now, cancellationToken);
        return UsageDashboardComposer.Compose(new UsageComposeInput(
            now, period, zone.Id, models, points, lifetime, activity, personalization));
    }

    private async Task<PersonalizationSnapshot> LoadPersonalizationAsync(Guid ownerId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var memory = await db.Memories.AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .GroupBy(item => item.OwnerId)
            .Select(group => new
            {
                Active = group.Count(item => item.ValidUntil == null || item.ValidUntil > now),
                Pinned = group.Count(item => item.IsPinned && (item.ValidUntil == null || item.ValidUntil > now)),
                Superseded = group.Count(item => item.ValidUntil != null && item.ValidUntil <= now)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var kinds = await db.Memories.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && (item.ValidUntil == null || item.ValidUntil > now))
            .Select(item => item.Kind)
            .Distinct()
            .CountAsync(cancellationToken);
        var feedback = await db.MessageFeedback.AsNoTracking()
            .CountAsync(item => item.OwnerId == ownerId, cancellationToken);
        var settings = await db.OwnerSettings.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Section == SettingsSections.Persona)
            .Select(item => item.ValueJson)
            .FirstOrDefaultAsync(cancellationToken);
        var persona = Deserialize<PersonaProfile>(settings) ?? PersonaProfile.Empty;
        return PersonalizationLevel.Score(new PersonalizationInputs(
            memory?.Active ?? 0, memory?.Pinned ?? 0, kinds, persona.TraitList.Count,
            !string.IsNullOrWhiteSpace(persona.CustomInstructions),
            !string.IsNullOrWhiteSpace(persona.PreferredName), feedback, memory?.Superseded ?? 0));
    }

    private async Task<UsageActivity> LoadActivityAsync(Guid ownerId, DateTimeOffset from, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var messages = await db.Messages.AsNoTracking()
            .Where(message => db.Conversations.Any(conversation =>
                conversation.Id == message.ConversationId && conversation.OwnerId == ownerId))
            .GroupBy(message => message.Role)
            .Select(group => new
            {
                Role = group.Key,
                Total = group.Count(),
                InPeriod = group.Count(message => message.CreatedAt >= from && message.CreatedAt <= now)
            })
            .ToListAsync(cancellationToken);
        var conversations = await CountAsync(
            db.Conversations.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.UpdatedAt),
            from, now, cancellationToken);
        var dreams = await db.AuditEvents.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Action == "learning.dreamed")
            .GroupBy(item => item.OwnerId)
            .Select(group => new
            {
                Total = group.Count(),
                InPeriod = group.Count(item => item.Timestamp >= from && item.Timestamp <= now),
                Last = group.Max(item => (DateTimeOffset?)item.Timestamp)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var memories = await CountAsync(
            db.Memories.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var skills = await CountAsync(
            db.Skills.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var files = await CountAsync(
            db.Files.AsNoTracking().Where(item => item.OwnerId == ownerId && item.ProcessingStatus != "deleting")
                .Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var entities = await CountAsync(
            db.GraphEntities.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var relations = await CountAsync(
            db.GraphRelations.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.ValidFrom),
            from, now, cancellationToken);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Status == "completed")
            .GroupBy(item => item.OwnerId)
            .Select(group => new
            {
                Total = group.Count(),
                InPeriod = group.Count(item => (item.CompletedAt ?? item.CreatedAt) >= from
                    && (item.CompletedAt ?? item.CreatedAt) <= now)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var reminders = await CountAsync(
            db.Reminders.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var approvals = await CountAsync(
            db.ToolApprovals.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var channels = await CountAsync(
            db.ChannelMessages.AsNoTracking()
                .Where(message => db.ChannelConnections.Any(connection =>
                    connection.Id == message.ConnectionId && connection.OwnerId == ownerId))
                .Select(message => message.CreatedAt),
            from, now, cancellationToken);
        var feedback = await CountAsync(
            db.MessageFeedback.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var browsers = await CountAsync(
            db.BrowserSessions.AsNoTracking().Where(item => item.OwnerId == ownerId).Select(item => item.CreatedAt),
            from, now, cancellationToken);
        var diaryJson = await db.OwnerSettings.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Section == LearningSections.DreamingState)
            .Select(item => item.ValueJson)
            .FirstOrDefaultAsync(cancellationToken);
        var diary = Deserialize<DreamingState>(diaryJson)?.Entries.Count ?? 0;
        CountWindow Role(string role)
        {
            var row = messages.FirstOrDefault(item => item.Role == role);
            return new CountWindow(row?.InPeriod ?? 0, row?.Total ?? 0);
        }

        return new UsageActivity(
            Role("user"), Role("assistant"), conversations,
            new CountWindow(dreams?.InPeriod ?? 0, dreams?.Total ?? 0),
            memories, skills, files, entities, relations,
            new CountWindow(tasks?.InPeriod ?? 0, tasks?.Total ?? 0),
            reminders, approvals, new CountWindow(0, 0),
            channels,
            feedback, browsers, dreams?.Last, diary);
    }

    private static async Task<CountWindow> CountAsync(IQueryable<DateTimeOffset> timestamps, DateTimeOffset from,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var total = await timestamps.CountAsync(cancellationToken);
        var inPeriod = await timestamps.CountAsync(
            timestamp => timestamp >= from && timestamp <= now, cancellationToken);
        return new CountWindow(inPeriod, total);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
