using System.Globalization;
using System.Security.Cryptography;
using Jarvis.Application.Decisions;
using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Inbox;
using Jarvis.Application.Journal;
using Jarvis.Application.Library;
using Jarvis.Application.Memory;
using Jarvis.Application.Preferences;
using Jarvis.Application.Settings;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Discover;

namespace Jarvis.Application.Discover;

internal static class OwnerToday
{
    public static async Task<DateOnly> GetAsync(IOwnerSettingsStore settings, IDailyBriefingRepository briefings,
        Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var zoneId = await OwnerZoneResolver.ZoneIdAsync(settings, briefings, ownerId, cancellationToken);
        LocalClock.TryFind(zoneId, out var zone);
        return TimelineRules.LocalDate(now, zone);
    }
}

/// <summary>One collision a day from the owner's library, journal and memories.</summary>
public sealed class CollisionService(
    ILibraryService library,
    IJournalService journal,
    IMemoryService memories,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : ICollisionService
{
    private const int MaxSeen = 365;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Collision?> TodayAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await FindAsync(ownerId, advance: false, cancellationToken);

    public async Task<Collision?> NextAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await FindAsync(ownerId, advance: true, cancellationToken);

    private async Task<Collision?> FindAsync(Guid ownerId, bool advance, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = await OwnerToday.GetAsync(settings, briefings, ownerId, now, cancellationToken);
        var state = await settings.GetAsync<CollisionState>(ownerId, SettingsSections.Collisions, cancellationToken)
                    ?? new CollisionState();
        var seen = (state.Seen ?? []).ToList();
        var items = await ItemsAsync(ownerId, cancellationToken);

        // The same collision all day, unless the owner asked for another.
        if (!advance && state.Day == today && state.Key is { } key)
        {
            var kept = CollisionEngine.Find(items,
                seen.Where(s => s != key).ToHashSet(StringComparer.Ordinal));
            if (kept?.Key == key) return kept;
        }

        if (advance && state.Key is { } current && !seen.Contains(current)) seen.Add(current);
        if (!advance && state.Key is { } previous && state.Day != today && !seen.Contains(previous))
            seen.Add(previous);
        if (seen.Count > MaxSeen) seen.RemoveRange(0, seen.Count - MaxSeen);

        var found = CollisionEngine.Find(items, seen.ToHashSet(StringComparer.Ordinal));
        await settings.SaveAsync(ownerId, SettingsSections.Collisions,
            new CollisionState(today, found?.Key, seen), cancellationToken);
        return found;
    }

    private async Task<IReadOnlyList<CollisionItem>> ItemsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var items = new List<CollisionItem>();
        foreach (var item in await library.ListAsync(ownerId, new LibraryQuery(Limit: LibraryRules.MaxListLimit),
                     cancellationToken))
            items.Add(new CollisionItem("library", item.Id, item.Title,
                string.Join(' ', [item.Summary, .. item.KeyPoints, .. item.Tags]), item.CreatedAt));

        foreach (var entry in await journal.ListAsync(ownerId, null, null, 300, cancellationToken))
            items.Add(new CollisionItem("journal", entry.Id, $"Journal, {entry.EntryDate:d MMM yyyy}",
                string.Join(' ', [entry.Content, entry.Highlights ?? "", entry.Gratitude ?? "", .. entry.Tags]),
                // The day the entry is about, not when it was typed in.
                new DateTimeOffset(entry.EntryDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));

        foreach (var memory in (await memories.ListAsync(ownerId, null, cancellationToken))
                 .Where(m => m.Kind != "journal" && m.SupersededById is null &&
                             (m.ValidUntil is not { } until || until > now))
                 .Take(300))
            items.Add(new CollisionItem("memory", memory.Id, Shorten(memory.Content, 60), memory.Content,
                memory.CreatedAt));

        return items;
    }

    private static string Shorten(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// The life museum: a year as rooms of exhibits, built from the owner's own records. Nothing is generated; empty
/// rooms are left out.
/// </summary>
public sealed class MuseumService(
    IJournalService journal,
    IHabitService habits,
    IDecisionService decisions,
    IExpenseRepository expenses,
    ILibraryService library,
    ICommitmentService commitments,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IMuseumService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Museum> BuildAsync(Guid ownerId, int? year, CancellationToken cancellationToken)
    {
        var today = await OwnerToday.GetAsync(settings, briefings, ownerId, clock.GetUtcNow(), cancellationToken);
        var chosen = year ?? today.Year;
        if (chosen > today.Year || chosen < today.Year - 9)
            throw new ArgumentException("Choose a year from the last ten.", nameof(year));
        var from = new DateOnly(chosen, 1, 1);
        var to = chosen == today.Year ? today : new DateOnly(chosen, 12, 31);
        bool InYear(DateTimeOffset at) => at.Year == chosen;

        var entries = await journal.ListAsync(ownerId, from, to, 2_000, cancellationToken);
        var habitList = await habits.ListAsync(ownerId, true, cancellationToken);
        var settled = (await decisions.ListAsync(ownerId, DecisionStatuses.Resolved, DecisionRules.MaxLimit,
            cancellationToken)).Where(d => d.ResolvedAt is { } at && InYear(at)).ToArray();
        var spending = (await expenses.ListAsync(ownerId, from, to, cancellationToken))
            .Where(e => e.Kind == "expense").ToArray();
        var saved = (await library.ListAsync(ownerId, new LibraryQuery(Limit: LibraryRules.MaxListLimit),
            cancellationToken)).Where(i => InYear(i.CreatedAt)).ToArray();
        var kept = (await commitments.ListAsync(ownerId, CommitmentDirections.IOwe, CommitmentStatuses.Done, false,
            cancellationToken)).Where(c => c.CompletedAt is { } at && InYear(at)).ToArray();

        var rooms = new List<MuseumRoom>();

        var best = entries.Where(e => e.Rating is not null)
            .OrderByDescending(e => e.Rating).ThenByDescending(e => e.Mood ?? 0).ThenBy(e => e.EntryDate)
            .Take(6).ToArray();
        if (best.Length > 0)
            rooms.Add(new MuseumRoom("best-days", "Best days", "The days you rated highest in your journal.",
                // Titled by what happened; the date is the exhibit's label, so it is not repeated.
                best.Select(e => new MuseumExhibit(
                    Shorten(string.IsNullOrWhiteSpace(e.Highlights) ? e.Content : e.Highlights!, 160), null,
                    $"{e.Rating}/10", e.EntryDate)).ToArray()));

        var gratitude = entries.Where(e => !string.IsNullOrWhiteSpace(e.Gratitude))
            .OrderByDescending(e => e.EntryDate).Take(6).ToArray();
        if (gratitude.Length > 0)
            rooms.Add(new MuseumRoom("gratitude", "Grateful for", "What you wrote down to be thankful for.",
                gratitude.Select(e => new MuseumExhibit(Shorten(e.Gratitude!, 120), null, null, e.EntryDate))
                    .ToArray()));

        var streaks = habitList.Where(h => h.Stats.BestStreak > 1)
            .OrderByDescending(h => h.Stats.BestStreak).Take(5).ToArray();
        if (streaks.Length > 0)
            rooms.Add(new MuseumRoom("streaks", "Longest streaks", "Your best runs, all time.",
                streaks.Select(h => new MuseumExhibit(h.Habit.Name,
                    $"{h.Stats.TotalCheckIns} check-ins in all",
                    $"{h.Stats.BestStreak} {h.Stats.StreakUnit}", null)).ToArray()));

        if (settled.Length > 0)
            rooms.Add(new MuseumRoom("calls", "Calls you made", "Predictions you settled this year.",
                settled.OrderByDescending(d => d.ResolvedAt).Take(6).Select(d => new MuseumExhibit(d.Title,
                    d.Prediction, $"{(d.Outcome == true ? "Happened" : "Didn’t happen")} · {Math.Round(d.Probability * 100)}% sure",
                    d.ResolvedAt is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : null)).ToArray()));

        if (kept.Length > 0)
            rooms.Add(new MuseumRoom("promises", "Promises kept", $"{kept.Length} kept this year. The latest:",
                kept.OrderByDescending(c => c.CompletedAt).Take(6).Select(c => new MuseumExhibit(
                    Shorten(c.Description, 100), $"For {c.Counterparty}", null,
                    DateOnly.FromDateTime(c.CompletedAt!.Value.UtcDateTime))).ToArray()));

        if (saved.Length > 0)
            rooms.Add(new MuseumRoom("library", "What caught your eye", $"{saved.Length} things saved to the library.",
                saved.OrderByDescending(i => i.CreatedAt).Take(6).Select(i => new MuseumExhibit(i.Title,
                    Shorten(i.Summary, 140), null, DateOnly.FromDateTime(i.CreatedAt.UtcDateTime))).ToArray()));

        var currency = spending.GroupBy(e => e.Currency.ToUpperInvariant()).OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
        if (currency is not null)
        {
            var categories = spending.Where(e => e.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Category).Select(g => (Category: g.Key, Total: g.Sum(e => e.Amount), Count: g.Count()))
                .OrderByDescending(x => x.Total).Take(5).ToArray();
            rooms.Add(new MuseumRoom("money", "Where the money went", "Your biggest spending categories.",
                categories.Select(c => new MuseumExhibit(c.Category, c.Count == 1 ? "1 payment" : $"{c.Count} payments",
                    Money(c.Total, currency), null)).ToArray()));
        }

        var numbers = new List<MuseumExhibit>();
        if (entries.Count > 0)
            numbers.Add(new MuseumExhibit("Journal entries", null, entries.Count.ToString(CultureInfo.InvariantCulture), null));
        var moods = entries.Where(e => e.Mood is not null).Select(e => (double)e.Mood!.Value).ToArray();
        if (moods.Length > 0)
            numbers.Add(new MuseumExhibit("Average mood", null,
                moods.Average().ToString("0.0", CultureInfo.InvariantCulture) + " of 5", null));
        if (kept.Length > 0)
            numbers.Add(new MuseumExhibit("Promises kept", null, kept.Length.ToString(CultureInfo.InvariantCulture), null));
        if (numbers.Count > 0)
            rooms.Insert(0, new MuseumRoom("numbers", $"{chosen} in numbers", "The entrance hall.", numbers));

        return new Museum(chosen, rooms, Enumerable.Range(0, 10).Select(i => today.Year - i).ToArray());
    }

    private static string Shorten(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }

    private static string Money(decimal amount, string currency) =>
        (currency switch { "EUR" => "€", "USD" => "$", "GBP" => "£", _ => currency + " " }) +
        amount.ToString("#,0", CultureInfo.InvariantCulture);
}

/// <summary>Counts the owner's own score in a duel, from their own records. Pure.</summary>
public static class DuelScoring
{
    public static int Count(string metric, DateOnly from, DateOnly to, IEnumerable<DateOnly> habitCheckIns,
        IEnumerable<DateOnly> journalDays, IEnumerable<DateOnly> promisesKept)
    {
        bool In(DateOnly d) => d >= from && d <= to;
        return metric switch
        {
            DuelMetrics.HabitCheckIns => habitCheckIns.Count(In),
            DuelMetrics.JournalDays => journalDays.Where(In).Distinct().Count(),
            DuelMetrics.PromisesKept => promisesKept.Count(In),
            _ => 0
        };
    }

    /// <summary>"won", "lost", "tied", or null while the duel runs or the friend's score is unknown.</summary>
    public static string? Outcome(string status, int mine, int? theirs) =>
        status != DuelStatuses.Finished || theirs is not { } other ? null
        : mine > other ? "won" : mine < other ? "lost" : "tied";

    public static string NewCode()
    {
        // No 0/O or 1/I, so a code read out loud or typed from a message survives.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[DuelRules.CodeLength];
        for (var i = 0; i < code.Length; i++) code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(code);
    }

    public static string? NormalizeCode(string? code)
    {
        var clean = new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length == DuelRules.CodeLength ? clean : null;
    }
}

/// <summary>Duels with friends' Jarvises: only the two scores cross over Agent2Agent.</summary>
public sealed class DuelService(
    IDuelRepository repository,
    IDuelPeerClient peers,
    Agents.IRemoteAgentRepository agents,
    IHabitService habits,
    IJournalService journal,
    ICommitmentService commitments,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IDuelService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<DuelView>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = await TodayAsync(ownerId, cancellationToken);
        var views = new List<DuelView>();
        foreach (var stored in await repository.ListAsync(ownerId, cancellationToken))
        {
            var duel = stored;
            if (duel.Status == DuelStatuses.Active)
            {
                var ended = today > duel.EndsOn;
                var stale = duel.TheirScoreAt is not { } at || now - at >= DuelRules.RefreshEvery || ended;
                if (stale) duel = await RefreshTheirsAsync(duel, now, cancellationToken);
                // A duel ends once its last day is over and the friend's final score has been read.
                if (ended && duel.TheirScoreAt is { } read && DateOnly.FromDateTime(read.UtcDateTime) > duel.EndsOn)
                    duel = duel with { Status = DuelStatuses.Finished };
                if (duel != stored) await repository.UpdateAsync(duel, cancellationToken);
            }

            views.Add(await ViewAsync(duel, cancellationToken));
        }

        return views.OrderBy(v => v.Status == DuelStatuses.Active ? 0 : 1).ThenByDescending(v => v.EndsOn).ToArray();
    }

    public async Task<DuelOperation> ChallengeAsync(Guid ownerId, Guid remoteAgentId, string metric, int days,
        CancellationToken cancellationToken)
    {
        if (!DuelMetrics.All.Contains(metric)) return DuelOperation.Invalid("metric", "Choose what to compete on.");
        if (days is < DuelRules.MinDays or > DuelRules.MaxDays)
            return DuelOperation.Invalid("days", $"A duel lasts {DuelRules.MinDays} to {DuelRules.MaxDays} days.");
        var peer = await agents.GetAsync(ownerId, remoteAgentId, cancellationToken);
        if (peer is null) return DuelOperation.Invalid("remoteAgentId", "Choose a friend’s Jarvis from Agents.");
        if (await ActiveCountAsync(ownerId, cancellationToken) >= DuelRules.MaxActive)
            return DuelOperation.Invalid("duel", $"You already have {DuelRules.MaxActive} duels running.");

        var today = await TodayAsync(ownerId, cancellationToken);
        var duel = new Duel(Guid.CreateVersion7(), ownerId, DuelScoring.NewCode(), DuelRoles.Challenger, peer.Id,
            peer.Name, metric, today, today.AddDays(days - 1), DuelStatuses.Active, null, null, null,
            clock.GetUtcNow());
        await repository.AddAsync(duel, cancellationToken);
        return new DuelOperation(await ViewAsync(duel, cancellationToken));
    }

    public async Task<DuelOperation> JoinAsync(Guid ownerId, Guid remoteAgentId, string code,
        CancellationToken cancellationToken)
    {
        if (DuelScoring.NormalizeCode(code) is not { } clean)
            return DuelOperation.Invalid("code", $"A duel code has {DuelRules.CodeLength} letters and digits.");
        var peer = await agents.GetAsync(ownerId, remoteAgentId, cancellationToken);
        if (peer is null) return DuelOperation.Invalid("remoteAgentId", "Choose a friend’s Jarvis from Agents.");
        if (await repository.FindAsync(ownerId, clean, DuelRoles.Joiner, cancellationToken) is not null)
            return DuelOperation.Invalid("code", "You already joined this duel.");
        if (await ActiveCountAsync(ownerId, cancellationToken) >= DuelRules.MaxActive)
            return DuelOperation.Invalid("duel", $"You already have {DuelRules.MaxActive} duels running.");

        PeerDuelTerms terms;
        try
        {
            terms = await peers.GetTermsAsync(ownerId, peer.Id, clean, cancellationToken);
        }
        catch (DuelPeerException exception)
        {
            return DuelOperation.Invalid("code", exception.Message);
        }

        var today = await TodayAsync(ownerId, cancellationToken);
        if (!DuelMetrics.All.Contains(terms.Metric) || terms.EndsOn < today ||
            terms.EndsOn.DayNumber - terms.StartsOn.DayNumber + 1 > DuelRules.MaxDays)
            return DuelOperation.Invalid("code", "That duel is over or not one Jarvis can join.");
        var duel = new Duel(Guid.CreateVersion7(), ownerId, clean, DuelRoles.Joiner, peer.Id, peer.Name,
            terms.Metric, terms.StartsOn, terms.EndsOn, DuelStatuses.Active, null, null, null, clock.GetUtcNow());
        await repository.AddAsync(duel, cancellationToken);
        duel = await RefreshTheirsAsync(duel, clock.GetUtcNow(), cancellationToken);
        await repository.UpdateAsync(duel, cancellationToken);
        return new DuelOperation(await ViewAsync(duel, cancellationToken));
    }

    public async Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var duel = await repository.GetAsync(id, ownerId, cancellationToken);
        if (duel is null || duel.Status != DuelStatuses.Active) return false;
        return await repository.UpdateAsync(duel with { Status = DuelStatuses.Cancelled }, cancellationToken);
    }

    public async Task<PeerDuelTerms?> TermsForPeerAsync(Guid ownerId, string code,
        CancellationToken cancellationToken)
    {
        if (DuelScoring.NormalizeCode(code) is not { } clean) return null;
        var duel = await repository.FindAsync(ownerId, clean, DuelRoles.Challenger, cancellationToken);
        return duel is { Status: DuelStatuses.Active } ? new PeerDuelTerms(duel.Metric, duel.StartsOn, duel.EndsOn) : null;
    }

    public async Task<int?> ScoreForPeerAsync(Guid ownerId, string code, string peerRole,
        CancellationToken cancellationToken)
    {
        if (DuelScoring.NormalizeCode(code) is not { } clean ||
            peerRole is not (DuelRoles.Challenger or DuelRoles.Joiner))
            return null;
        // The friend holds one side; the owner's row is the other.
        var duel = await repository.FindAsync(ownerId, clean, DuelRoles.Other(peerRole), cancellationToken);
        return duel is null || duel.Status == DuelStatuses.Cancelled ? null : await MyScoreAsync(duel, cancellationToken);
    }

    private async Task<Duel> RefreshTheirsAsync(Duel duel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var score = await peers.GetScoreAsync(duel.OwnerId, duel.RemoteAgentId, duel.Code, duel.Role,
                cancellationToken);
            return duel with { TheirScore = Math.Max(0, score), TheirScoreAt = now, LastError = null };
        }
        catch (DuelPeerException exception)
        {
            return duel with { LastError = exception.Message.Length > 200 ? exception.Message[..200] : exception.Message };
        }
    }

    private async Task<DuelView> ViewAsync(Duel duel, CancellationToken cancellationToken)
    {
        var mine = await MyScoreAsync(duel, cancellationToken);
        return new DuelView(duel.Id, duel.Code, duel.Role, duel.PeerName, duel.Metric, DuelMetrics.Label(duel.Metric),
            duel.StartsOn, duel.EndsOn, duel.Status, mine, duel.TheirScore, duel.TheirScoreAt,
            DuelScoring.Outcome(duel.Status, mine, duel.TheirScore), duel.LastError);
    }

    private async Task<int> MyScoreAsync(Duel duel, CancellationToken cancellationToken)
    {
        IEnumerable<DateOnly> checkIns = [];
        IEnumerable<DateOnly> journalDays = [];
        IEnumerable<DateOnly> kept = [];
        switch (duel.Metric)
        {
            case DuelMetrics.HabitCheckIns:
                checkIns = (await habits.ListAsync(duel.OwnerId, true, cancellationToken))
                    .SelectMany(h => h.Stats.RecentDates);
                break;
            case DuelMetrics.JournalDays:
                journalDays = (await journal.ListAsync(duel.OwnerId, duel.StartsOn, duel.EndsOn, 500,
                    cancellationToken)).Select(e => e.EntryDate);
                break;
            case DuelMetrics.PromisesKept:
                kept = (await commitments.ListAsync(duel.OwnerId, CommitmentDirections.IOwe,
                        CommitmentStatuses.Done, false, cancellationToken))
                    .Where(c => c.CompletedAt is not null)
                    .Select(c => DateOnly.FromDateTime(c.CompletedAt!.Value.UtcDateTime));
                break;
        }

        return DuelScoring.Count(duel.Metric, duel.StartsOn, duel.EndsOn, checkIns, journalDays, kept);
    }

    private async Task<int> ActiveCountAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(ownerId, cancellationToken)).Count(d => d.Status == DuelStatuses.Active);

    private Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken) =>
        OwnerToday.GetAsync(settings, briefings, ownerId, clock.GetUtcNow(), cancellationToken);
}
