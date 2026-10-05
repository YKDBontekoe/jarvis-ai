using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Settings;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Routines;

namespace Jarvis.Application.Routines;

public sealed class RoutineSuggestionService(
    IRoutineSuggestionRepository repository,
    IEnumerable<ITimelineSource> sources,
    IAutomationRuleService automations,
    IDailyBriefingRepository briefings,
    INotificationRepository notifications,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null) : IRoutineSuggestionService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<RoutineSuggestionView>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<RoutineState>(ownerId, SettingsSections.Routines, cancellationToken);
        var stale = state?.LastRefreshAt is not { } last || clock.GetUtcNow() - last >= RoutineRules.RefreshInterval;
        if (stale) return await RefreshAsync(ownerId, true, cancellationToken);
        return await PendingAsync(ownerId, cancellationToken);
    }

    public async Task<IReadOnlyList<RoutineSuggestionView>> RefreshAsync(Guid ownerId, bool force,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await settings.GetAsync<RoutineState>(ownerId, SettingsSections.Routines, cancellationToken)
                    ?? new RoutineState(null, null);
        if (!force && state.LastRefreshAt is { } last && now - last < RoutineRules.RefreshInterval)
            return await PendingAsync(ownerId, cancellationToken);

        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        LocalClock.TryFind(zoneId, out var zone);
        var events = await CollectAsync(ownerId, zone, now, cancellationToken);
        var mined = RoutineMinerEngine.Mine(events, zone, now);

        var existing = await repository.ListAsync(ownerId, cancellationToken);
        var byFingerprint = existing.ToDictionary(x => x.Fingerprint, StringComparer.Ordinal);
        var added = new List<RoutineSuggestionEntry>();
        foreach (var suggestion in mined)
        {
            var json = AutomationDefinitionJson.Serialize(suggestion.Definition);
            if (byFingerprint.TryGetValue(suggestion.Fingerprint, out var current))
            {
                // Accepted and dismissed rows are the owner's decisions; only pending ones follow the data.
                if (current.Status == RoutineStatuses.Pending)
                    await repository.UpdateAsync(current with
                    {
                        Title = suggestion.Title, Evidence = suggestion.Evidence, Confidence = suggestion.Confidence,
                        DefinitionJson = json, UpdatedAt = now
                    }, cancellationToken);
                continue;
            }

            added.Add(new RoutineSuggestionEntry(Guid.CreateVersion7(), ownerId, suggestion.Fingerprint,
                suggestion.Title, suggestion.Evidence, suggestion.Confidence, json, RoutineStatuses.Pending, null,
                now, now));
        }

        if (added.Count > 0) await repository.AddRangeAsync(added, cancellationToken);

        // A pattern that no longer shows up is not worth offering any more.
        var seen = mined.Select(x => x.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var gone = existing.Where(x => x.Status == RoutineStatuses.Pending && !seen.Contains(x.Fingerprint))
            .Select(x => x.Id).ToArray();
        if (gone.Length > 0) await repository.DeleteManyAsync(ownerId, gone, cancellationToken);

        var notifiedAt = state.LastNotifiedAt;
        if (added.Count > 0 && (notifiedAt is null || now - notifiedAt.Value >= RoutineRules.NotifyInterval))
        {
            var best = added.OrderByDescending(x => x.Confidence).First();
            try
            {
                await notifications.CreateAsync(ownerId, RoutineRules.NotificationType,
                    "Jarvis noticed a routine", best.Title + ". " + best.Evidence, best.Id, cancellationToken);
                notifiedAt = now;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed push must not lose the suggestions that were just stored.
            }
        }

        await settings.SaveAsync(ownerId, SettingsSections.Routines, new RoutineState(now, notifiedAt),
            cancellationToken);
        return await PendingAsync(ownerId, cancellationToken);
    }

    public async Task<RoutineAcceptResult?> AcceptAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var entry = await repository.GetAsync(id, ownerId, cancellationToken);
        if (entry is null || entry.Status == RoutineStatuses.Dismissed) return null;
        if (entry.Status == RoutineStatuses.Accepted && entry.AutomationId is { } done)
            return new RoutineAcceptResult(entry, done);

        var definition = AutomationDefinitionJson.Parse(entry.DefinitionJson);
        var json = JsonDocument.Parse(AutomationDefinitionJson.Serialize(definition)).RootElement.Clone();
        var name = entry.Title.Length > 200 ? entry.Title[..200] : entry.Title;
        var rule = await automations.CreateAsync(ownerId, new SaveAutomationRuleRequest(name, json),
            cancellationToken);
        var accepted = entry with
        {
            Status = RoutineStatuses.Accepted, AutomationId = rule.Id, UpdatedAt = clock.GetUtcNow()
        };
        await repository.UpdateAsync(accepted, cancellationToken);
        return new RoutineAcceptResult(accepted, rule.Id);
    }

    public async Task<bool> DismissAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var entry = await repository.GetAsync(id, ownerId, cancellationToken);
        if (entry is null || entry.Status == RoutineStatuses.Accepted) return false;
        if (entry.Status == RoutineStatuses.Dismissed) return true;
        return await repository.UpdateAsync(entry with
        {
            Status = RoutineStatuses.Dismissed, UpdatedAt = clock.GetUtcNow()
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<RoutineSuggestionView>> PendingAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var views = new List<RoutineSuggestionView>();
        foreach (var entry in (await repository.ListAsync(ownerId, cancellationToken))
                 .Where(x => x.Status == RoutineStatuses.Pending)
                 .OrderByDescending(x => x.Confidence).ThenBy(x => x.CreatedAt))
        {
            AutomationRuleDefinition definition;
            try
            {
                definition = AutomationDefinitionJson.Parse(entry.DefinitionJson);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                continue;
            }

            views.Add(new RoutineSuggestionView(entry.Id, entry.Title, entry.Evidence, entry.Confidence,
                AutomationSimulator.Simulate(definition, null, now), entry.CreatedAt));
        }

        return views;
    }

    /// <summary>Sources share one DbContext, so they run one after another; a failing one is skipped.</summary>
    private async Task<IReadOnlyList<TimelineEvent>> CollectAsync(Guid ownerId, TimeZoneInfo zone,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = TimelineRules.LocalDate(now, zone);
        var window = new TimelineWindow(ownerId, today.AddDays(-7 * RoutineMinerEngine.WindowWeeks), today, zone,
            RoutineRules.MaxSourceEvents);
        var events = new List<TimelineEvent>();
        foreach (var source in sources)
        {
            try
            {
                events.AddRange(await source.ListAsync(window, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The miner works with whatever areas could be read.
            }
        }

        return events;
    }
}
