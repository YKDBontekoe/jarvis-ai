using Jarvis.Application.Home;
using Jarvis.Application.Inbox;
using Jarvis.Domain.Decisions;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeAttentionTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static Commitment Promise(string description, DateOnly? due, bool suggested = false,
        string status = CommitmentStatuses.Open, string direction = CommitmentDirections.IOwe,
        DateTimeOffset? created = null, Guid? reminder = null) =>
        new(Guid.CreateVersion7(), Owner, direction, "Rosa", description, due, status, suggested,
            CommitmentSources.Chat, null, reminder, null, created ?? Created, created ?? Created);

    private static Decision Choice(string title, DateOnly reviewOn, bool? outcome = null, Guid? reminder = null) =>
        new(Guid.CreateVersion7(), Owner, title, null, "It works", .7, reviewOn, outcome, null, null, reminder,
            Created, Created);

    [Fact]
    public void Suggested_promises_are_not_obligations()
    {
        var picked = HomeAttention.Commitments(
            [Promise("Send the deck", Today, suggested: true), Promise("Book the table", Today)], Today);

        Assert.Equal("Book the table", Assert.Single(picked).Description);
    }

    [Fact]
    public void Only_open_promises_due_today_or_earlier_are_picked()
    {
        var picked = HomeAttention.Commitments(
        [
            Promise("Done already", Today, status: CommitmentStatuses.Done),
            Promise("Dropped", Today, status: CommitmentStatuses.Dropped),
            Promise("Tomorrow", Today.AddDays(1)),
            Promise("No date", null),
            Promise("Today", Today)
        ], Today);

        Assert.Equal("Today", Assert.Single(picked).Description);
    }

    [Fact]
    public void Late_promises_come_first_longest_overdue_leading_and_say_how_late()
    {
        var picked = HomeAttention.Commitments(
        [
            Promise("Today", Today),
            Promise("Two days", Today.AddDays(-2)),
            Promise("Nine days", Today.AddDays(-9))
        ], Today);

        Assert.Equal(["Nine days", "Two days", "Today"], picked.Select(item => item.Description));
        Assert.Equal([9, 2, 0], picked.Select(item => item.DaysOverdue));
    }

    [Fact]
    public void What_I_owe_comes_before_what_is_owed_to_me_on_the_same_day()
    {
        var picked = HomeAttention.Commitments(
        [
            Promise("They owe", Today, direction: CommitmentDirections.OwedToMe),
            Promise("I owe", Today)
        ], Today);

        Assert.Equal(["I owe", "They owe"], picked.Select(item => item.Description));
    }

    [Fact]
    public void Promises_are_capped_so_they_cannot_bury_the_rest_of_Home()
    {
        var many = Enumerable.Range(0, 12).Select(day => Promise($"Promise {day}", Today.AddDays(-day))).ToArray();

        Assert.Equal(HomeAttention.MaxPerKind, HomeAttention.Commitments(many, Today).Count);
    }

    [Fact]
    public void A_promise_carries_the_reminder_that_would_otherwise_repeat_it()
    {
        var reminder = Guid.CreateVersion7();

        var picked = Assert.Single(HomeAttention.Commitments([Promise("Send it", Today, reminder: reminder)], Today));

        Assert.Equal(reminder, picked.ReminderId);
    }

    [Fact]
    public void A_decision_is_due_from_its_review_day_until_it_is_resolved()
    {
        var picked = HomeAttention.Decisions(
        [
            Choice("Due today", Today),
            Choice("Late", Today.AddDays(-3)),
            Choice("Later", Today.AddDays(1)),
            Choice("Resolved", Today.AddDays(-5), outcome: true)
        ], Today);

        Assert.Equal(["Late", "Due today"], picked.Select(item => item.Title));
        Assert.Equal([3, 0], picked.Select(item => item.DaysOverdue));
    }

    [Fact]
    public void Nothing_due_gives_empty_lists_not_invented_items()
    {
        Assert.Empty(HomeAttention.Commitments([], Today));
        Assert.Empty(HomeAttention.Decisions([], Today));
    }
}
