using Jarvis.Domain.Files;
using System.Text.Json;
using Jarvis.Api.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
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

    /// <summary>Downloads one media item that arrived on this connection, or null when it cannot be had.</summary>
    public async Task<DownloadedMedia?> FetchMediaAsync(ChannelConnectionRecord connection, string mediaId,
        long maxBytes, CancellationToken cancellationToken)
    {
        var transport = transports.SingleOrDefault(item => item.Kind == connection.Kind);
        if (transport is null) return null;
        var secrets = await credentials.GetSecretsAsync(connection.OwnerId, SecretsProvider(connection.Id),
            cancellationToken);
        try
        {
            return await transport.FetchMediaAsync(connection, secrets, mediaId, maxBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not download media for a {Kind} connection {ConnectionId}.",
                connection.Kind, connection.Id);
            return null;
        }
    }

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
    IFileService files,
    IConversationFileContextRepository fileContext,
    ILogger<ChannelMessageRouter> logger)
{
    internal const string Help = """
        I'm Jarvis. Message me like you would in the app.
        • /new — start a fresh conversation
        • /status — what I'm waiting on
        • Send a photo (JPEG, PNG, WebP) or a file (PDF, Word, text) and ask about it
        • YES or NO — answer an approval request from this chat
        Approvals from app chats, tasks and automations can only be decided in the Jarvis app.
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
        if (text.Length > 32_000) text = text[..32_000];
        var ownerId = connection.OwnerId;

        // Photos go to the model as real images, files are stored and attached as sources to the chat, and other media is
        // answered honestly instead of being dropped.
        var media = ChannelMedia.Parse(message.MediaJson);
        IReadOnlyList<MessageAttachment>? attachments = null;
        IReadOnlyList<StoredFile> documents = [];
        if (media.Count > 0)
        {
            if (ChannelMedia.UnreadableReply(media) is { } unreadable)
                await messenger.SendAsync(connection, sender, unreadable, cancellationToken);
            var stored = await StorePhotosAsync(connection, media, cancellationToken);
            if (stored.Count == 0 && media.Any(ChannelMedia.IsPhoto))
                await messenger.SendAsync(connection, sender,
                    "I couldn't download that photo. Please send it again.", cancellationToken);
            if (stored.Count > 0)
            {
                attachments = stored;
                if (text.Length == 0) text = MessageAttachments.DefaultContent(stored.Count);
            }
            documents = await StoreDocumentsAsync(connection, sender, media, cancellationToken);
            if (documents.Count > 0)
            {
                var names = string.Join(", ", documents.Select(file => file.FileName));
                var note = $"[Files the user sent with this message, attached to this chat as sources: {names}]";
                text = text.Length == 0 ? "I sent you a file.\n\n" + note : text + "\n\n" + note;
            }
        }
        if (text.Length == 0) return;

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
        await AttachDocumentsAsync(ownerId, conversationId, documents, cancellationToken);
        var pending = (await approvals.ListActionableForConversationAsync(ownerId, conversationId, cancellationToken))
            .Where(approval => approval.Status == "pending").ToList();
        if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            var elsewhere = await CountPendingElsewhereAsync(ownerId, pending, cancellationToken);
            var status = pending.Count == 0
                ? "Nothing is waiting on you here. Ask me anything."
                : $"I'm waiting for your approval to run {pending[0].ToolName}. Reply YES or NO.";
            await messenger.SendAsync(connection, sender,
                elsewhere == 0 ? status : status + "\n\n" + AppOnlyNotice(connection.Kind, elsewhere), cancellationToken);
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
        else if ((Yes.Contains(normalizedAnswer) || No.Contains(normalizedAnswer)) &&
                 await CountPendingElsewhereAsync(ownerId, pending, cancellationToken) is var otherPending and > 0)
        {
            // A bare YES/NO would otherwise go to the agent as chat and look like a decision that never happened.
            await messenger.SendAsync(connection, sender, AppOnlyNotice(connection.Kind, otherPending), cancellationToken);
            return;
        }
        else
        {
            result = await turns.SendAsync(ownerId, conversationId, text, cancellationToken, attachments: attachments);
            if (result is ConversationTurnResult.NotFound)
            {
                conversationId = await StartConversationAsync(connection, sender, cancellationToken);
                await AttachDocumentsAsync(ownerId, conversationId, documents, cancellationToken);
                result = await turns.SendAsync(ownerId, conversationId, text, cancellationToken,
                    attachments: attachments);
            }
        }
        await messenger.SendAsync(connection, sender, Describe(result), cancellationToken);
    }

    /// <summary>
    /// Downloads the readable photos of a message and stores each as one of the owner's files, through the same
    /// type check, size limit and virus scan as a photo sent from the app. A photo that fails any step is skipped.
    /// </summary>
    private async Task<IReadOnlyList<MessageAttachment>> StorePhotosAsync(ChannelConnectionRecord connection,
        IReadOnlyList<ChannelMediaRef> media, CancellationToken cancellationToken)
    {
        var stored = new List<MessageAttachment>();
        foreach (var photo in media.Where(ChannelMedia.IsPhoto).Take(ChannelMedia.MaxPhotos))
        {
            var downloaded = await messenger.FetchMediaAsync(connection, photo.Id, MessageAttachments.MaxImageBytes,
                cancellationToken);
            if (downloaded is null) continue;
            var type = ChannelMedia.NormalizeMime(photo.Mime);
            var name = $"{ChannelKinds.Label(connection.Kind).ToLowerInvariant()}-photo-" +
                       $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{stored.Count + 1}{ChannelMedia.Extension(type)}";
            try
            {
                using var content = new MemoryStream(downloaded.Content);
                var file = await files.UploadAsync(connection.OwnerId, name, type, content.Length, content,
                    cancellationToken);
                stored.Add(new MessageAttachment(file.Id, file.FileName, file.ContentType));
            }
            catch (Exception exception) when (exception is ArgumentException or MalwareDetectedException)
            {
                logger.LogWarning("A {Kind} photo was refused: {Reason}", connection.Kind, exception.GetType().Name);
            }
        }
        return stored;
    }

    /// <summary>
    /// Downloads the files of a message and stores each as one of the owner's files, through the same type check, size
    /// limit and virus scan as an upload from the app. A file that fails any step is reported to the sender.
    /// </summary>
    private async Task<IReadOnlyList<StoredFile>> StoreDocumentsAsync(ChannelConnectionRecord connection, string sender,
        IReadOnlyList<ChannelMediaRef> media, CancellationToken cancellationToken)
    {
        var stored = new List<StoredFile>();
        var label = ChannelKinds.Label(connection.Kind);
        foreach (var item in media.Where(ChannelMedia.IsDocument).Take(ChannelMedia.MaxDocuments))
        {
            var name = ChannelMedia.DocumentName(item, label, stored.Count + 1);
            var downloaded = await messenger.FetchMediaAsync(connection, item.Id, ChannelMedia.MaxDocumentBytes,
                cancellationToken);
            if (downloaded is null)
            {
                await messenger.SendAsync(connection, sender,
                    $"I couldn't download {name} (files up to {ChannelMedia.MaxDocumentBytes / (1024 * 1024)} MB work). Please send it again.",
                    cancellationToken);
                continue;
            }
            try
            {
                using var content = new MemoryStream(downloaded.Content);
                stored.Add(await files.UploadAsync(connection.OwnerId, name,
                    ChannelMedia.NormalizeMime(item.Mime), content.Length, content, cancellationToken));
            }
            catch (ArgumentException)
            {
                await messenger.SendAsync(connection, sender,
                    $"I can't store {name}: that file type isn't supported. PDF, Word, PowerPoint and plain text files work.",
                    cancellationToken);
            }
            catch (MalwareDetectedException)
            {
                logger.LogWarning("A {Kind} file was refused by the virus scan.", connection.Kind);
                await messenger.SendAsync(connection, sender, $"I didn't keep {name}: it did not pass the safety scan.",
                    cancellationToken);
            }
        }
        if (media.Count(ChannelMedia.IsDocument) > ChannelMedia.MaxDocuments)
            await messenger.SendAsync(connection, sender,
                $"I can take up to {ChannelMedia.MaxDocuments} files in one message, so I only used the first {ChannelMedia.MaxDocuments}.",
                cancellationToken);
        return stored;
    }

    private async Task AttachDocumentsAsync(Guid ownerId, Guid conversationId, IReadOnlyList<StoredFile> documents,
        CancellationToken cancellationToken)
    {
        foreach (var file in documents)
            await fileContext.AttachFileAsync(conversationId, file.Id, ownerId, cancellationToken);
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

    /// <summary>Pending tool approvals that a YES/NO in this chat cannot reach.</summary>
    private async Task<int> CountPendingElsewhereAsync(Guid ownerId, IReadOnlyList<ToolApprovalRecord> here,
        CancellationToken cancellationToken)
    {
        var hereIds = here.Select(item => item.Id).ToHashSet();
        return (await approvals.ListActionableAsync(ownerId, cancellationToken))
            .Count(item => item.Status == "pending" && !hereIds.Contains(item.Id));
    }

    internal static string AppOnlyNotice(string kind, int count) =>
        $"⚠️ {count} other approval{(count == 1 ? " is" : "s are")} waiting in the Jarvis app. " +
        $"Approvals from app chats, tasks and automations can't be decided from {ChannelKinds.Label(kind)} — open the Jarvis app.";

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
