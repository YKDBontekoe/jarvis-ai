namespace Jarvis.Application.Game;

/// <summary>
/// Writes the week's side quests from the owner's own data. The boss is the promise avoided longest; the regular
/// quests are one each of: another promise, someone drifting away, a habit to push, and writing in the journal.
/// </summary>
public static class QuestEngine
{
    /// <summary>A promise counts as avoided once it is overdue or has waited this long without a date.</summary>
    public const int BossAgeDays = 14;

    public static IReadOnlyList<QuestCandidate> Write(IReadOnlyList<PromiseInput> promises,
        IReadOnlyList<DriftInput> drifting, IReadOnlyList<HabitHistory> habits, DateOnly today)
    {
        var quests = new List<QuestCandidate>();
        var used = new HashSet<Guid>();

        var boss = promises
            .Where(p => p.DueOn is { } due ? due < today : today.DayNumber - p.CreatedOn.DayNumber >= BossAgeDays)
            .OrderBy(p => p.DueOn ?? p.CreatedOn).ThenBy(p => p.CreatedOn)
            .FirstOrDefault();
        if (boss is not null)
        {
            used.Add(boss.Id);
            quests.Add(new QuestCandidate(QuestKinds.Promise, PromiseKey(boss.Id), boss.Id, boss.Description,
                $"You promised {boss.Counterparty}. " + Waiting(boss, today), 1, GameRules.BossPoints, true));
        }

        var regular = new List<QuestCandidate>();
        var promise = promises.Where(p => !used.Contains(p.Id))
            .OrderBy(p => p.DueOn is null).ThenBy(p => p.DueOn).ThenBy(p => p.CreatedOn).FirstOrDefault();
        if (promise is not null)
            regular.Add(new QuestCandidate(QuestKinds.Promise, PromiseKey(promise.Id), promise.Id,
                $"Keep your promise to {promise.Counterparty}", promise.Description, 1, GameRules.QuestPoints,
                false));

        var person = drifting.OrderByDescending(d => d.Severity).ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (person is not null)
            regular.Add(new QuestCandidate(QuestKinds.Person, "person:" + person.PersonId.ToString("N"),
                person.PersonId, $"Get back in touch with {person.Name}", person.Headline, 1, GameRules.QuestPoints,
                false));

        if (HabitPush(habits, today) is { } habit) regular.Add(habit);

        regular.Add(new QuestCandidate(QuestKinds.Journal, "journal:" + GameRules.WeekStart(today).ToString("yyyy-MM-dd"),
            null, "Write in your journal on 3 days", "A few lines is enough; Jarvis keeps it in memory.", 3,
            GameRules.JournalQuestPoints, false));

        quests.AddRange(regular.Take(GameRules.RegularQuests));
        return quests;
    }

    /// <summary>The active habit done least over the last four weeks, with a target one above its usual week.</summary>
    public static QuestCandidate? HabitPush(IReadOnlyList<HabitHistory> habits, DateOnly today)
    {
        var weekStart = GameRules.WeekStart(today);
        var from = weekStart.AddDays(-28);
        var scored = habits
            .Where(h => !Enumerable.Range(0, 7).Any(d => h.Paused(weekStart.AddDays(d))))
            .Select(h =>
            {
                var perWeek = h.Dates.Count(d => d >= from && d < weekStart) / 4.0;
                var goal = h.Cadence == "weekly" ? h.TargetPerWeek : 7;
                return (Habit: h, PerWeek: perWeek, Ratio: goal == 0 ? 1 : perWeek / goal);
            })
            .Where(x => x.Ratio < 1)
            .OrderBy(x => x.Ratio).ThenBy(x => x.Habit.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (scored.Habit is null) return null;

        var habit = scored.Habit;
        var target = habit.Cadence == "weekly"
            ? Math.Max(1, habit.TargetPerWeek)
            : Math.Clamp((int)Math.Round(scored.PerWeek) + 1, 2, 7);
        var usual = scored.PerWeek < 0.5 ? "less than once a week" :
            $"about {Math.Round(scored.PerWeek):0} times a week";
        return new QuestCandidate(QuestKinds.Habit, BetEngine.SubjectKey(habit.Id), habit.Id,
            $"Do {habit.Name} {target} times this week", $"Lately you manage {usual}.", target,
            GameRules.QuestPoints, false);
    }

    public static string PromiseKey(Guid commitmentId) => "promise:" + commitmentId.ToString("N");

    private static string Waiting(PromiseInput promise, DateOnly today)
    {
        if (promise.DueOn is { } due)
        {
            var late = today.DayNumber - due.DayNumber;
            return late == 1 ? "It was due yesterday." : $"It was due {late} days ago.";
        }

        var days = today.DayNumber - promise.CreatedOn.DayNumber;
        return days >= 14 ? $"It has waited {days / 7} weeks." : $"It has waited {days} days.";
    }
}
