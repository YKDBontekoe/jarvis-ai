using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Knowing;
using Jarvis.Application.Memory;
using Jarvis.Domain.Knowing;
using Jarvis.Domain.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class KnowingTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static MemoryRecord Memory(string content, float confidence = 0.6f, string kind = "fact",
        bool pinned = false, DateTimeOffset? validUntil = null, Guid? supersededBy = null, int daysOld = 10) =>
        new(Guid.NewGuid(), Owner, kind, content, 0.5f, confidence, "conversation", null, Now.AddDays(-daysOld),
            Now.AddDays(-daysOld), validUntil, pinned, SupersededById: supersededBy);

    private static MemoryReview Review(Guid memoryId, string verdict, int daysAgo) =>
        new(Guid.NewGuid(), Owner, memoryId, verdict, "snapshot", Now.AddDays(-daysAgo));

    // ---- Train Jarvis ----

    [Fact]
    public void Cards_are_active_inferred_memories_least_sure_first_and_not_asked_recently()
    {
        var unsure = Memory("Lives in Utrecht", 0.4f);
        var sure = Memory("Has a dog called Pip", 0.9f);
        var asked = Memory("Works at Booking", 0.3f);
        var pinned = Memory("Allergic to nuts", 0.2f, pinned: true);
        var journal = Memory("Felt tired today", 0.1f, "journal");
        var ended = Memory("Drives a Golf", 0.1f, validUntil: Now.AddDays(-1));
        var replaced = Memory("Drives a Polo", 0.1f, supersededBy: Guid.NewGuid());
        var longAgo = Memory("Plays padel", 0.5f);

        var cards = TrainingEngine.Candidates([sure, unsure, asked, pinned, journal, ended, replaced, longAgo],
            [Review(asked.Id, MemoryVerdicts.Right, 10), Review(longAgo.Id, MemoryVerdicts.Right, 120)], Now);

        Assert.Equal(["Lives in Utrecht", "Plays padel", "Has a dog called Pip"], cards.Select(c => c.Content));
    }

    [Fact]
    public void Score_is_how_often_Jarvis_was_right_and_the_streak_counts_days_in_a_row()
    {
        var id = Guid.NewGuid();
        var reviews = new[]
        {
            Review(id, MemoryVerdicts.Right, 0), Review(id, MemoryVerdicts.Right, 0), Review(id, MemoryVerdicts.Wrong, 1),
            Review(id, MemoryVerdicts.Outdated, 2), Review(id, MemoryVerdicts.Right, 4)
        };

        var stats = TrainingEngine.Stats(reviews, 7, Today, at => DateOnly.FromDateTime(at.UtcDateTime));

        Assert.Equal(5, stats.Reviewed);
        Assert.Equal(60, stats.AccuracyPercent);
        Assert.Equal(2, stats.Today);
        Assert.Equal(3, stats.StreakDays);
        Assert.Equal(7, stats.ReviewableLeft);
        Assert.Null(TrainingEngine.Stats([], 0, Today, at => Today).AccuracyPercent);
    }

    [Fact]
    public void A_streak_survives_until_the_day_is_over()
    {
        Assert.Equal(2, TrainingEngine.Streak(new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2) }, Today));
        Assert.Equal(0, TrainingEngine.Streak(new HashSet<DateOnly> { Today.AddDays(-2) }, Today));
    }

    // ---- map of the unknown ----

    private static GraphEntityRecord Entity(string name, string type, int relations = 1) =>
        new(Guid.NewGuid(), name, type, null, [], relations, Now);

    [Fact]
    public void Gaps_are_the_basics_missing_for_the_best_known_people_and_projects()
    {
        var anna = Entity("Anna", "person", 9);
        var bob = Entity("Bob", "person", 2);
        var site = Entity("Website", "project", 4);
        var me = Entity("User", "person", 50);
        var place = Entity("Utrecht", "place", 7);
        var graph = new GraphOverview([anna, bob, site, me, place],
            [new GraphEdge(anna.Id, place.Id, "lives_in", 1, Now)],
            [new GraphLiteral(anna.Id, "works_at", "Booking", 1, Now), new GraphLiteral(site.Id, "status", "beta", 1, Now)]);

        var (gaps, entities) = GapEngine.Find(graph, new HashSet<string> { GapEngine.Key(bob.Id, "lives_in") });

        Assert.Equal(["Anna", "Website", "Bob"], entities.Select(e => e.Name));
        Assert.Equal((2, 3), (entities[0].Known, entities[0].Expected));
        Assert.Contains(gaps, g => g.EntityId == anna.Id && g.Predicate == "birthday" && g.Question == "When is Anna’s birthday?");
        Assert.DoesNotContain(gaps, g => g.EntityId == anna.Id && g.Predicate is "job_title" or "lives_in");
        Assert.Contains(gaps, g => g.EntityId == site.Id && g.Predicate == "deadline");
        // Bob's skipped gap is never asked, but it still counts as unknown.
        Assert.DoesNotContain(gaps, g => g.EntityId == bob.Id && g.Predicate == "lives_in");
        Assert.Equal(0, entities.Single(e => e.Name == "Bob").Known);
        Assert.True(GapEngine.IsKnownPredicate("person", "birthday"));
        Assert.False(GapEngine.IsKnownPredicate("person", "status"));
        Assert.Equal(("place", true), GapEngine.AnswerShape("lives_in"));
    }

    // ---- trust ladder ----

    private static DecidedApproval Decision(string tool, bool approved, int minutesAgo) =>
        new(tool, "{}", approved, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void Approvals_in_a_row_climb_and_a_decline_starts_again()
    {
        var decisions = new List<DecidedApproval>();
        for (var i = 0; i < 12; i++) decisions.Add(Decision("SendWhatsAppMessage", true, 1000 - i));
        decisions.Add(Decision("DeleteExpense", true, 500));
        decisions.Add(Decision("DeleteExpense", true, 400));
        decisions.Add(Decision("DeleteExpense", true, 300));
        decisions.Add(Decision("DeleteExpense", false, 200));
        decisions.Add(Decision("DeleteExpense", true, 100));
        for (var i = 0; i < 4; i++) decisions.Add(Decision("RunAutomation", true, 50 - i));

        var ladder = TrustEngine.Climb(decisions, [], Now);

        var whatsapp = ladder.Single(r => r.Category == "whatsapp.send");
        Assert.Equal(TrustLevels.Trusted, whatsapp.Level);
        Assert.True(whatsapp.CanHandOver);
        Assert.Equal(12, whatsapp.Streak);
        var expenses = ladder.Single(r => r.Category == "expenses.delete");
        Assert.Equal(TrustLevels.New, expenses.Level);
        Assert.Equal(1, expenses.Streak);
        Assert.Equal((4, 1), (expenses.Approved, expenses.Declined));
        Assert.Equal(TrustLevels.Learning, ladder.Single(r => r.Category == "automations.run").Level);
        Assert.Equal("whatsapp.send", ladder[0].Category);
    }

    [Fact]
    public void A_handed_over_category_is_hands_free_and_risky_ones_are_never_offered()
    {
        var decisions = Enumerable.Range(0, 12).SelectMany(i => new[]
        {
            Decision("SendWhatsAppMessage", true, 100 - i), Decision("RunCodingTask", true, 100 - i)
        }).ToArray();
        var grants = new[] { new StandingApprovalGrant("whatsapp.send", "Sending WhatsApp messages", Now, Now.AddDays(30)) };

        var ladder = TrustEngine.Climb(decisions, grants, Now);

        var whatsapp = ladder.Single(r => r.Category == "whatsapp.send");
        Assert.Equal(TrustLevels.HandsFree, whatsapp.Level);
        Assert.False(whatsapp.CanHandOver);
        Assert.Equal(Now.AddDays(30), whatsapp.HandedOverUntil);
        var coding = ladder.Single(r => r.Category == "coding.run");
        Assert.Equal(TrustLevels.Trusted, coding.Level);
        Assert.False(coding.CanHandOver);
        Assert.False(TrustEngine.MayHandOver("integrations.add"));
    }

    // ---- special chats ----

    [Fact]
    public void Each_special_chat_tells_the_model_how_to_behave_and_frames_owner_text_as_data()
    {
        Assert.Contains("never met", Jarvis.Agents.Knowing.ChatModeContextContributor.Stranger());
        using var council = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            question = "Should I take the job in Berlin?",
            advisers = new[] { "The sceptic", "Your accountant" }
        }));

        var text = Jarvis.Agents.Knowing.ChatModeContextContributor.Council(council.RootElement);

        Assert.StartsWith("Special chat: the council.", text, StringComparison.Ordinal);
        Assert.Contains("never decide for the owner", text);
        Assert.Contains("The question (owner's words, untrusted): Should I take the job in Berlin?", text);
        Assert.Contains("- Your accountant", text);
    }
}
