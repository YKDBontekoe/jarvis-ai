using System.Text.Json;
using Jarvis.Api.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;

namespace Jarvis.Api.Channels;

/// <summary>Sends outbound channel messages with the connection's stored secrets and records each delivery.</summary>
public sealed class ChannelMessenger(
    IEnumerable<IChannelTransport> transports,
    IIntegrationCredentialStore credentials,
    IChannelRepository channels,
    ILogger<ChannelMessenger> logger)
{
    public static string SecretsProvider(Guid connectionId) =>
        IntegrationCredentialProviders.ChannelPrefix + connectionId.ToString("N");

    public async Task<bool> SendAsync(ChannelConnectionRecord connection, string recipient, string markdown,
        CancellationToken cancellationToken)
    {
        var transport = transports.SingleOrDefault(item => item.Kind == connection.Kind)
                        ?? throw new InvalidOperationException($"No transport handles {connection.Kind}.");
        var secrets = await credentials.GetSecretsAsync(connection.OwnerId, SecretsProvider(connection.Id),
            cancellationToken);
        var text = ChannelText.FromMarkdown(connection.Kind, markdown);
        try
        {
            foreach (var part in ChannelText.Split(text, transport.MaxMessageLength))
                await transport.SendAsync(connection, secrets, recipient, part, cancellationToken);
            await channels.RecordOutboundAsync(connection.Id, recipient, text, null, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not deliver a {Kind} message for connection {ConnectionId}.",
                connection.Kind, connection.Id);
            await channels.RecordOutboundAsync(connection.Id, recipient, text,
                exception.Message.Length <= 500 ? exception.Message : exception.Message[..500], cancellationToken);
            return false;
        }
    }
}

/// <summary>
/// Turns one inbound WhatsApp or Signal message into a Jarvis turn. Only allowlisted senders reach the agent;
/// YES/NO replies decide the pending approval in that sender's conversation.
/// </summary>
public sealed class ChannelMessageRouter(
    IConversationStore conversations,
    IChannelRepository channels,
    IToolApprovalStore approvals,
    ConversationTurnService turns,
    ApprovalDecisionService decisions,
    ChannelMessenger messenger,
    IAuditEventStore audit,
    ILogger<ChannelMessageRouter> logger)
{
    internal const string Help = """
        I'm Jarvis. Message me like you would in the app.
        • /new — start a fresh conversation
        • /status — what I'm waiting on
        • YES or NO — answer an approval request
        """;

    private static readonly HashSet<string> Yes = new(StringComparer.OrdinalIgnoreCase)
        { "yes", "y", "approve", "approved", "ok", "okay", "ja", "👍", "✅" };
    private static readonly HashSet<string> No = new(StringComparer.OrdinalIgnoreCase)
        { "no", "n", "decline", "deny", "reject", "nee", "👎", "❌" };

    public async Task HandleAsync(InboundChannelMessage message, CancellationToken cancellationToken)
    {
        var connection = message.Connection;
        var sender = ChannelAddresses.Normalize(message.Sender);
        if (!connection.Enabled) return;
        if (!ChannelAddresses.IsAllowed(connection, sender))
        {
            logger.LogWarning("Ignored a {Kind} message from a sender that is not allowlisted.", connection.Kind);
            await audit.AppendAsync(connection.OwnerId, "channels", "channel.sender_rejected", "high", false, null,
                JsonSerializer.Serialize(new { connectionId = connection.Id, connection.Kind }), cancellationToken);
            return;
        }

        var text = message.Text.Trim();
        if (text.Length == 0) return;
        if (text.Length > 32_000) text = text[..32_000];
        var ownerId = connection.OwnerId;

        if (text.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await messenger.SendAsync(connection, sender, Help, cancellationToken);
            return;
        }
        if (text.Equals("/new", StringComparison.OrdinalIgnoreCase))
        {
            await StartConversationAsync(connection, sender, cancellationToken);
            await messenger.SendAsync(connection, sender, "Started a fresh conversation. What's up?", cancellationToken);
            return;
        }

        var conversationId = await GetOrCreateConversationAsync(connection, sender, cancellationToken);
        var pending = (await approvals.ListActionableForConversationAsync(ownerId, conversationId, cancellationToken))
            .Where(approval => approval.Status == "pending").ToList();
        if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            await messenger.SendAsync(connection, sender, pending.Count == 0
                ? "Nothing is waiting on you here. Ask me anything."
                : $"I'm waiting for your approval to run {pending[0].ToolName}. Reply YES or NO.", cancellationToken);
            return;
        }

        var normalizedAnswer = text.TrimEnd('.', '!').Trim();
        ConversationTurnResult result;
        if (pending.Count > 0 && (Yes.Contains(normalizedAnswer) || No.Contains(normalizedAnswer)))
        {
            result = await decisions.DecideAsync(ownerId, pending[0].Id, Yes.Contains(normalizedAnswer),
                cancellationToken);
        }
        else if (pending.Count > 0)
        {
            await messenger.SendAsync(connection, sender, ApprovalPrompt(pending[0]), cancellationToken);
            return;
        }
        else
        {
            result = await turns.SendAsync(ownerId, conversationId, text, cancellationToken);
            if (result is ConversationTurnResult.NotFound)
            {
                conversationId = await StartConversationAsync(connection, sender, cancellationToken);
                result = await turns.SendAsync(ownerId, conversationId, text, cancellationToken);
            }
        }
        await messenger.SendAsync(connection, sender, Describe(result), cancellationToken);
    }

    internal static string Describe(ConversationTurnResult result) => result switch
    {
        ConversationTurnResult.Completed completed => completed.Message.Content,
        ConversationTurnResult.AwaitingApproval pending => ApprovalPrompt(pending.Approvals[0]),
        ConversationTurnResult.Conflict conflict => conflict.Message,
        ConversationTurnResult.Failed failed => failed.Message + " You can also continue in the Jarvis app.",
        _ => "I couldn't find that conversation. Send /new to start again."
    };

    internal static string ApprovalPrompt(ToolApprovalRecord approval)
    {
        var arguments = approval.ArgumentsJson.Length <= 400 ? approval.ArgumentsJson : approval.ArgumentsJson[..400] + "…";
        return $"🔐 I need your approval to run *{approval.ToolName}*.\n{arguments}\n\nReply YES to approve or NO to decline.";
    }

    private async Task<Guid> GetOrCreateConversationAsync(ChannelConnectionRecord connection, string sender,
        CancellationToken cancellationToken)
    {
        var existing = await channels.GetThreadConversationAsync(connection.Id, sender, cancellationToken);
        if (existing is { } id && await conversations.GetAsync(id, connection.OwnerId, cancellationToken) is not null)
            return id;
        return await StartConversationAsync(connection, sender, cancellationToken);
    }

    private async Task<Guid> StartConversationAsync(ChannelConnectionRecord connection, string sender,
        CancellationToken cancellationToken)
    {
        var title = $"{ChannelKinds.Label(connection.Kind)} · {connection.DisplayName}";
        var conversation = await conversations.CreateAsync(connection.OwnerId, title, cancellationToken);
        await channels.SetThreadConversationAsync(connection.Id, sender, conversation.Id, cancellationToken);
        return conversation.Id;
    }
}
