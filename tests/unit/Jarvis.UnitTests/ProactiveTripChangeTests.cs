using Jarvis.Application.Proactivity;
using Jarvis.Application.Trips;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProactiveTripChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProactiveSource Watch = new("watch:" + Guid.NewGuid(), "Flight delay", "v2", "active", "60 minutes",
        At: Now, NumericValue: 60, ValidUntil: Now.AddHours(1));
    private static readonly IReadOnlyList<TripItem> Items = [
        new("flight", new(2026, 11, 2), new(9, 0), new(11, 0), "Flight", TripKinds.Travel, null, 0, null, null, true, "https://example.org/flight"),
        new("museum", new(2026, 11, 2), new(11, 15), new(12, 15), "Museum", TripKinds.Activity, null, 15, null, new(14, 0), true, "https://example.org/museum"),
        new("connection", new(2026, 11, 2), new(13, 0), new(13, 30), "Reserved train", TripKinds.Travel, null, 15, null, null, true, "https://example.org/train")];
    private static readonly ProactiveSource Trip = new("trip:" + Guid.NewGuid(), "Weekend away", "v1", "planned", "Trip", TripItems: Items);
    private static ProactiveCase Case() => new(Guid.NewGuid(), Guid.NewGuid(), "fixture", "trip", "Trip", "Keep the trip useful",
        "open", true, null, Trip.Ref, [Trip.Ref, Watch.Ref], null, new(), [],
        [new("delay", "Flight schedule", Watch.Ref, Effect: "delay_minutes", TripRef: Trip.Ref, TripItemId: "flight",
            BaselineDate: new(2026, 11, 2), BaselineStart: new(9, 0), BaselineEnd: new(11, 0))],
        null, null, "Review", [Trip, Watch], [], [], [], Now, Now, null, Now);

    [Fact]
    public void A_confirmed_delay_identifies_affected_items_and_proposes_times_without_moving_a_reserved_connection()
    {
        var lines = ProactiveTripChanges.Build(Case(), [Trip, Watch], Now);
        Assert.Contains(lines, line => line.Contains("Flight") && line.Contains("2026-11-02 10:00") && line.Contains("2026-11-02 12:00"));
        Assert.Contains(lines, line => line.Contains("Museum") && line.Contains("12:15") && line.Contains("13:15"));
        Assert.Contains(lines, line => line.Contains("Reserved train") && line.Contains("cannot be reached") && line.Contains("13:30"));
        Assert.Equal(new TimeOnly(9, 0), Items[0].Start);
        Assert.Equal(new TimeOnly(13, 0), Items[2].Start);
    }

    [Fact]
    public void Adopting_a_previous_proposal_does_not_apply_the_delay_twice_and_later_observations_use_the_same_baseline()
    {
        var adopted = Trip with { TripItems = [Items[0] with { Start = new(10, 0), End = new(12, 0) }, Items[1], Items[2]] };
        var lines = ProactiveTripChanges.Build(Case(), [adopted, Watch], Now);
        Assert.Contains(lines, line => line.StartsWith("Selected source") && line.Contains("10:00") && line.Contains("12:00"));
        lines = ProactiveTripChanges.Build(Case(), [adopted, Watch with { NumericValue = 90, Version = "v3" }], Now);
        Assert.Contains(lines, line => line.StartsWith("Selected source") && line.Contains("10:30") && line.Contains("12:30"));
    }

    [Fact]
    public void Missing_or_stale_sources_and_intervening_owner_edits_stay_unknown()
    {
        var lines = ProactiveTripChanges.Build(Case(), [Trip, Watch with { ValidUntil = Now.AddMinutes(-1) }], Now);
        Assert.DoesNotContain(lines, line => line.StartsWith("Selected source confirms"));
        Assert.Contains(lines, line => line.Contains("unknown"));
        var edited = Trip with { TripItems = [Items[0] with { Start = new(16, 0), End = new(18, 0) }] };
        lines = ProactiveTripChanges.Build(Case(), [edited, Watch], Now);
        Assert.Contains(lines, line => line.Contains("edited separately"));
        Assert.DoesNotContain(lines, line => line.StartsWith("Selected source confirms"));
    }
}
