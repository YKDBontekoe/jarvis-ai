using Jarvis.Application.Settings;
using Jarvis.Application.WhatsApp;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;

namespace Jarvis.Application.People.Radar;

/// <summary>
/// Links people to their WhatsApp chats and turns message times into the relationship radar. Messages only exist
/// for chats the owner reads along with, so a link to any other chat shows no data.
/// </summary>
public sealed class RelationshipRadarService(
    IPeopleRepository people,
    IPersonLinkRepository links,
    IChatActivityStats stats,
    IWhatsAppAssistantRepository chats,
    IOwnerSettingsStore settings,
    INotificationRepository notifications,
    IPeopleCheckInScheduler? scheduler = null,
    IRadarToneAnalyzer? tone = null,
    TimeProvider? timeProvider = null) : IRelationshipRadarService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<RadarOverview> OverviewAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await SyncLastContactAsync(ownerId, cancellationToken);
        var all = await links.ListAsync(ownerId, cancellationToken);
        var reports = await BuildReportsAsync(ownerId, all, cancellationToken);
        var config = await SettingsAsync(ownerId, cancellationToken);
        return new RadarOverview(config.ToneEnabled, reports
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    public async Task<PersonRadarView?> PersonAsync(Guid ownerId, Guid personId, CancellationToken cancellationToken)
    {
        var views = await LinksAsync(ownerId, personId, cancellationToken);
        if (views is null) return null;
        var mine = (await links.ListAsync(ownerId, cancellationToken)).Where(x => x.PersonId == personId).ToArray();
        var report = mine.Length == 0
            ? null
            : (await BuildReportsAsync(ownerId, mine, cancellationToken)).FirstOrDefault();
        return new PersonRadarView(views, report);
    }

    public async Task<IReadOnlyList<PersonLinkView>?> LinksAsync(Guid ownerId, Guid personId,
        CancellationToken cancellationToken)
    {
        if (await people.GetAsync(personId, ownerId, cancellationToken) is null) return null;
        var mine = (await links.ListAsync(ownerId, cancellationToken)).Where(x => x.PersonId == personId).ToArray();
        var known = await ChatsByKeyAsync(ownerId, cancellationToken);
        return mine.Select(link => ToView(link, known)).ToArray();
    }

    public async Task<PeopleOperation<PersonLinkView>> LinkAsync(Guid ownerId, Guid personId, Guid connectionId,
        string chatId, CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, ownerId, cancellationToken);
        if (person is null) return PeopleOperation<PersonLinkView>.NotFound();
        var id = WhatsAppChatIds.Normalize(chatId ?? string.Empty);
        if (string.IsNullOrEmpty(id))
            return PeopleOperation<PersonLinkView>.Invalid("chatId", "Pick a chat to link.");
        var chat = await chats.GetChatAsync(ownerId, connectionId, id, cancellationToken);
        if (chat is null) return PeopleOperation<PersonLinkView>.Invalid("chatId", "That chat was not found.");
        if (chat.IsGroup || id.EndsWith("@g.us", StringComparison.Ordinal))
            return PeopleOperation<PersonLinkView>.Invalid("chatId", "Only one-to-one chats can be linked to a person.");

        var existing = await links.ListAsync(ownerId, cancellationToken);
        if (existing.FirstOrDefault(x => x.ConnectionId == connectionId && x.ChatId == chat.ChatId) is { } taken)
            return taken.PersonId == personId
                ? PeopleOperation<PersonLinkView>.Ok(ToView(taken, [chat]))
                : PeopleOperation<PersonLinkView>.Conflict("chatId", "That chat is already linked to someone else.");
        if (existing.Count(x => x.PersonId == personId) >= RadarRules.MaxLinksPerPerson)
            return PeopleOperation<PersonLinkView>.Invalid("chatId",
                $"A person can have at most {RadarRules.MaxLinksPerPerson} linked chats.");
        if (existing.Count >= RadarRules.MaxLinksPerOwner)
            return PeopleOperation<PersonLinkView>.Invalid("chatId",
                $"You can link at most {RadarRules.MaxLinksPerOwner} chats.");

        var link = new PersonChannelLink(Guid.CreateVersion7(), ownerId, personId, connectionId, chat.ChatId, null,
            null, null, clock.GetUtcNow());
        await links.AddAsync(link, cancellationToken);

        // The daily check-in carries the radar nudges, so it must be running even for someone without a cadence.
        if (scheduler is not null)
        {
            try
            {
                await scheduler.SchedulePeopleCheckInAsync(ownerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The link is saved; the reconciler starts the check-in on its next pass.
            }
        }

        return PeopleOperation<PersonLinkView>.Ok(ToView(link, [chat]));
    }

    public async Task<bool> UnlinkAsync(Guid ownerId, Guid personId, Guid linkId, CancellationToken cancellationToken) =>
        await links.DeleteAsync(linkId, personId, ownerId, cancellationToken);

    public async Task<IReadOnlyList<LinkSuggestion>> SuggestAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var linked = await links.ListAsync(ownerId, cancellationToken);
        var linkedPeople = linked.Select(x => x.PersonId).ToHashSet();
        var linkedChats = linked.Select(x => new ChatKey(x.ConnectionId, x.ChatId)).ToHashSet();
        var candidatePeople = (await people.ListAsync(ownerId, cancellationToken))
            .Where(x => !linkedPeople.Contains(x.Id)).ToArray();
        var candidateChats = (await chats.ListChatsAsync(ownerId, null, cancellationToken))
            .Where(x => !x.IsGroup && !x.ChatId.EndsWith("@g.us", StringComparison.Ordinal) &&
                        !linkedChats.Contains(new ChatKey(x.ConnectionId, x.ChatId)))
            .ToArray();
        return LinkMatcher.Match(candidatePeople, candidateChats);
    }

    public async Task<IReadOnlyList<LinkCandidate>> CandidatesAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var linked = (await links.ListAsync(ownerId, cancellationToken))
            .Select(x => new ChatKey(x.ConnectionId, x.ChatId)).ToHashSet();
        return (await chats.ListChatsAsync(ownerId, null, cancellationToken))
            .Where(x => !x.IsGroup && !x.ChatId.EndsWith("@g.us", StringComparison.Ordinal) &&
                        !linked.Contains(new ChatKey(x.ConnectionId, x.ChatId)))
            .OrderByDescending(x => x.ReadAlong).ThenByDescending(x => x.LastMessageAt)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(RadarRules.MaxCandidates)
            .Select(x => new LinkCandidate(x.ConnectionId, x.ChatId, x.DisplayName, x.ReadAlong)).ToArray();
    }

    public async Task<RadarSettings> SettingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<RadarSettings>(ownerId, SettingsSections.PeopleRadar, cancellationToken)
        ?? new RadarSettings();

    public async Task<RadarSettings> SaveSettingsAsync(Guid ownerId, bool toneEnabled,
        CancellationToken cancellationToken)
    {
        var updated = (await SettingsAsync(ownerId, cancellationToken)) with { ToneEnabled = toneEnabled };
        await settings.SaveAsync(ownerId, SettingsSections.PeopleRadar, updated, cancellationToken);
        return updated;
    }

    public async Task<bool> HasLinksAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await links.ListAsync(ownerId, cancellationToken)).Count > 0;

    public async Task SyncLastContactAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var all = await links.ListAsync(ownerId, cancellationToken);
        if (all.Count == 0) return;
        var now = clock.GetUtcNow();
        var sent = (await stats.ListAsync(ownerId, Keys(all), now.AddDays(-RadarRules.HistoryDays), cancellationToken))
            .Where(x => x.FromMe && x.SentAt <= now).ToArray();
        if (sent.Length == 0) return;

        var byPerson = all.GroupBy(x => x.PersonId);
        foreach (var group in byPerson)
        {
            var keys = group.Select(x => new ChatKey(x.ConnectionId, x.ChatId)).ToHashSet();
            var latest = sent.Where(x => keys.Contains(new ChatKey(x.ConnectionId, x.ChatId)))
                .Select(x => (DateTimeOffset?)x.SentAt).Max();
            if (latest is not { } at) continue;
            var person = await people.GetAsync(group.Key, ownerId, cancellationToken);
            if (person is null || person.LastContactedAt >= at) continue;
            await people.UpdateAsync(person with { LastContactedAt = at, UpdatedAt = now }, cancellationToken);
        }
    }

    public async Task<RadarDailyResult> DailyAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var all = await links.ListAsync(ownerId, cancellationToken);
        if (all.Count == 0) return new RadarDailyResult(0);
        await SyncLastContactAsync(ownerId, cancellationToken);

        var config = await SettingsAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        if (config.ToneEnabled && tone is not null)
        {
            await RefreshToneAsync(ownerId, all, now, cancellationToken);
            all = await links.ListAsync(ownerId, cancellationToken);
        }

        if (config.LastNudgedAt is { } last && now - last < TimeSpan.FromDays(RadarRules.NudgeRepeatDays))
            return new RadarDailyResult(0);

        var worrying = (await BuildReportsAsync(ownerId, all, cancellationToken))
            .Where(x => x.Severity >= 2)
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (worrying.Length == 0) return new RadarDailyResult(0);

        var (title, body) = DescribeNudge(worrying);
        await notifications.CreateAsync(ownerId, RadarRules.NotificationType, title, body,
            worrying.Length == 1 ? worrying[0].PersonId : null, cancellationToken);
        await settings.SaveAsync(ownerId, SettingsSections.PeopleRadar, config with { LastNudgedAt = now },
            cancellationToken);
        return new RadarDailyResult(1);
    }

    public static (string Title, string Body) DescribeNudge(IReadOnlyList<RadarReport> worrying)
    {
        if (worrying.Count == 1)
        {
            var one = worrying[0];
            var top = one.Signals.OrderByDescending(s => s.Severity).First();
            return ($"{one.Name}: {top.Headline}", top.Detail);
        }

        var title = worrying.Count == 2
            ? $"{worrying[0].Name} and {worrying[1].Name} may need a message"
            : $"{worrying[0].Name} and {worrying.Count - 1} others may need a message";
        var lines = worrying.Take(RadarRules.MaxNudgeNames).Select(x =>
            $"{x.Name}: {x.Signals.OrderByDescending(s => s.Severity).First().Headline.ToLowerInvariant()}");
        var body = string.Join("; ", lines);
        if (worrying.Count > RadarRules.MaxNudgeNames) body += $"; and {worrying.Count - RadarRules.MaxNudgeNames} more";
        return (title, body + ".");
    }

    private async Task<IReadOnlyList<RadarReport>> BuildReportsAsync(Guid ownerId,
        IReadOnlyList<PersonChannelLink> all, CancellationToken cancellationToken)
    {
        if (all.Count == 0) return [];
        var now = clock.GetUtcNow();
        var rows = await stats.ListAsync(ownerId, Keys(all), now.AddDays(-RadarRules.HistoryDays), cancellationToken);
        var byChat = rows.ToLookup(x => new ChatKey(x.ConnectionId, x.ChatId));
        var known = (await people.ListAsync(ownerId, cancellationToken)).ToDictionary(x => x.Id);

        var reports = new List<RadarReport>();
        foreach (var group in all.GroupBy(x => x.PersonId))
        {
            if (!known.TryGetValue(group.Key, out var person)) continue;
            var messages = group
                .SelectMany(link => byChat[new ChatKey(link.ConnectionId, link.ChatId)])
                .Select(x => new RadarMessage(x.FromMe, x.SentAt)).ToArray();
            var judged = group.Where(x => x.ToneAt is not null).OrderByDescending(x => x.ToneAt).FirstOrDefault();
            reports.Add(RelationshipRadarEngine.Analyze(person.Id, person.Name, messages, now, judged?.ToneScore,
                judged?.ToneReason));
        }

        return reports;
    }

    private async Task RefreshToneAsync(Guid ownerId, IReadOnlyList<PersonChannelLink> all, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = all.Where(x => x.ToneAt is not { } at || now - at >= TimeSpan.FromDays(RadarRules.ToneRefreshDays))
            .Take(RadarRules.ToneMaxPerRun).ToArray();
        if (due.Length == 0) return;
        var names = (await people.ListAsync(ownerId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var since = now.AddDays(-RadarRules.RecentDays);
        foreach (var link in due)
        {
            if (!names.TryGetValue(link.PersonId, out var name)) continue;
            var messages = (await chats.ListMessagesAsync(ownerId, link.ConnectionId, link.ChatId,
                    RadarRules.ToneMaxMessages, null, cancellationToken))
                .Where(x => x.SentAt > since && !string.IsNullOrWhiteSpace(x.Text))
                .OrderBy(x => x.SentAt)
                .Select(x => new RadarMessageText(x.FromMe, x.SentAt, x.Text)).ToArray();
            // Too little to judge: remember that we looked so a quiet chat is not asked about every day.
            if (messages.Length < RadarRules.ToneMinMessages)
            {
                await links.UpdateToneAsync(link.Id, ownerId, null, null, now, cancellationToken);
                continue;
            }

            try
            {
                var result = await tone!.AnalyzeAsync(ownerId, name, messages, cancellationToken);
                if (result is not null)
                    await links.UpdateToneAsync(link.Id, ownerId, Math.Clamp(result.Warmth, -2, 2), result.Reason,
                        now, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A guess that cannot be made is simply missing; the radar does not depend on it.
            }
        }
    }

    private static IReadOnlyCollection<ChatKey> Keys(IReadOnlyList<PersonChannelLink> all) =>
        all.Select(x => new ChatKey(x.ConnectionId, x.ChatId)).Distinct().ToArray();

    private async Task<IReadOnlyList<WhatsAppChatSettings>> ChatsByKeyAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        await chats.ListChatsAsync(ownerId, null, cancellationToken);

    private static PersonLinkView ToView(PersonChannelLink link, IReadOnlyList<WhatsAppChatSettings> known)
    {
        var chat = known.FirstOrDefault(x => x.ConnectionId == link.ConnectionId && x.ChatId == link.ChatId);
        return new PersonLinkView(link.Id, link.PersonId, link.ConnectionId, link.ChatId,
            chat?.DisplayName ?? link.ChatId, chat?.ReadAlong ?? false, link.CreatedAt);
    }
}

/// <summary>
/// Finds people and chats that look like the same human. A suggestion is only ever shown for the owner to confirm.
/// A full name match always counts; a shared first name counts only when exactly one unlinked person and one
/// chat have it, so "Anna" never guesses between two Annas.
/// </summary>
public static class LinkMatcher
{
    public static IReadOnlyList<LinkSuggestion> Match(IReadOnlyList<Person> people,
        IReadOnlyList<WhatsAppChatSettings> chats)
    {
        var named = chats.Where(chat => PeopleRules.Words(chat.DisplayName).Count > 0 &&
                                        !IsNumber(chat.DisplayName)).ToArray();
        var suggestions = new List<LinkSuggestion>();
        var usedPeople = new HashSet<Guid>();
        var usedChats = new HashSet<ChatKey>();

        // Exact (accent- and case-insensitive) names first.
        foreach (var person in people)
        {
            var key = PeopleRules.NameKey(person.Name);
            if (key.Length == 0) continue;
            var matches = named.Where(c => PeopleRules.NameKey(c.DisplayName) == key &&
                                           !usedChats.Contains(new ChatKey(c.ConnectionId, c.ChatId))).ToArray();
            if (matches.Length != 1) continue;
            suggestions.Add(Suggest(person, matches[0]));
            usedPeople.Add(person.Id);
            usedChats.Add(new ChatKey(matches[0].ConnectionId, matches[0].ChatId));
        }

        // Then a first name that only one person and one chat have.
        var remainingPeople = people.Where(x => !usedPeople.Contains(x.Id)).ToArray();
        var remainingChats = named.Where(c => !usedChats.Contains(new ChatKey(c.ConnectionId, c.ChatId))).ToArray();
        foreach (var person in remainingPeople)
        {
            var first = PeopleRules.Words(person.Name).FirstOrDefault();
            if (first is null || first.Length < 3) continue;
            if (remainingPeople.Count(p => PeopleRules.Words(p.Name).FirstOrDefault() == first) != 1) continue;
            var matches = remainingChats.Where(c => PeopleRules.Words(c.DisplayName).FirstOrDefault() == first)
                .ToArray();
            if (matches.Length != 1) continue;
            suggestions.Add(Suggest(person, matches[0]));
        }

        return suggestions;
    }

    private static LinkSuggestion Suggest(Person person, WhatsAppChatSettings chat) =>
        new(person.Id, person.Name, chat.ConnectionId, chat.ChatId, chat.DisplayName);

    /// <summary>A chat that is only a phone number has no name to compare.</summary>
    private static bool IsNumber(string name) => name.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')');
}
