using Jarvis.Application.Automations;
using Jarvis.Application.WhatsApp;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Inbox;

public sealed class InboxService(IInboxRepository repository, IWhatsAppAssistantRepository whatsApp,
    IInboxTriager triager, ICommitmentService commitments, TimeProvider? timeProvider = null,
    Automations.IAutomationEventBus? events = null) : IInboxService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<InboxSyncResult> SyncAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        int created = 0, updated = 0, scanned = 0;
        var now = clock.GetUtcNow();
        foreach (var chat in (await whatsApp.ListChatsAsync(ownerId, null, cancellationToken)).Where(x => x.ReadAlong))
        {
            var messages = await whatsApp.ListMessagesAsync(ownerId, chat.ConnectionId, chat.ChatId,
                InboxRules.ContextMessages, null, cancellationToken);
            if (messages.Count == 0) continue;
            scanned++;

            var newest = messages.MaxBy(x => x.SentAt)!;
            var key = ExternalKey(chat.ConnectionId, chat.ChatId);
            var existing = await repository.FindThreadAsync(ownerId, InboxSources.WhatsApp, key, cancellationToken);
            if (existing is not null && !InboxHeuristics.ShouldRetriage(existing, newest.SentAt)) continue;

            var triage = InboxHeuristics.Triage(Views(messages), chat.IsGroup, now);
            var preview = InboxRules.Limit(newest.Text, InboxRules.MaxPreviewLength);
            if (existing is null)
            {
                await repository.AddThreadAsync(new InboxThread(Guid.CreateVersion7(), ownerId,
                    InboxSources.WhatsApp, key, chat.ConnectionId, chat.ChatId,
                    InboxRules.Limit(chat.DisplayName, InboxRules.MaxTitleLength) ?? "Chat", chat.DisplayName,
                    triage.State, triage.Priority, null, null, preview, newest.FromMe, newest.SentAt, null, null, now,
                    now), cancellationToken);
                created++;
                await PublishIfNeedsReplyAsync(ownerId, chat.DisplayName, preview, triage.State, newest, cancellationToken);
            }
            else
            {
                // New messages make an earlier summary and draft stale, and reopen a finished or snoozed thread.
                await repository.UpdateThreadAsync(existing with
                {
                    Title = InboxRules.Limit(chat.DisplayName, InboxRules.MaxTitleLength) ?? existing.Title,
                    State = triage.State,
                    Priority = triage.Priority,
                    Summary = null,
                    SuggestedReply = null,
                    TriagedAt = null,
                    SnoozedUntil = null,
                    LastMessagePreview = preview,
                    LastFromMe = newest.FromMe,
                    LastMessageAt = newest.SentAt,
                    UpdatedAt = now
                }, cancellationToken);
                updated++;
                await PublishIfNeedsReplyAsync(ownerId, chat.DisplayName, preview, triage.State, newest, cancellationToken);
            }
        }
        return new InboxSyncResult(created, updated, scanned);
    }

    private Task PublishIfNeedsReplyAsync(Guid ownerId, string title, string? preview, string state,
        WhatsAppChatMessage newest, CancellationToken cancellationToken) =>
        state == InboxStates.NeedsReply
            ? events.TryPublishAsync(ownerId, new Automations.AutomationEvent(
                Automations.AutomationEventKinds.InboxNeedsReply, title, preview, "whatsapp", newest.Id,
                newest.SentAt), cancellationToken)
            : Task.CompletedTask;

    public async Task<InboxListing> ListAsync(Guid ownerId, IReadOnlySet<string>? states,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var threads = new List<InboxThread>();
        foreach (var thread in await repository.ListThreadsAsync(ownerId, cancellationToken))
        {
            // A snooze that has run out brings the thread back to the top.
            if (thread is { State: InboxStates.Snoozed, SnoozedUntil: { } until } && until <= now)
            {
                var woken = thread with { State = InboxStates.NeedsReply, SnoozedUntil = null, UpdatedAt = now };
                await repository.UpdateThreadAsync(woken, cancellationToken);
                threads.Add(woken);
            }
            else
            {
                threads.Add(thread);
            }
        }

        var counts = InboxStates.All.ToDictionary(state => state, state => threads.Count(x => x.State == state));
        var shown = threads.Where(x => states is not { Count: > 0 } || states.Contains(x.State))
            .OrderBy(x => InboxStates.Rank(x.State))
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.LastMessageAt ?? x.UpdatedAt)
            .ToArray();
        return new InboxListing(shown, counts);
    }

    public Task<InboxThread?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetThreadAsync(id, ownerId, cancellationToken);

    public async Task<InboxOperation<InboxThread>> SetStateAsync(Guid id, Guid ownerId, string state,
        CancellationToken cancellationToken)
    {
        if (!InboxStates.IsValid(state) || state == InboxStates.Snoozed)
            return InboxOperation<InboxThread>.Invalid("state",
                "Use needs_reply, waiting, fyi, or done. Snooze a thread with a date instead.");
        var thread = await repository.GetThreadAsync(id, ownerId, cancellationToken);
        if (thread is null) return InboxOperation<InboxThread>.NotFound();
        var updated = thread with { State = state, SnoozedUntil = null, UpdatedAt = clock.GetUtcNow() };
        await repository.UpdateThreadAsync(updated, cancellationToken);
        return InboxOperation<InboxThread>.Ok(updated);
    }

    public async Task<InboxOperation<InboxThread>> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (until <= now.AddMinutes(1) || until > now + InboxRules.MaxSnooze)
            return InboxOperation<InboxThread>.Invalid("until", "Snooze to a moment within the next six months.");
        var thread = await repository.GetThreadAsync(id, ownerId, cancellationToken);
        if (thread is null) return InboxOperation<InboxThread>.NotFound();
        var updated = thread with { State = InboxStates.Snoozed, SnoozedUntil = until, UpdatedAt = now };
        await repository.UpdateThreadAsync(updated, cancellationToken);
        return InboxOperation<InboxThread>.Ok(updated);
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteThreadAsync(id, ownerId, cancellationToken);

    public async Task<InboxOperation<InboxTriageOutcome>> TriageAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var thread = await repository.GetThreadAsync(id, ownerId, cancellationToken);
        if (thread is null) return InboxOperation<InboxTriageOutcome>.NotFound();

        IReadOnlyList<InboxMessageView> views;
        if (thread is { Source: InboxSources.WhatsApp, ConnectionId: { } connectionId, ChatId: { } chatId })
        {
            var messages = await whatsApp.ListMessagesAsync(ownerId, connectionId, chatId, InboxRules.ContextMessages,
                null, cancellationToken);
            views = Views(messages);
        }
        else
        {
            views = thread.LastMessagePreview is null
                ? []
                : [new InboxMessageView(thread.LastFromMe, thread.Counterparty, thread.LastMessagePreview,
                    thread.LastMessageAt ?? thread.UpdatedAt)];
        }
        if (views.Count == 0)
            return InboxOperation<InboxTriageOutcome>.Unavailable("There are no messages to look at in this thread.");

        var now = clock.GetUtcNow();
        var triage = await triager.TriageAsync(ownerId, thread, views, now, cancellationToken);
        var keepState = thread.State is InboxStates.Done or InboxStates.Snoozed &&
                        thread.LastMessageAt is { } last && views.All(x => x.At <= last);
        var updated = thread with
        {
            State = keepState ? thread.State : InboxStates.IsValid(triage.State) ? triage.State : thread.State,
            Priority = InboxPriorities.Clamp(triage.Priority),
            Summary = InboxRules.Limit(triage.Summary, InboxRules.MaxSummaryLength),
            SuggestedReply = InboxRules.Limit(triage.SuggestedReply, InboxRules.MaxReplyLength),
            TriagedAt = now,
            UpdatedAt = now
        };
        await repository.UpdateThreadAsync(updated, cancellationToken);

        var source = thread.Source == InboxSources.WhatsApp ? CommitmentSources.WhatsApp
            : thread.Source == InboxSources.Mail ? CommitmentSources.Mail
            : CommitmentSources.Manual;
        var added = await commitments.SuggestAsync(ownerId, thread.Id, source, triage.Commitments,
            await commitments.TodayAsync(ownerId, cancellationToken), cancellationToken);
        return InboxOperation<InboxTriageOutcome>.Ok(new InboxTriageOutcome(updated, added));
    }

    public async Task<InboxOperation<InboxThread>> TrackAsync(Guid ownerId, ExternalInboxItem item,
        CancellationToken cancellationToken)
    {
        if (item.Source is not (InboxSources.Mail or InboxSources.Manual))
            return InboxOperation<InboxThread>.Invalid("source", "Use mail or manual.");
        var key = InboxRules.Clean(item.ExternalKey);
        if (key is null || key.Length > InboxRules.MaxKeyLength)
            return InboxOperation<InboxThread>.Invalid("externalKey",
                $"Give an id of 1 to {InboxRules.MaxKeyLength} characters.");
        var title = InboxRules.Limit(item.Title, InboxRules.MaxTitleLength);
        if (title is null) return InboxOperation<InboxThread>.Invalid("title", "Give the thread a title.");

        var now = clock.GetUtcNow();
        var at = item.At is { } given && given <= now.AddHours(1) ? given : now;
        var preview = InboxRules.Limit(item.Preview, InboxRules.MaxPreviewLength);
        var heuristic = InboxHeuristics.Triage(
            preview is null ? [] : [new InboxMessageView(item.FromMe, item.Counterparty, preview, at)], false, now);
        var state = item.NeedsReply switch
        {
            true => InboxStates.NeedsReply,
            false => item.FromMe ? InboxStates.Waiting : InboxStates.Fyi,
            null => preview is null ? InboxStates.NeedsReply : heuristic.State
        };
        var priority = InboxPriorities.Clamp(item.Priority ?? heuristic.Priority);

        var existing = await repository.FindThreadAsync(ownerId, item.Source, key, cancellationToken);
        if (existing is null)
        {
            var thread = new InboxThread(Guid.CreateVersion7(), ownerId, item.Source, key, null, null, title,
                InboxRules.Limit(item.Counterparty, InboxRules.MaxCounterpartyLength), state, priority, null, null,
                preview, item.FromMe, at, null, null, now, now);
            await repository.AddThreadAsync(thread, cancellationToken);
            return InboxOperation<InboxThread>.Ok(thread);
        }

        var updated = existing with
        {
            Title = title,
            Counterparty = InboxRules.Limit(item.Counterparty, InboxRules.MaxCounterpartyLength) ??
                           existing.Counterparty,
            State = state,
            Priority = priority,
            Summary = null,
            SuggestedReply = null,
            TriagedAt = null,
            SnoozedUntil = null,
            LastMessagePreview = preview ?? existing.LastMessagePreview,
            LastFromMe = item.FromMe,
            LastMessageAt = at,
            UpdatedAt = now
        };
        await repository.UpdateThreadAsync(updated, cancellationToken);
        return InboxOperation<InboxThread>.Ok(updated);
    }

    public static string ExternalKey(Guid connectionId, string chatId) => $"{connectionId:N}:{chatId}";

    private static IReadOnlyList<InboxMessageView> Views(IReadOnlyList<WhatsAppChatMessage> messages) =>
        messages.OrderBy(x => x.SentAt)
            .Select(x => new InboxMessageView(x.FromMe, x.Sender, x.Text, x.SentAt))
            .ToArray();
}
