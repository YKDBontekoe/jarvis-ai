using Jarvis.Application.Notifications;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NotificationDeliveryTruthTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static NotificationDeliveryFacts Facts(DateTimeOffset? read = null, params DeviceDelivery[] devices) =>
        new(Guid.NewGuid(), "reminder.due", "Call mum", At, read, devices);

    [Fact]
    public void Without_a_registered_phone_it_says_nothing_was_pushed()
    {
        var steps = NotificationDeliveryRules.Describe(Facts()).Steps;

        Assert.Equal(["created", "not_pushed", "unread"], steps.Select(s => s.State));
        Assert.Contains("No phone was registered", steps[1].Text);
    }

    [Fact]
    public void A_push_handed_to_the_provider_is_not_claimed_as_shown_on_a_phone()
    {
        var steps = NotificationDeliveryRules.Describe(Facts(At.AddMinutes(3),
            new DeviceDelivery(PushDeliveryOutcomes.ProviderAccepted, 1, At.AddMinutes(1), null, false))).Steps;

        Assert.Equal(["created", "provider_accepted", "read"], steps.Select(s => s.State));
        Assert.Contains("cannot see when a phone shows it", steps[1].Text);
        Assert.Equal(At.AddMinutes(3), steps[2].At);
    }

    [Fact]
    public void Held_and_app_only_pushes_are_told_apart_from_sent_ones()
    {
        var held = NotificationDeliveryRules.Describe(Facts(null, new DeviceDelivery("held", 1, At, null, false))).Steps[1];
        var appOnly = NotificationDeliveryRules.Describe(Facts(null, new DeviceDelivery("app_only", 1, At, null, false))).Steps[1];

        Assert.Equal("held", held.State);
        Assert.Contains("catch-up", held.Text);
        Assert.Equal("app_only", appOnly.State);
        Assert.Contains("No push was sent", appOnly.Text);
    }

    [Fact]
    public void Waiting_retrying_and_given_up_are_different_states()
    {
        string State(DeviceDelivery d) => NotificationDeliveryRules.Describe(Facts(null, d)).Steps[1].State;

        Assert.Equal("waiting", State(new DeviceDelivery(null, 0, null, null, false)));
        Assert.Equal("retrying", State(new DeviceDelivery(null, 2, null, "FCM returned 500.", false)));
        var gaveUp = NotificationDeliveryRules.Describe(Facts(null, new DeviceDelivery(null, 10, null, "FCM returned 500.", true))).Steps[1];
        Assert.Equal("failed", gaveUp.State);
        Assert.Contains("after 10 tries", gaveUp.Text);
    }

    [Fact]
    public void Old_rows_without_an_outcome_say_it_is_not_known_and_several_phones_are_labelled()
    {
        var steps = NotificationDeliveryRules.Describe(Facts(null,
            new DeviceDelivery(null, 1, At, null, false),
            new DeviceDelivery("provider_accepted", 1, At, null, false))).Steps;

        Assert.Equal("unrecorded", steps[1].State);
        Assert.Contains("not known", steps[1].Text);
        Assert.StartsWith("One phone: ", steps[2].Text);
    }
}
