using System.Text.Json;
using Jarvis.Application.Home;
using Jarvis.Application.Improvements;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Improvements;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeLearningTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000e801");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 16, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ImprovementProposal Proposal(string kind, string status, object payload, string title = "Title",
        double confidence = 0.6, int daysAgo = 1) =>
        new(Guid.NewGuid(), Owner, kind, $"{kind}:{Guid.NewGuid():N}", title, "Evidence for it.", confidence,
            JsonSerializer.Serialize(payload, Json), status, null, Now.AddDays(-daysAgo), Now.AddDays(-daysAgo));

    private static SkillRecord Skill(string name, string status = SkillStatuses.Active) =>
        new(Guid.NewGuid(), Owner, name, $"Use {name} when asked.", "1. Do the steps in order.", SkillSources.Learned,
            status, false, 1, 0, null, Now, Now);

    private static NotificationRecord Note(string type, Guid source, int daysAgo = 1, string body = "Because.") =>
        new(Guid.NewGuid(), type, "Title", body, source, Now.AddDays(-daysAgo), null);

    [Fact]
    public void Nothing_to_show_leaves_home_as_it_was()
    {
        Assert.Null(HomeLearning.Build([], [], [], Now));
    }

    [Fact]
    public void A_tested_app_leads_the_suggestions_with_its_own_words()
    {
        var app = Proposal(ImprovementKinds.Capability, ImprovementStatuses.Pending,
            new CapabilityPayload("live-train-departures", 3, 2, CapabilityBuildStatuses.Built, Guid.NewGuid(), "sha",
                "Trains"), confidence: 0.5);
        var skill = Proposal(ImprovementKinds.Skill, ImprovementStatuses.Pending,
            new SkillPayload("trip-plan", "Plan trips.", "1. Ask dates."), "Save “trip-plan” as a skill", 0.9);
        var dismissed = Proposal(ImprovementKinds.Memory, ImprovementStatuses.Dismissed, new { }, confidence: 1);

        var learning = HomeLearning.Build([skill, app, dismissed], [], [], Now)!;

        Assert.Equal(["Switch on “Trains”", "Save “trip-plan” as a skill"], learning.Waiting.Select(w => w.Title));
        Assert.Equal("An app Jarvis built and tested for live train departures", learning.Waiting[0].Detail);
        Assert.Equal(["Switch on", "Save skill"], learning.Waiting.Select(w => w.AcceptLabel));
        Assert.Empty(learning.Changes);
    }

    [Fact]
    public void Changes_Jarvis_made_itself_come_newest_first_with_the_step_that_reverses_each()
    {
        var learned = Skill("weekly-review");
        var retired = Skill("old-meal-plan", SkillStatuses.Disabled);
        var lesson = Proposal(ImprovementKinds.Lesson, ImprovementStatuses.Applied,
            new LessonPayload("Use the owner's time zone.", ["CreateReminder"]), daysAgo: 0);
        var accepted = Proposal(ImprovementKinds.Memory, ImprovementStatuses.Accepted, new { }, daysAgo: 0);

        var learning = HomeLearning.Build([lesson, accepted],
            [Note("skill.learned", learned.Id, 2), Note("skill.retired", retired.Id, 3, "Went well 1 of 6 times.")],
            [learned, retired], Now)!;

        Assert.Equal(3, learning.ChangesThisWeek);
        Assert.Equal(["New rule for create reminder", "Learned “weekly-review”", "Turned off “old-meal-plan”"],
            learning.Changes.Select(change => change.Title));
        Assert.Equal([HomeLearningActions.Undo, HomeLearningActions.DisableSkill, HomeLearningActions.EnableSkill],
            learning.Changes.Select(change => change.Action));
        Assert.Equal(lesson.Id.ToString(), learning.Changes[0].Target);
        Assert.Equal(learned.Id.ToString(), learning.Changes[1].Target);
        Assert.Equal("Went well 1 of 6 times.", learning.Changes[2].Detail);
    }

    [Fact]
    public void Old_changes_and_ones_the_owner_already_reversed_are_left_out()
    {
        var learned = Skill("weekly-review", SkillStatuses.Disabled); // the owner turned it off since
        var reEnabled = Skill("old-meal-plan"); // the owner turned it back on
        var stale = Skill("last-month");

        var learning = HomeLearning.Build([],
            [Note("skill.learned", learned.Id), Note("skill.retired", reEnabled.Id), Note("skill.learned", stale.Id, 9)],
            [learned, reEnabled, stale], Now);

        Assert.Null(learning);
    }

    [Fact]
    public void At_most_three_changes_are_listed_but_all_are_counted()
    {
        var skills = Enumerable.Range(0, 5).Select(index => Skill($"skill-{index}")).ToArray();

        var learning = HomeLearning.Build([], skills.Select((skill, index) => Note("skill.learned", skill.Id, index)).ToArray(),
            skills, Now)!;

        Assert.Equal(5, learning.ChangesThisWeek);
        Assert.Equal(HomeLearning.MaxChanges, learning.Changes.Count);
        Assert.Equal("Learned “skill-0”", learning.Changes[0].Title);
    }
}
