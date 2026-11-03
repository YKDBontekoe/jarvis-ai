using Jarvis.Application.Events;
using Jarvis.Application.Proactivity;
using Jarvis.Application.Profiles;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Jarvis.Application.Settings;
using Jarvis.Application.Goals;
using Jarvis.Application.Trips;
using Jarvis.Application.Documents;
using Jarvis.Application.Meetings;
using System.Text.Json;

namespace Jarvis.Infrastructure.Proactivity;

/// <summary>Explicit, bounded source reads. Never serializes credentials, tool arguments or private meeting notes.</summary>
public sealed class ProactiveSourceReader(JarvisDbContext db, IAssistantProfileRepository profiles,
    IRelatedEntityService related, IOwnerSettingsStore settings) : IProactiveSourceReader
{
    public async Task<bool> AllowsProfileAsync(Guid ownerId, Guid? profileId, CancellationToken ct) =>
        profileId is null || await profiles.GetAsync(profileId.Value, ownerId, ct) is not null;
    public async Task<IReadOnlyList<ProactiveSource>> AvailableAsync(Guid ownerId, CancellationToken ct)
    {
        var refs = new List<string>();
        refs.AddRange((await db.Projects.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(30).ToListAsync(ct)).Select(id => $"project:{id}"));
        refs.AddRange((await db.Commitments.Where(x => x.OwnerId == ownerId && !x.Suggested).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(30).ToListAsync(ct)).Select(id => $"commitment:{id}"));
        refs.AddRange((await db.InboxThreads.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"inbox_thread:{id}"));
        refs.AddRange((await db.Tasks.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"task:{id}"));
        refs.AddRange((await db.Decisions.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"decision:{id}"));
        refs.AddRange((await db.LibraryItems.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"library:{id}"));
        refs.AddRange((await db.ConditionWatches.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"watch:{id}"));
        refs.AddRange((await db.Memories.Where(x => x.OwnerId == ownerId && x.SupersededById == null).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"memory:{id}"));
        refs.AddRange((await db.Files.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"file:{id}"));
        refs.AddRange((await db.Meetings.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"meeting:{id}"));
        refs.AddRange((await db.AutomationRules.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.UpdatedAt).Select(x => x.Id).Take(20).ToListAsync(ct)).Select(id => $"automation:{id}"));
        refs.AddRange(((await settings.GetAsync<GoalState>(ownerId, GoalState.Section, ct))?.Goals ?? []).Select(x => $"goal:{x.Id}"));
        refs.AddRange(((await settings.GetAsync<TripState>(ownerId, TripState.Section, ct))?.Items ?? []).Select(x => $"trip:{x.Id}"));
        refs.AddRange(((await settings.GetAsync<DocumentState>(ownerId, DocumentState.Section, ct))?.Drafts ?? []).Select(x => $"document:{x.Id}"));
        var result = new List<ProactiveSource>();
        foreach (var chunk in refs.Chunk(40)) result.AddRange(await ReadAsync(ownerId, null, chunk, ct));
        return result;
    }
    public async Task<IReadOnlyList<ProactiveSource>> ReadAsync(Guid ownerId, Guid? profileId,
        IReadOnlyList<string> refs, CancellationToken ct)
    {
        AssistantProfileSnapshot? profile = null;
        if (profileId is { } id)
        {
            var record = await profiles.GetAsync(id, ownerId, ct);
            if (record is null) return [];
            profile = AssistantProfileSnapshot.From(record, DateTimeOffset.UtcNow);
        }
        var result = new List<ProactiveSource>();
        foreach (var raw in refs.Distinct().Take(40))
        {
            if (!EntityRef.TryParse(raw, out var entity)) continue;
            var source = await ReadOneAsync(ownerId, profile, entity, ct);
            if (source is not null) result.Add(source);
        }
        return result;
    }

    private async Task<ProactiveSource?> ReadOneAsync(Guid owner, AssistantProfileSnapshot? profile, EntityRef entity, CancellationToken ct)
    {
        // Unclassified owner-wide records cannot be used to bypass a restricted profile's context boundary.
        if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all") &&
            entity.Type is not (EntityTypes.Memory or EntityTypes.File or EntityTypes.Task or EntityTypes.Conversation or EntityTypes.Message))
            return null;
        var id = entity.Id;
        ProactiveSource Make(string title, string state, string detail, DateTimeOffset? at = null,
            string? parent = null, bool protect = false) => new(entity.ToString(), title,
            ProactiveRules.Hash(ProactiveRules.Serialize(new { title, state, detail, at })), state,
            detail.Length > 4000 ? detail[..4000] : detail, at, parent, protect);
        switch (entity.Type)
        {
            case EntityTypes.Meeting:
            {
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var row = await db.Meetings.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null) return null;
                var meeting = JsonSerializer.Deserialize<MeetingDocument>(row.DocumentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (meeting is null) return null;
                var publicLines = meeting.Agenda.Where(a => a.Shared).Select(a => "Agenda: " + a.Text).ToList();
                foreach (var candidate in meeting.Candidates.Where(c => c.State == "accepted"))
                {
                    ProactiveSource? accepted = null;
                    if (EntityRef.TryParse(candidate.ResultRef, out var reference) && reference.Type is EntityTypes.Commitment or EntityTypes.Decision)
                        accepted = await ReadOneAsync(owner, profile, reference, ct);
                    publicLines.Add((accepted is null ? "Unverified accepted: " : ProactiveRules.CompleteStates.Contains(accepted.State) ? "Resolved accepted: " : "Open accepted: ") +
                        candidate.Text + " [" + candidate.ResultRef + "]");
                }
                var publicText = string.Join("\n", publicLines);
                return Make(row.Title, "scheduled", publicText, row.UpdatedAt) with {
                    Version = ProactiveRules.Hash(row.Title + "\n" + publicText) };
            }
            case EntityTypes.Goal:
            {
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var goal = (await settings.GetAsync<GoalState>(owner, GoalState.Section, ct))?.Goals.FirstOrDefault(g => g.Id == id);
                return goal is null ? null : Make(goal.Title, goal.Status,
                    $"Weekly budget: {goal.WeeklyMinutes} minutes. " + string.Join("; ", (goal.Plan?.Items ?? []).Select(i => $"{i.Title}: {i.Minutes} min ({i.Status})")), goal.UpdatedAt)
                    with { EffortMinutes = goal.Plan?.Items.Where(i => i.Status is not ("declined" or "done")).Sum(i => i.Minutes) ?? goal.WeeklyMinutes };
            }
            case EntityTypes.Trip:
            {
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var trip = (await settings.GetAsync<TripState>(owner, TripState.Section, ct))?.Items.FirstOrDefault(t => t.Id == id);
                return trip is null ? null : Make(trip.Title, "planned", $"{trip.Start:yyyy-MM-dd}–{trip.End:yyyy-MM-dd}; {trip.Destination}. " +
                    string.Join("; ", trip.Options.Where(o => o.Chosen).Select(o => $"{o.Title}: {o.Price} {o.Currency}; {o.Availability}; cancel by {o.CancelBy}; source {o.SourceUrl}")) +
                    "\n" + string.Join('\n', TripPlanner.Checklist(trip).Where(c => !c.Done).Select(c => "Checklist open: " + c.Text)) +
                    "\n" + string.Join('\n', TripPlanner.Holds(trip).Select(h => $"Suggested preparation hold: {h.Date:yyyy-MM-dd} {h.At}: {h.Title} ({h.Why})")), trip.UpdatedAt)
                    with { TripOptions = trip.Options, TripItems = trip.Items };
            }
            case EntityTypes.Document:
            {
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var draft = (await settings.GetAsync<DocumentState>(owner, DocumentState.Section, ct))?.Drafts.FirstOrDefault(d => d.Id == id);
                return draft is null ? null : Make(draft.Title, "draft", string.Join("\n", draft.Sections.Select(s => s.Heading + ": " + s.Body)), draft.UpdatedAt);
            }
            case EntityTypes.Message:
            {
                var row = await db.Messages.AsNoTracking().Where(m => m.Id == id && db.Conversations.Any(c => c.Id == m.ConversationId && c.OwnerId == owner))
                    .FirstOrDefaultAsync(ct);
                if (row is null) return null;
                if (profile is not null && !await db.Conversations.AnyAsync(c => c.OwnerId == owner && c.Id == row.ConversationId && c.ProfileId == profile.ProfileId, ct)) return null;
                return Make("Source message", row.Role, row.Content, row.CreatedAt, $"conversation:{row.ConversationId}");
            }
            case EntityTypes.Automation:
            {
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var row = await db.AutomationRules.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null) return null;
                var definition = Jarvis.Application.Automations.AutomationDefinitionJson.Deserialize(row.DefinitionJson);
                var simulation = Jarvis.Application.Automations.AutomationSimulator.Simulate(definition, null, DateTimeOffset.UtcNow);
                return Make(row.Name, row.Status, $"{simulation.Trigger}; {definition.Actions.Count} actions. Open Automations to review the steps.", row.UpdatedAt)
                    with { Version = ProactiveRules.Hash(row.DefinitionJson) };
            }
            case EntityTypes.Task:
            {
                var row = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null) return null;
                if (profile is not null && !await db.Conversations.AnyAsync(x => x.Id == row.ConversationId && x.OwnerId == owner && x.ProfileId == profile.ProfileId, ct)) return null;
                return Make(row.Title, row.Status, row.Summary ?? "No result yet.", row.CompletedAt ?? row.StartedAt ?? row.CreatedAt);
            }
            case EntityTypes.Commitment:
            {
                var row = await db.Commitments.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                return row is null ? null : Make(row.Description, row.Suggested ? "suggested" : row.Status,
                    $"{row.Direction}; {row.Counterparty}; due {row.DueOn?.ToString("yyyy-MM-dd") ?? "unknown"}.", row.UpdatedAt,
                    row.InboxThreadId is { } thread ? $"inbox_thread:{thread}" : null);
            }
            case EntityTypes.InboxThread:
            {
                var row = await db.InboxThreads.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                return row is null ? null : Make(row.Title, !row.LastFromMe && row.LastMessageAt is not null ? "reply_received" : row.State,
                    row.Summary ?? row.LastMessagePreview ?? "No message preview.", row.LastMessageAt ?? row.UpdatedAt);
            }
            case EntityTypes.Project:
            {
                var row = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                return row is null ? null : Make(row.Name, row.Status, row.Outcome ?? row.Description ?? "No outcome set.", row.UpdatedAt);
            }
            case EntityTypes.Decision:
            {
                var row = await db.Decisions.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                return row is null ? null : Make(row.Title, row.ResolvedAt is null ? "open" : "resolved",
                    $"Prediction: {row.Prediction}; review {row.ReviewOn:yyyy-MM-dd}.", row.UpdatedAt);
            }
            case EntityTypes.Memory:
            {
                var row = await db.Memories.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null || row.SupersededById is not null || row.ValidUntil <= DateTimeOffset.UtcNow || !ProfileScope.AllowsMemory(profile, row.ToRecord())) return null;
                return Make(row.Kind, "current", row.Content, row.UpdatedAt, protect: row.IsPinned || row.SourceType == "user") with { IsPinned = row.IsPinned };
            }
            case EntityTypes.File:
            {
                var row = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null) return null;
                if (profile?.RestrictFiles == true && !await db.DocumentCollectionFiles.AnyAsync(x => x.OwnerId == owner && x.FileId == id && profile.AllowedCollectionIds.Contains(x.CollectionId), ct)) return null;
                return Make(row.FileName, row.ProcessingStatus, $"{row.ContentType}; {row.SizeBytes} bytes; {row.Sha256}", row.CreatedAt);
            }
            case EntityTypes.Conversation:
            {
                var row = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                if (row is null || profile is not null && row.ProfileId != profile.ProfileId) return null;
                // The pack holds only a title; message content remains behind ordinary conversation access.
                return Make(row.Title, "open", "Open the source conversation for its messages.", row.UpdatedAt);
            }
            case EntityTypes.Watch:
            {
                var row = await db.ConditionWatches.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                var state = row is not null && (row.Status is "active" or "triggered") &&
                    (row.LastCheckedAt is null || row.LastCheckedAt < DateTimeOffset.UtcNow.AddMinutes(-Math.Max(60, row.IntervalMinutes * 2))) ? "unavailable" : row?.Status;
                return row is null ? null : Make(row.Title, state!,
                    $"Observed value: {row.LastValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; triggers: {row.TriggerCount}.", row.LastCheckedAt)
                    with { NumericValue = row.LastValue, ValidUntil = row.LastCheckedAt?.AddMinutes(Math.Max(60, row.IntervalMinutes * 2)),
                        Version = ProactiveRules.Hash($"{state}|{row.LastValue?.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{row.TriggerCount}") };
            }
            case EntityTypes.Library:
            {
                // Library items have no file/profile access policy. Restricted profiles must not use them as a bypass.
                if (profile is not null && (profile.RestrictFiles || profile.MemoryScope != "all")) return null;
                var row = await db.LibraryItems.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == owner && x.Id == id, ct);
                return row is null ? null : Make(row.Title, "saved", row.Summary, row.UpdatedAt) with { Url = row.Url };
            }
            default:
            {
                if (profile is not null) return null;
                var title = await related.DescribeAsync(owner, entity, ct);
                return title is null ? null : Make(title, "unknown", "Open the source to review its current state.");
            }
        }
    }
}
