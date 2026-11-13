using Jarvis.Application.Game;
using Jarvis.Domain.Game;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class GameTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");

    // Monday 9 November 2026.
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static HabitHistory Habit(string name, IEnumerable<DateOnly> dates, string cadence = "daily",
        int target = 7, int createdWeeksAgo = 12, Func<DateOnly, bool>? paused = null) =>
        new(Guid.NewGuid(), name, cadence, target, Today.AddDays(-7 * createdWeeksAgo), dates.ToHashSet(),
            paused ?? (_ => false));

    /// <summary>Every day of the last <paramref name="days"/> days except the given weekdays.</summary>
    private static IEnumerable<DateOnly> EveryDayExcept(int days, params DayOfWeek[] skipped) =>
        Enumerable.Range(1, days).Select(d => Today.AddDays(-d)).Where(d => !skipped.Contains(d.DayOfWeek));

    private static GameBet Bet(string kind, DateOnly startsOn, DateOnly endsOn) =>
        new(Guid.NewGuid(), Owner, kind, "habit:x", null, "claim", "evidence", 0.8, 84, startsOn, endsOn,
            BetStatuses.Open, null, Now, Now);

    // ---- bets ----

    [Fact]
    public void Jarvis_bets_on_the_weekday_you_skip_most()
    {
        var habit = Habit("Exercise", EveryDayExcept(70, DayOfWeek.Thursday));

        var bet = BetEngine.DailyHabit(habit, Today);

        Assert.NotNull(bet);
        Assert.Equal(BetKinds.HabitDay, bet.Kind);
        Assert.Equal("You’ll skip Exercise on Thursday", bet.Claim);
        Assert.Equal(new DateOnly(2026, 11, 12), bet.EndsOn);
        Assert.Equal(bet.EndsOn, bet.StartsOn);
        Assert.Equal("You skipped it on 8 of your last 8 Thursdays.", bet.Evidence);
        Assert.Equal(0.95, bet.Confidence);
        Assert.Equal(BetEngine.SubjectKey(habit.Id), bet.SubjectKey);
    }

    [Fact]
    public void No_bet_on_a_habit_you_keep_or_one_too_new_to_judge()
    {
        Assert.Null(BetEngine.DailyHabit(Habit("Read", EveryDayExcept(70)), Today));
        // Three weeks of history is fewer than four chances per weekday.
        Assert.Null(BetEngine.DailyHabit(Habit("Read", [], createdWeeksAgo: 3), Today));
    }

    [Fact]
    public void A_paused_day_is_never_bet_on_and_paused_history_does_not_count()
    {
        var thursday = new DateOnly(2026, 11, 12);
        var habit = Habit("Exercise", EveryDayExcept(70, DayOfWeek.Thursday), paused: d => d == thursday);

        Assert.Null(BetEngine.DailyHabit(habit, Today));
    }

    [Fact]
    public void Weekly_target_bet_needs_time_left_in_the_week()
    {
        // One session a week against a target of three, for the last ten weeks.
        var dates = Enumerable.Range(1, 10).Select(w => Today.AddDays(-7 * w + 2));
        var habit = Habit("Swim", dates, "weekly", 3);

        var bet = BetEngine.WeeklyHabit(habit, Today);

        Assert.NotNull(bet);
        Assert.Equal("You won’t do Swim 3 times this week", bet.Claim);
        Assert.Equal(new DateOnly(2026, 11, 15), bet.EndsOn);
        Assert.Equal(0.95, bet.Confidence);
        Assert.Null(BetEngine.WeeklyHabit(habit, new DateOnly(2026, 11, 14)));
    }

    [Fact]
    public void Budget_bet_only_when_heading_over_with_time_to_turn_it_round()
    {
        var bet = BetEngine.Budget(new BudgetSnapshot("Takeaway", 300, 200, 450, "EUR"), Today);

        Assert.NotNull(bet);
        Assert.Equal("You’ll go over your Takeaway budget this month", bet.Claim);
        Assert.Equal("€200 of €300 spent, heading for €450.", bet.Evidence);
        Assert.Equal(0.9, bet.Confidence);
        Assert.Equal(new DateOnly(2026, 11, 30), bet.EndsOn);
        Assert.Equal("budget:takeaway:2026-11", bet.SubjectKey);
        Assert.Equal("takeaway", BetEngine.BudgetCategory(bet.SubjectKey));

        Assert.Null(BetEngine.Budget(new BudgetSnapshot("Takeaway", 300, 200, 280, "EUR"), Today));
        Assert.Null(BetEngine.Budget(new BudgetSnapshot("Takeaway", 300, 310, 450, "EUR"), Today));
        Assert.Null(BetEngine.Budget(new BudgetSnapshot("Takeaway", 300, 200, 450, "EUR"), new DateOnly(2026, 11, 27)));
    }

    [Fact]
    public void Propose_puts_the_surest_bets_first()
    {
        var weak = Habit("Stretch", EveryDayExcept(70, DayOfWeek.Friday)
            .Where(d => d.DayOfWeek != DayOfWeek.Friday || d.Day % 2 == 0));
        var strong = Habit("Exercise", EveryDayExcept(70, DayOfWeek.Thursday));

        var bets = BetEngine.Propose([weak, strong], [new BudgetSnapshot("Fun", 100, 50, 120, "EUR")], Today);

        Assert.Equal("You’ll skip Exercise on Thursday", bets[0].Claim);
        Assert.True(bets.Zip(bets.Skip(1)).All(p => p.First.Confidence >= p.Second.Confidence));
    }

    [Fact]
    public void Habit_bets_settle_from_check_ins()
    {
        var thursday = new DateOnly(2026, 11, 12);
        var bet = Bet(BetKinds.HabitDay, thursday, thursday);
        var done = Habit("Exercise", [thursday]);
        var skipped = Habit("Exercise", []);

        Assert.Equal(BetStatuses.Won, BetEngine.SettleHabit(bet, done, 1, thursday));
        Assert.Null(BetEngine.SettleHabit(bet, skipped, 1, thursday));
        Assert.Equal(BetStatuses.Lost, BetEngine.SettleHabit(bet, skipped, 1, thursday.AddDays(1)));
        Assert.Equal(BetStatuses.Void, BetEngine.SettleHabit(bet, Habit("Exercise", [], paused: d => d == thursday), 1, thursday.AddDays(1)));
        Assert.Equal(BetStatuses.Void, BetEngine.SettleHabit(bet, null, 1, thursday));

        var week = Bet(BetKinds.HabitWeek, Today, new DateOnly(2026, 11, 15));
        var three = Habit("Swim", [Today, Today.AddDays(1), Today.AddDays(3)], "weekly", 3);
        Assert.Equal(BetStatuses.Won, BetEngine.SettleHabit(week, three, 3, Today.AddDays(3)));
        Assert.Equal(BetStatuses.Lost, BetEngine.SettleHabit(week, Habit("Swim", [Today], "weekly", 3), 3, Today.AddDays(7)));
    }

    [Fact]
    public void Budget_bets_lose_the_moment_the_limit_is_crossed()
    {
        var bet = Bet(BetKinds.Budget, Today, new DateOnly(2026, 11, 30));

        Assert.Equal(BetStatuses.Lost, BetEngine.SettleBudget(bet, new BudgetSnapshot("Fun", 100, 101, 120, "EUR"), Today));
        Assert.Null(BetEngine.SettleBudget(bet, new BudgetSnapshot("Fun", 100, 90, 120, "EUR"), Today));
        Assert.Equal(BetStatuses.Won, BetEngine.SettleBudget(bet, new BudgetSnapshot("Fun", 100, 99, 99, "EUR"), new DateOnly(2026, 12, 1)));
        Assert.Equal(BetStatuses.Void, BetEngine.SettleBudget(bet, null, Today));
    }

    [Fact]
    public void Beating_a_confident_Jarvis_pays_more()
    {
        Assert.Equal(20, GameRules.PointsFor(0.5));
        Assert.Equal(52, GameRules.PointsFor(0.7));
        Assert.Equal(92, GameRules.PointsFor(0.95));
        Assert.Equal(92, GameRules.PointsFor(1.0));
    }

    [Fact]
    public void Levels_get_further_apart()
    {
        Assert.Equal(1, GameRules.LevelFor(0));
        Assert.Equal(1, GameRules.LevelFor(99));
        Assert.Equal(2, GameRules.LevelFor(100));
        Assert.Equal(3, GameRules.LevelFor(300));
        Assert.Equal(4, GameRules.LevelFor(999));
        Assert.Equal(5, GameRules.LevelFor(1000));
        Assert.Equal("Rookie", GameRules.TitleFor(1));
        Assert.Equal("Legend", GameRules.TitleFor(40));
    }

    [Fact]
    public void Score_counts_beaten_bets_and_finished_quests_only()
    {
        GameBet B(string status, int points) => Bet(BetKinds.HabitDay, Today, Today) with { Status = status, Points = points };
        GameQuest Q(string status, int points, bool boss = false) => new(Guid.NewGuid(), Owner, Today, QuestKinds.Habit,
            "k", null, "t", "d", 1, points, boss, status, null, Now, Now);

        var score = GameService.Score(
            [B(BetStatuses.Won, 60), B(BetStatuses.Lost, 80), B(BetStatuses.Void, 50), B(BetStatuses.Open, 40)],
            [Q(QuestStatuses.Done, 30), Q(QuestStatuses.Done, 100, true), Q(QuestStatuses.Skipped, 30)]);

        Assert.Equal(190, score.Points);
        Assert.Equal(2, score.Level);
        Assert.Equal("Contrarian", score.Title);
        Assert.Equal(100, score.LevelStart);
        Assert.Equal(300, score.NextLevelAt);
        Assert.Equal(1, score.BetsWon);
        Assert.Equal(1, score.BetsLost);
        Assert.Equal(2, score.QuestsDone);
        Assert.Equal(1, score.BossesBeaten);
    }

    // ---- quests ----

    [Fact]
    public void The_boss_is_the_promise_avoided_longest()
    {
        var old = new PromiseInput(Guid.NewGuid(), "Rosa", "Send the photos", null, Today.AddDays(-30));
        var overdue = new PromiseInput(Guid.NewGuid(), "Sam", "Return the drill", Today.AddDays(-40), Today.AddDays(-50));
        var fresh = new PromiseInput(Guid.NewGuid(), "Kim", "Book the table", Today.AddDays(3), Today.AddDays(-1));

        var quests = QuestEngine.Write([old, overdue, fresh], [], [], Today);

        var boss = Assert.Single(quests, q => q.IsBoss);
        Assert.Equal("Return the drill", boss.Title);
        Assert.Equal("You promised Sam. It was due 40 days ago.", boss.Detail);
        Assert.Equal(GameRules.BossPoints, boss.Points);
        var promise = Assert.Single(quests, q => q.Kind == QuestKinds.Promise && !q.IsBoss);
        Assert.Equal("Keep your promise to Kim", promise.Title);
    }

    [Fact]
    public void Regular_quests_take_one_of_each_kind_and_stop_at_three()
    {
        var promise = new PromiseInput(Guid.NewGuid(), "Kim", "Book the table", Today.AddDays(3), Today.AddDays(-1));
        var drift = new[]
        {
            new DriftInput(Guid.NewGuid(), "Noor", 1, "Quieter than usual"),
            new DriftInput(Guid.NewGuid(), "Lars", 3, "No message in 5 weeks")
        };
        var habit = Habit("Piano", Enumerable.Range(1, 28).Where(d => d % 14 == 0).Select(d => Today.AddDays(-d)));

        var quests = QuestEngine.Write([promise], drift, [habit], Today);

        Assert.Equal(3, quests.Count);
        Assert.DoesNotContain(quests, q => q.IsBoss);
        Assert.Equal([QuestKinds.Promise, QuestKinds.Person, QuestKinds.Habit], quests.Select(q => q.Kind));
        Assert.Equal("Get back in touch with Lars", quests[1].Title);
        Assert.Equal("No message in 5 weeks", quests[1].Detail);
        Assert.Equal("Do Piano 2 times this week", quests[2].Title);
        Assert.Equal(2, quests[2].Target);
    }

    [Fact]
    public void An_empty_week_still_gets_the_journal_quest()
    {
        var quest = Assert.Single(QuestEngine.Write([], [], [], Today));

        Assert.Equal(QuestKinds.Journal, quest.Kind);
        Assert.Equal(3, quest.Target);
        Assert.Equal("journal:2026-11-09", quest.SubjectKey);
    }

    [Fact]
    public void A_habit_you_already_keep_is_not_pushed()
    {
        Assert.Null(QuestEngine.HabitPush([Habit("Read", EveryDayExcept(35))], Today));
    }

    // ---- ghost ----

    [Fact]
    public void Ghost_compares_this_week_with_the_same_days_four_weeks_ago()
    {
        var wednesday = new DateOnly(2026, 11, 11);
        var ghostMonday = new DateOnly(2026, 10, 12);
        var habitDays = new[] { Today, Today.AddDays(1), wednesday, ghostMonday, new DateOnly(2026, 10, 20) };
        var spend = new[]
        {
            new GhostEngine.Spend(Today, 40, "EUR"), new GhostEngine.Spend(ghostMonday, 90, "EUR"),
            new GhostEngine.Spend(ghostMonday, 500, "USD")
        };

        var report = GhostEngine.Race(wednesday, habitDays, spend,
            [new GhostEngine.Mood(Today, 4)], [Today, Today], []);

        Assert.Equal(ghostMonday, report.GhostFrom);
        Assert.Equal(new DateOnly(2026, 10, 14), report.GhostTo);
        var habits = Assert.Single(report.Metrics, m => m.Key == "habits");
        Assert.Equal(3, habits.Now);
        // 20 October is a Tuesday after the ghost's Wednesday, so it does not count.
        Assert.Equal(1, habits.Ghost);
        Assert.Equal(GhostVerdicts.Ahead, habits.Verdict);
        var spent = Assert.Single(report.Metrics, m => m.Key == "spending");
        Assert.Equal("EUR", spent.Unit);
        Assert.Equal(40, spent.Now);
        Assert.Equal(90, spent.Ghost);
        Assert.Equal(GhostVerdicts.Ahead, spent.Verdict);
        Assert.Equal(1, Assert.Single(report.Metrics, m => m.Key == "journal").Now);
        // Mood needs both sides; promises had nothing on either side.
        Assert.DoesNotContain(report.Metrics, m => m.Key is "mood" or "promises");
        Assert.Equal(3, report.Ahead);
        Assert.Equal(0, report.Behind);
    }

    [Fact]
    public void Spending_more_than_your_ghost_is_behind()
    {
        var report = GhostEngine.Race(Today, [], [new GhostEngine.Spend(Today, 80, "EUR"),
            new GhostEngine.Spend(Today.AddDays(-28), 30, "EUR")], [], [], []);

        Assert.Equal(GhostVerdicts.Behind, Assert.Single(report.Metrics).Verdict);
        Assert.Equal(1, report.Behind);
    }

    [Fact]
    public void Chat_about_quests_or_bets_reaches_the_game_tools()
    {
        var game = Jarvis.Agents.ToolGroups.All.Single(g => g.Id == "game");

        Assert.Equal(["GetGameBoard", "CompleteQuest"], game.Tools);
        Assert.Matches(game.Keywords, "what are my quests this week?");
        Assert.Matches(game.Keywords, "what did you bet against me");
        Assert.Matches(game.Keywords, "ben ik mijn ghost voor?");
        Assert.DoesNotMatch(game.Keywords, "remind me to call mum");
    }
}
