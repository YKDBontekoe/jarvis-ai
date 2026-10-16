using Jarvis.Api.Notifications;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class PushMessageTargetTests
{
    private static PushDeliveryWork Work(string deviceToken)
    {
        var notification = new Notification(Guid.NewGuid(), Guid.NewGuid(), "reminder.due", "Title", "Body", null);
        return new PushDeliveryWork(new PushDeliveryKey(notification.Id, Guid.NewGuid()), 0, notification.OwnerId,
            deviceToken, notification, new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString() });
    }

    private static Dictionary<string, object?> MessageOf(string deviceToken) =>
        Assert.IsType<Dictionary<string, object?>>(NotificationPushWorker.BuildMessage(Work(deviceToken))["message"]);

    [Fact]
    public void An_installation_id_is_sent_in_the_fid_field()
    {
        var message = MessageOf("cX3kJq0uTfS0h1B-2eU9aQ");

        Assert.Equal("cX3kJq0uTfS0h1B-2eU9aQ", message["fid"]);
        Assert.False(message.ContainsKey("token"));
    }

    [Fact]
    public void A_legacy_fcm_registration_token_keeps_the_token_field_until_the_app_re_registers()
    {
        var message = MessageOf("dGVzdA:APA91bLegacyRegistrationToken");

        Assert.Equal("dGVzdA:APA91bLegacyRegistrationToken", message["token"]);
        Assert.False(message.ContainsKey("fid"));
    }

    [Fact]
    public void The_message_carries_the_notification_data_and_platform_options_for_either_target()
    {
        var message = MessageOf("cX3kJq0uTfS0h1B-2eU9aQ");

        Assert.Contains("notification", message);
        Assert.Contains("data", message);
        Assert.Contains("android", message);
        Assert.Contains("apns", message);
    }
}
