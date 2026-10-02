using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PlaceReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    private static Reminder Place(string trigger = Reminder.LocationArrive, bool repeats = false) =>
        Reminder.ForPlace(Guid.CreateVersion7(), "Buy milk", "Supermarket", 52.1, 5.1, 150, trigger, repeats, "UTC");

    [Fact]
    public void Arrive_fires_once_when_the_phone_enters_the_radius()
    {
        var reminder = Place();
        Assert.False(reminder.ObservePosition(2_000, 20, Now));
        Assert.True(reminder.ObservePosition(80, 20, Now.AddMinutes(10)));

        reminder.CompleteLocationOccurrence(Now.AddMinutes(10));
        Assert.Equal("completed", reminder.Status);
        Assert.False(reminder.ObservePosition(2_000, 20, Now.AddMinutes(20)));
        Assert.False(reminder.ObservePosition(80, 20, Now.AddMinutes(30)));
    }

    [Fact]
    public void Arrive_fires_on_the_first_fix_inside_when_nothing_was_known()
    {
        Assert.True(Place().ObservePosition(10, 15, Now));
    }

    [Fact]
    public void A_reminder_made_at_the_place_waits_for_the_next_visit()
    {
        var reminder = Place();
        reminder.SeedLocationState(30, 10);
        Assert.False(reminder.ObservePosition(40, 10, Now));
        Assert.False(reminder.ObservePosition(1_000, 10, Now.AddMinutes(5)));
        Assert.True(reminder.ObservePosition(40, 10, Now.AddMinutes(40)));
    }

    [Fact]
    public void Leave_fires_only_after_being_inside()
    {
        var reminder = Place(Reminder.LocationLeave);
        Assert.False(reminder.ObservePosition(1_000, 10, Now));
        Assert.False(reminder.ObservePosition(50, 10, Now.AddMinutes(1)));
        Assert.True(reminder.ObservePosition(1_000, 10, Now.AddMinutes(2)));
    }

    [Fact]
    public void Positions_just_outside_the_edge_and_vague_fixes_change_nothing()
    {
        var reminder = Place();
        Assert.False(reminder.ObservePosition(170, 10, Now));
        Assert.Null(reminder.LocationInside);
        Assert.False(reminder.ObservePosition(50, 1_500, Now));
        Assert.Null(reminder.LocationInside);
    }

    [Fact]
    public void Every_visit_reminders_stay_pending_and_respect_the_cooldown()
    {
        var reminder = Place(repeats: true);
        Assert.True(reminder.ObservePosition(50, 10, Now));
        reminder.CompleteLocationOccurrence(Now);
        Assert.Equal("pending", reminder.Status);

        Assert.False(reminder.ObservePosition(1_000, 10, Now.AddMinutes(5)));
        Assert.False(reminder.ObservePosition(50, 10, Now.AddMinutes(10)));
        Assert.False(reminder.ObservePosition(1_000, 10, Now.AddMinutes(50)));
        Assert.True(reminder.ObservePosition(50, 10, Now.AddMinutes(60)));
        Assert.Throws<InvalidOperationException>(reminder.MarkDone);
    }

    [Fact]
    public void Snoozing_a_one_time_place_reminder_makes_it_timed()
    {
        var reminder = Place();
        reminder.CompleteLocationOccurrence(Now);
        reminder.Snooze(Now.AddHours(1), new TimeOnly(19, 0));
        Assert.False(reminder.IsLocationBased);
        Assert.Equal("pending", reminder.Status);
        Assert.Equal(Now.AddHours(1), reminder.DueAt);
    }

    [Theory]
    [InlineData(91, 5, 150, Reminder.LocationArrive, "Home")]
    [InlineData(52, 5, 10, Reminder.LocationArrive, "Home")]
    [InlineData(52, 5, 150, "near", "Home")]
    [InlineData(52, 5, 150, Reminder.LocationArrive, " ")]
    public void Invalid_places_are_rejected(double latitude, double longitude, double radius, string trigger,
        string name)
    {
        Assert.Throws<ArgumentException>(() => Reminder.ForPlace(Guid.CreateVersion7(), "x", name, latitude,
            longitude, radius, trigger, false, "UTC"));
    }

    [Fact]
    public void Record_carries_the_place()
    {
        var record = Place(Reminder.LocationLeave, true).ToRecord();
        Assert.Equal(new ReminderPlace("Supermarket", 52.1, 5.1, 150, Reminder.LocationLeave, true), record.Place);
        Assert.Null(new Reminder(Guid.CreateVersion7(), "x", Now).ToRecord().Place);
    }
}
