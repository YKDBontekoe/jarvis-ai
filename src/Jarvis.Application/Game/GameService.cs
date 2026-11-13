using Jarvis.Application.Expenses;
using Jarvis.Application.Finance;
using Jarvis.Application.Habits;
using Jarvis.Application.Inbox;
using Jarvis.Application.Journal;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Preferences;
using Jarvis.Application.Settings;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Game;

namespace Jarvis.Application.Game;

/// <summary>
/// The game around the owner's own life: Jarvis's bets against them, the week's side quests, the race against last
/// month's self, and one scoreboard. Everything is read from the owner's data and settles itself; nothing here
/// changes anything outside the game.
/// </summary>
public sealed class GameService(
    IGameRepository repository,
    IHabitService habits,
    IFinanceService finance,
    ICommitmentService commitments,
    IRelationshipRadarService radar,
    IJournalService journal,
    IExpenseRepository expenses,
    INotificationRepository notifications,
    IDailyBriefingRepository briefings,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null) : IGameService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<GameBoard> BoardAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var (today, zone) = await TodayAsync(ownerId, cancellationToken);
        var history = await HabitsAsync(ownerId, zone, cancellationToken);
        await RefreshAsync(ownerId, today, history, cancellationToken);

        var bets = await repository.ListBetsAsync(ownerId, cancellationToken);
        var quests = await repository.ListQuestsAsync(ownerId, cancellationToken);
        var weekStart = GameRules.WeekStart(today);
        var journalDays = await JournalDaysAsync(ownerId, weekStart, today, cancellationToken);

        var shownBets = bets.Where(b => b.IsOpen || (b.Status != BetStatuses.Void &&
                                                     b.ResolvedAt is { } at && at >= clock.GetUtcNow().AddDays(-14)))
            .OrderBy(b => !b.IsOpen).ThenBy(b => b.IsOpen ? b.EndsOn.DayNumber : -b.EndsOn.DayNumber)
            .Take(8).Select(ToView).ToArray();
        var shownQuests = quests.Where(q => q.WeekStart == weekStart)
            .OrderByDescending(q => q.IsBoss).ThenBy(q => q.CreatedAt)
            .Select(q => ToView(q, Progress(q, history, journalDays, weekStart, today))).ToArray();

        return new GameBoard(today, Score(bets, quests), shownBets, shownQuests,
            await GhostAsync(ownerId, today, history, cancellationToken));
    }

    public async Task<GameRefreshResult> RefreshAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var (today, zone) = await TodayAsync(ownerId, cancellationToken);
        return await RefreshAsync(ownerId, today, await HabitsAsync(ownerId, zone, cancellationToken),
            cancellationToken);
    }

    public async Task<GameQuestView?> CompleteQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await CloseQuestAsync(id, ownerId, QuestStatuses.Done, cancellationToken);

    public async Task<GameQuestView?> SkipQuestAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await CloseQuestAsync(id, ownerId, QuestStatuses.Skipped, cancellationToken);

    public async Task<GameBetView?> CallOffBetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var bet = await repository.GetBetAsync(id, ownerId, cancellationToken);
        if (bet is null || !bet.IsOpen) return null;
        var now = clock.GetUtcNow();
        var off = bet with { Status = BetStatuses.Void, ResolvedAt = now, UpdatedAt = now };
        return await repository.UpdateBetAsync(off, cancellationToken) ? ToView(off) : null;
    }

    private async Task<GameQuestView?> CloseQuestAsync(Guid id, Guid ownerId, string status,
        CancellationToken cancellationToken)
    {
        var quest = await repository.GetQuestAsync(id, ownerId, cancellationToken);
        if (quest is null || !quest.IsOpen) return null;
        var now = clock.GetUtcNow();
        var closed = quest with
        {
            Status = status, CompletedAt = status == QuestStatuses.Done ? now : null, UpdatedAt = now
        };
        if (!await repository.UpdateQuestAsync(closed, cancellationToken)) return null;
        return ToView(closed, status == QuestStatuses.Done ? closed.Target : 0);
    }

    private async Task<GameRefreshResult> RefreshAsync(Guid ownerId, DateOnly today,
        IReadOnlyList<HabitHistory> history, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var bets = await repository.ListBetsAsync(ownerId, cancellationToken);
        var byHabit = history.ToDictionary(h => h.Id);
        var budgetsByMonth = new Dictionary<(int, int), IReadOnlyList<BudgetSnapshot>>();

        async Task<IReadOnlyList<BudgetSnapshot>> BudgetsAsync(int year, int month)
        {
            if (budgetsByMonth.TryGetValue((year, month), out var cached)) return cached;
            var list = (await finance.BudgetStatusAsync(ownerId, year, month, today, cancellationToken))
                .Select(b => new BudgetSnapshot(b.Category, b.Limit, b.Spent, b.Projected, b.Currency)).ToArray();
            budgetsByMonth[(year, month)] = list;
            return list;
        }

        // 1. Settle what the data already decides.
        var settled = 0;
        foreach (var bet in bets.Where(b => b.IsOpen))
        {
            string? status;
            if (bet.Kind == BetKinds.Budget)
            {
                var category = BetEngine.BudgetCategory(bet.SubjectKey);
                var budget = (await BudgetsAsync(bet.EndsOn.Year, bet.EndsOn.Month))
                    .FirstOrDefault(b => b.Category.Trim().Equals(category, StringComparison.OrdinalIgnoreCase));
                status = BetEngine.SettleBudget(bet, budget, today);
            }
            else
            {
                var habit = bet.SubjectId is { } habitId ? byHabit.GetValueOrDefault(habitId) : null;
                status = BetEngine.SettleHabit(bet, habit, habit?.TargetPerWeek ?? 1, today);
            }

            if (status is null) continue;
            await repository.UpdateBetAsync(bet with { Status = status, ResolvedAt = now, UpdatedAt = now },
                cancellationToken);
            settled++;
        }

        // 2. Place new bets up to the limit, never on something just bet on or called off.
        bets = await repository.ListBetsAsync(ownerId, cancellationToken);
        var room = GameRules.MaxOpenBets - bets.Count(b => b.IsOpen);
        var placed = new List<GameBet>();
        if (room > 0)
        {
            var cooling = bets.Where(b => b.IsOpen || b.CreatedAt >= now.AddDays(-GameRules.SubjectCooldownDays))
                .Select(b => b.SubjectKey).ToHashSet(StringComparer.Ordinal);
            var budgets = await BudgetsAsync(today.Year, today.Month);
            foreach (var candidate in BetEngine.Propose(history, budgets, today))
            {
                if (placed.Count >= room) break;
                if (!cooling.Add(candidate.SubjectKey)) continue;
                placed.Add(new GameBet(Guid.CreateVersion7(), ownerId, candidate.Kind, candidate.SubjectKey,
                    candidate.SubjectId, Cut(candidate.Claim, 300), Cut(candidate.Evidence, 500), candidate.Confidence,
                    GameRules.PointsFor(candidate.Confidence), candidate.StartsOn, candidate.EndsOn, BetStatuses.Open,
                    null, now, now));
            }

            if (placed.Count > 0) await repository.AddBetsAsync(placed, cancellationToken);
        }

        // 3. The week's quests, written once per week.
        var weekStart = GameRules.WeekStart(today);
        var quests = await repository.ListQuestsAsync(ownerId, cancellationToken);
        var created = 0;
        if (!quests.Any(q => q.WeekStart == weekStart))
        {
            var written = QuestEngine.Write(await PromisesAsync(ownerId, cancellationToken),
                await DriftingAsync(ownerId, cancellationToken), history, today);
            var rows = written.Select(q => new GameQuest(Guid.CreateVersion7(), ownerId, weekStart, q.Kind,
                q.SubjectKey, q.SubjectId, Cut(q.Title, 300), Cut(q.Detail, 500), q.Target, q.Points, q.IsBoss, QuestStatuses.Open, null,
                now, now)).ToArray();
            if (rows.Length > 0) await repository.AddQuestsAsync(rows, cancellationToken);
            created = rows.Length;
            quests = await repository.ListQuestsAsync(ownerId, cancellationToken);
        }

        // 4. Quests the data shows are done complete themselves.
        var completed = 0;
        var open = quests.Where(q => q.WeekStart == weekStart && q.IsOpen).ToArray();
        if (open.Length > 0)
        {
            var journalDays = await JournalDaysAsync(ownerId, weekStart, today, cancellationToken);
            var kept = open.Any(q => q.Kind == QuestKinds.Promise)
                ? (await commitments.ListAsync(ownerId, CommitmentDirections.IOwe, CommitmentStatuses.Done, false,
                    cancellationToken)).Select(c => c.Id).ToHashSet()
                : [];
            foreach (var quest in open)
            {
                var done = quest.Kind == QuestKinds.Promise
                    ? quest.SubjectId is { } promiseId && kept.Contains(promiseId)
                    : quest.Kind != QuestKinds.Person &&
                      Progress(quest, history, journalDays, weekStart, today) >= quest.Target;
                if (!done) continue;
                await repository.UpdateQuestAsync(quest with
                {
                    Status = QuestStatuses.Done, CompletedAt = now, UpdatedAt = now
                }, cancellationToken);
                completed++;
            }
        }

        if (placed.Count > 0) await NotifyAsync(ownerId, placed[0], today, cancellationToken);
        return new GameRefreshResult(placed.Count, settled, created, completed);
    }

    private async Task NotifyAsync(Guid ownerId, GameBet bet, DateOnly today, CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<GameState>(ownerId, SettingsSections.Game, cancellationToken)
                    ?? new GameState();
        if (state.LastBetNotifiedOn == today) return;
        try
        {
            await notifications.CreateAsync(ownerId, GameRules.NotificationType, "Jarvis is betting against you",
                $"{bet.Claim}. Prove it wrong for {bet.Points} points.", bet.Id, cancellationToken);
            await settings.SaveAsync(ownerId, SettingsSections.Game, state with { LastBetNotifiedOn = today },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The bet stands even when the push could not be queued.
        }
    }

    private static int Progress(GameQuest quest, IReadOnlyList<HabitHistory> history,
        IReadOnlyCollection<DateOnly> journalDays, DateOnly weekStart, DateOnly today)
    {
        if (quest.Status == QuestStatuses.Done) return quest.Target;
        return quest.Kind switch
        {
            QuestKinds.Habit => history.FirstOrDefault(h => h.Id == quest.SubjectId) is { } habit
                ? Math.Min(quest.Target, habit.Dates.Count(d => d >= weekStart && d <= today))
                : 0,
            QuestKinds.Journal => Math.Min(quest.Target, journalDays.Count),
            _ => 0
        };
    }

    private async Task<GhostReport> GhostAsync(Guid ownerId, DateOnly today, IReadOnlyList<HabitHistory> history,
        CancellationToken cancellationToken)
    {
        var from = GameRules.WeekStart(today).AddDays(-GameRules.GhostLagDays);
        var spending = (await expenses.ListAsync(ownerId, from, today, cancellationToken))
            .Where(e => e.Kind == "expense")
            .Select(e => new GhostEngine.Spend(e.SpentOn, e.Amount, e.Currency)).ToArray();
        var entries = await journal.ListAsync(ownerId, from, today, 500, cancellationToken);
        var kept = (await commitments.ListAsync(ownerId, CommitmentDirections.IOwe, CommitmentStatuses.Done, false,
                cancellationToken))
            .Where(c => c.CompletedAt is not null)
            .Select(c => DateOnly.FromDateTime(c.CompletedAt!.Value.UtcDateTime)).ToArray();
        return GhostEngine.Race(today, history.SelectMany(h => h.Dates).ToArray(), spending,
            entries.Where(e => e.Mood is not null).Select(e => new GhostEngine.Mood(e.EntryDate, e.Mood!.Value))
                .ToArray(),
            entries.Select(e => e.EntryDate).ToArray(), kept);
    }

    private async Task<IReadOnlyList<HabitHistory>> HabitsAsync(Guid ownerId, TimeZoneInfo zone,
        CancellationToken cancellationToken) =>
        (await habits.ListAsync(ownerId, false, cancellationToken))
        .Select(s =>
        {
            // Check-ins recorded for days before the habit was added (an import) count as its history too.
            var created = TimelineRules.LocalDate(s.Habit.CreatedAt, zone);
            var first = s.Stats.RecentDates.Count == 0 ? created : s.Stats.RecentDates.Min();
            return new HabitHistory(s.Habit.Id, s.Habit.Name, s.Habit.Cadence, s.Habit.TargetPerWeek,
                first < created ? first : created, s.Stats.RecentDates.ToHashSet(), s.Habit.IsPausedOn);
        })
        .ToArray();

    private async Task<IReadOnlyList<PromiseInput>> PromisesAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await commitments.ListAsync(ownerId, CommitmentDirections.IOwe, CommitmentStatuses.Open, false,
            cancellationToken))
        .Select(c => new PromiseInput(c.Id, c.Counterparty, c.Description, c.DueOn,
            DateOnly.FromDateTime(c.CreatedAt.UtcDateTime)))
        .ToArray();

    private async Task<IReadOnlyList<DriftInput>> DriftingAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        try
        {
            return (await radar.OverviewAsync(ownerId, cancellationToken)).People
                .Where(p => p.Signals.Count > 0)
                .Select(p => new DriftInput(p.PersonId, p.Name, p.Severity,
                    p.Signals.OrderByDescending(s => s.Severity).First().Headline))
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Quests are still worth writing without the radar.
            return [];
        }
    }

    private async Task<IReadOnlyCollection<DateOnly>> JournalDaysAsync(Guid ownerId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) =>
        (await journal.ListAsync(ownerId, from, to, 200, cancellationToken)).Select(e => e.EntryDate).Distinct()
        .ToArray();

    private async Task<(DateOnly Today, TimeZoneInfo Zone)> TodayAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var zoneId = await OwnerZoneResolver.ZoneIdAsync(settings, briefings, ownerId, cancellationToken);
        LocalClock.TryFind(zoneId, out var zone);
        return (TimelineRules.LocalDate(clock.GetUtcNow(), zone), zone);
    }

    public static GameScore Score(IReadOnlyList<GameBet> bets, IReadOnlyList<GameQuest> quests)
    {
        var won = bets.Where(b => b.Status == BetStatuses.Won).ToArray();
        var done = quests.Where(q => q.Status == QuestStatuses.Done).ToArray();
        var points = won.Sum(b => b.Points) + done.Sum(q => q.Points);
        var level = GameRules.LevelFor(points);
        return new GameScore(points, level, GameRules.TitleFor(level), GameRules.LevelStart(level),
            GameRules.LevelStart(level + 1), won.Length, bets.Count(b => b.Status == BetStatuses.Lost), done.Length,
            done.Count(q => q.IsBoss));
    }

    private static string Cut(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }

    private static GameBetView ToView(GameBet b) => new(b.Id, b.Kind, b.Claim, b.Evidence,
        (int)Math.Round(b.Confidence * 100), b.Points, b.StartsOn, b.EndsOn, b.Status, b.ResolvedAt);

    private static GameQuestView ToView(GameQuest q, int progress) => new(q.Id, q.Kind, q.Title, q.Detail, progress,
        q.Target, q.Points, q.IsBoss, q.Status, q.CompletedAt, q.SubjectId);
}
