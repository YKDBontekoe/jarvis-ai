using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Api.Channels;
using Jarvis.Api.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Channels;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ChannelTests
{
    [Fact]
    public void WhatsApp_signature_must_match_the_raw_body_hmac()
    {
        var body = Encoding.UTF8.GetBytes("""{"entry":[]}""");
        var signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), body));

        Assert.True(WhatsAppWebhook.HasValidSignature(signature, body, "app-secret"));
        Assert.False(WhatsAppWebhook.HasValidSignature(signature, body, "other-secret"));
        Assert.False(WhatsAppWebhook.HasValidSignature(signature, Encoding.UTF8.GetBytes("{}"), "app-secret"));
        Assert.False(WhatsAppWebhook.HasValidSignature("sha256=zz", body, "app-secret"));
        Assert.False(WhatsAppWebhook.HasValidSignature(null, body, "app-secret"));
        Assert.False(WhatsAppWebhook.HasValidSignature(signature, body, null));
    }

    [Fact]
    public void WhatsApp_parser_reads_text_and_button_replies_for_this_number_only()
    {
        using var document = JsonDocument.Parse("""
            {"object":"whatsapp_business_account","entry":[{"changes":[
              {"field":"messages","value":{"metadata":{"phone_number_id":"1234567"},
                "messages":[
                  {"from":"31612345678","id":"wamid.1","type":"text","text":{"body":"Hi Jarvis"}},
                  {"from":"31612345678","id":"wamid.2","type":"interactive","interactive":{"button_reply":{"title":"YES"}}},
                  {"from":"31612345678","id":"wamid.3","type":"image","image":{}}
                ]}},
              {"field":"messages","value":{"metadata":{"phone_number_id":"999"},
                "messages":[{"from":"1555","id":"wamid.4","type":"text","text":{"body":"not for us"}}]}}
            ]}]}
            """);

        var messages = WhatsAppWebhook.ParseMessages(document.RootElement, "1234567").ToArray();

        Assert.Equal([("+31612345678", "Hi Jarvis", "wamid.1"), ("+31612345678", "YES", "wamid.2")], messages);
    }

    [Fact]
    public void Signal_envelopes_become_messages_with_stable_ids()
    {
        using var document = JsonDocument.Parse("""
            [{"envelope":{"source":"+31611111111","sourceNumber":"+31611111111","timestamp":1727450000000,
               "dataMessage":{"message":"Remind me to call mom","timestamp":1727450000000}}},
             {"envelope":{"sourceNumber":"+31611111111","timestamp":1727450000001,"typingMessage":{"action":"STARTED"}}}]
            """);

        var parsed = SignalReceiver.ParseEnvelopes(document.RootElement).Single();

        Assert.Equal(("+31611111111", "Remind me to call mom", "+31611111111:1727450000000"), parsed);
    }

    [Theory]
    [InlineData("+31 6 1234 5678", "+31612345678")]
    [InlineData("0031612345678", "+31612345678")]
    [InlineData("(555) 010-9999", "+5550109999")]
    [InlineData("", "")]
    public void Addresses_normalize_to_international_digits(string input, string expected)
    {
        Assert.Equal(expected, ChannelAddresses.Normalize(input));
    }

    [Fact]
    public void Validation_normalizes_numbers_and_requires_whatsapp_secrets()
    {
        var request = new SaveChannelRequest("WhatsApp", " Phone ", "106540352242922", true,
            ["+31 6 1234 5678", "+31612345678"], true, "0031612345678",
            new Dictionary<string, string> { ["access_token"] = "EAAB", ["app_secret"] = "s", ["verify_token"] = "v" });

        var normalized = ChannelValidation.Normalize(request, creating: true);

        Assert.Equal("whatsapp", normalized.Kind);
        Assert.Equal(["+31612345678"], normalized.AllowedSenders);
        Assert.Equal("+31612345678", normalized.NotifyRecipient);
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { Secrets = null }, true));
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { AllowedSenders = [] }, true));
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { NotifyRecipient = "+15550100" }, true));
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { Account = "not digits" }, true));
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(
            request with { Secrets = new Dictionary<string, string> { ["password"] = "x" } }, false));
    }

    [Fact]
    public void Markdown_adapts_to_each_app_and_long_replies_split_on_lines()
    {
        const string markdown = "## Plan\n**Train** first, see [NS](https://ns.nl).";

        Assert.Equal("*Plan*\n*Train* first, see NS (https://ns.nl).",
            ChannelText.FromMarkdown(ChannelKinds.WhatsApp, markdown));
        Assert.Equal("*Plan*\n**Train** first, see NS (https://ns.nl).",
            ChannelText.FromMarkdown(ChannelKinds.Signal, markdown));
        var parts = ChannelText.Split(string.Join('\n', Enumerable.Repeat(new string('a', 30), 10)), 100).ToArray();
        Assert.True(parts.Length > 3);
        Assert.All(parts, part => Assert.True(part.Length <= 100));
    }

    [Fact]
    public void Turn_results_become_channel_replies_with_approval_instructions()
    {
        var approval = new ToolApprovalRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "r", "c",
            "ForgetMemory", """{"memoryId":"abc"}""", "pending", null, "not_started", null, DateTimeOffset.UtcNow, null);

        var prompt = ChannelMessageRouter.Describe(new ConversationTurnResult.AwaitingApproval([approval]));
        var answer = ChannelMessageRouter.Describe(new ConversationTurnResult.Completed(
            new Message(Guid.NewGuid(), "assistant", "Done!")));

        Assert.Contains("ForgetMemory", prompt);
        Assert.Contains("Reply YES to approve or NO to decline", prompt);
        Assert.Equal("Done!", answer);
    }

    [Fact]
    public void WhatsApp_notifications_respect_the_24_hour_window()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new ChannelConnectionRecord(Guid.NewGuid(), Guid.NewGuid(), ChannelKinds.WhatsApp, "Phone", "1",
            true, ["+31612345678"], true, null, "key", now.AddHours(-2), null, null, now, now, now);

        Assert.True(ChannelNotificationForwarder.CanMessage(connection, now));
        Assert.False(ChannelNotificationForwarder.CanMessage(connection with { LastInboundAt = now.AddHours(-25) }, now));
        Assert.False(ChannelNotificationForwarder.CanMessage(connection with { LastInboundAt = null }, now));
        Assert.True(ChannelNotificationForwarder.CanMessage(connection with { Kind = ChannelKinds.Signal, LastInboundAt = null }, now));
        Assert.False(ChannelNotificationForwarder.ShouldForward(connection, "approval.required"));
        Assert.True(ChannelNotificationForwarder.ShouldForward(connection, "reminder.due"));
    }

    [Fact]
    public void Notification_categories_default_to_everything_but_approvals_and_can_be_narrowed()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new ChannelConnectionRecord(Guid.NewGuid(), Guid.NewGuid(), ChannelKinds.WhatsAppLinked, "Phone",
            "+31612345678", true, ["+31612345678"], true, null, "key", null, null, null, now, now, now);

        Assert.True(ChannelNotificationForwarder.ShouldForward(connection, "briefing.daily"));
        Assert.True(ChannelNotificationForwarder.ShouldForward(connection, "some.future.type"));
        Assert.False(ChannelNotificationForwarder.ShouldForward(connection, "automation.approval"));

        var approvalsOnly = connection with { NotificationCategories = [ChannelNotificationCategories.Approvals] };
        Assert.True(ChannelNotificationForwarder.ShouldForward(approvalsOnly, "approval.required"));
        Assert.True(ChannelNotificationForwarder.ShouldForward(approvalsOnly, "automation.approval"));
        Assert.False(ChannelNotificationForwarder.ShouldForward(approvalsOnly, "reminder.due"));
    }

    [Fact]
    public void Category_requests_are_normalized_and_unknown_categories_are_rejected()
    {
        Assert.Equal(["reminders", "approvals"], ChannelNotificationCategories.Normalize(["Approvals", "reminders", "approvals"]));
        Assert.Null(ChannelNotificationCategories.Normalize(null));
        Assert.Throws<ArgumentException>(() => ChannelNotificationCategories.Normalize(["everything"]));

        var request = new SaveChannelRequest(ChannelKinds.WhatsAppLinked, "WhatsApp", "+31612345678", true,
            ["+31612345678"], true, null, null, ["Tasks"]);
        Assert.Equal(["tasks"], ChannelValidation.Normalize(request, creating: true).NotificationCategories);
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { NotificationCategories = ["nope"] }, true));
    }

    [Fact]
    public void Forwarded_approvals_say_when_they_must_be_decided_in_the_jarvis_app()
    {
        var approval = new NotificationRecord(Guid.NewGuid(), "approval.required", "Approval needed",
            "Jarvis is waiting for approval to run SendEmail.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        var here = ChannelNotificationForwarder.Compose(ChannelKinds.WhatsAppLinked, approval, decidableHere: true);
        var appOnly = ChannelNotificationForwarder.Compose(ChannelKinds.WhatsAppLinked, approval, decidableHere: false);
        var automation = ChannelNotificationForwarder.Compose(ChannelKinds.WhatsAppLinked,
            approval with { Type = "automation.approval" }, decidableHere: null);
        var reminder = ChannelNotificationForwarder.Compose(ChannelKinds.WhatsAppLinked,
            approval with { Type = "reminder.due", Title = "Reminder", Body = "Call mom" }, decidableHere: null);

        Assert.Contains("Reply YES to approve or NO to decline", here);
        Assert.DoesNotContain("Jarvis app", here);
        Assert.Contains("can't be approved from WhatsApp", appOnly);
        Assert.Contains("Open the Jarvis app", appOnly);
        Assert.DoesNotContain("Reply YES", appOnly);
        Assert.Contains("can't be decided from WhatsApp", automation);
        Assert.Contains("Open the Jarvis app", automation);
        Assert.DoesNotContain("Jarvis app", reminder);
        Assert.Contains("can't be decided from WhatsApp", ChannelMessageRouter.AppOnlyNotice(ChannelKinds.WhatsAppLinked, 2));
        Assert.Contains("2 other approvals are", ChannelMessageRouter.AppOnlyNotice(ChannelKinds.WhatsAppLinked, 2));
        Assert.Contains("1 other approval is", ChannelMessageRouter.AppOnlyNotice(ChannelKinds.Signal, 1));
    }

    [Fact]
    public void Channel_options_read_the_signal_cli_base_url()
    {
        var configured = ChannelOptions.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Channels:Signal:BaseUrl"] = " http://signal-cli:8080/ " }).Build());
        var missing = ChannelOptions.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Channels:Signal:BaseUrl"] = "  " }).Build());

        Assert.Equal("http://signal-cli:8080/", configured.SignalBaseUrl);
        Assert.Null(missing.SignalBaseUrl);
        Assert.Null(ChannelOptions.From(new ConfigurationBuilder().Build()).SignalBaseUrl);
    }

    [Fact]
    public void Linked_whatsapp_needs_only_a_phone_number_and_no_credentials()
    {
        var request = new SaveChannelRequest(ChannelKinds.WhatsAppLinked, "WhatsApp", "+31 6 1234 5678", true,
            ["+31612345678"], true, "+31612345678", null);

        var normalized = ChannelValidation.Normalize(request, creating: true);

        Assert.Equal("+31612345678", normalized.Account);
        Assert.Empty(ChannelKinds.SecretNames(ChannelKinds.WhatsAppLinked));
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(request with { Account = "abc" }, true));
        Assert.True(ChannelKinds.IsWhatsApp(ChannelKinds.WhatsAppLinked));
        Assert.Equal("WhatsApp", ChannelKinds.Label(ChannelKinds.WhatsAppLinked));
    }

    [Fact]
    public void Signal_note_to_self_sync_messages_count_as_the_owner_talking()
    {
        using var document = JsonDocument.Parse("""
            [{"envelope":{"sourceNumber":"+31612345678","timestamp":1,"syncMessage":{"sentMessage":
               {"timestamp":1727450000000,"destinationNumber":"+31612345678","message":"Note to self"}}}},
             {"envelope":{"sourceNumber":"+31612345678","timestamp":2,"syncMessage":{"sentMessage":
               {"timestamp":1727450000001,"destinationNumber":"+31699999999","message":"Chat with someone else"}}}}]
            """);

        Assert.Equal(("+31612345678", "Note to self", "+31612345678:1727450000000"),
            SignalReceiver.ParseEnvelopes(document.RootElement, "+31612345678").Single());
        Assert.Empty(SignalReceiver.ParseEnvelopes(document.RootElement));
    }

    [Fact]
    public void Linked_whatsapp_formats_like_whatsapp_and_has_no_service_window()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new ChannelConnectionRecord(Guid.NewGuid(), Guid.NewGuid(), ChannelKinds.WhatsAppLinked, "Phone",
            "+31612345678", true, ["+31612345678"], true, null, "key", null, null, null, now, now, now);

        Assert.Equal("*Hi*", ChannelText.FromMarkdown(ChannelKinds.WhatsAppLinked, "**Hi**"));
        Assert.True(ChannelNotificationForwarder.CanMessage(connection, now));
    }

    [Fact]
    public void Channel_options_read_the_whatsapp_bridge()
    {
        var options = ChannelOptions.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Channels:WhatsAppBridge:BaseUrl"] = " http://whatsapp-bridge:3000 ",
                ["Channels:WhatsAppBridge:Token"] = "secret"
            }).Build());

        Assert.Equal("http://whatsapp-bridge:3000", options.WhatsAppBridgeUrl);
        Assert.Equal("secret", options.WhatsAppBridgeToken);
        Assert.Null(ChannelOptions.From(new ConfigurationBuilder().Build()).WhatsAppBridgeUrl);
    }
}
