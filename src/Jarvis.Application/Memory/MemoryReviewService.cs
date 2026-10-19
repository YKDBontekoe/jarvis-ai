using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Memory;

namespace Jarvis.Application.Memory;

public sealed class MemoryReviewService(IMemoryService memories, IMemoryRepository repository,
    IConversationStore conversations, IAssistantProfileService profiles, TimeProvider? timeProvider = null)
    : IMemoryReviewService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<MemoryReviewEntry>> GetReviewAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var picked = MemoryReviewRules.Select(await memories.ListAsync(ownerId, null, cancellationToken), now);
        if (picked.Count == 0) return [];
        var names = (await profiles.ListAsync(ownerId, cancellationToken)).ToDictionary(item => item.Id,
            item => item.Name);
        var entries = new List<MemoryReviewEntry>(picked.Count);
        foreach (var item in picked)
        {
            var memory = item.Memory;
            var history = await repository.ListHistoryAsync(memory.Id, ownerId, cancellationToken);
            entries.Add(new MemoryReviewEntry(memory.Id, memory.Kind, memory.Content, memory.Confidence,
                memory.IsPinned, memory.CreatedAt, memory.ValidUntil, item.Reasons,
                await SourceAsync(ownerId, memory, cancellationToken), memory.ProfileId,
                memory.ProfileId is { } id && names.TryGetValue(id, out var name) ? name : null, history.Count));
        }
        return entries;
    }

    public async Task<MemoryCorrectionResult> CorrectAsync(Guid ownerId, Guid memoryId, string content,
        CancellationToken cancellationToken)
    {
        var text = content?.Trim() ?? string.Empty;
        if (text.Length is < 3 or > 2_000)
            throw new ArgumentException("A memory must contain 3 to 2,000 characters.", nameof(content));
        var current = await memories.GetAsync(memoryId, ownerId, cancellationToken);
        if (current is null) return new MemoryCorrectionResult(MemoryCorrectionOutcome.NotFound);
        if (current.IsPinned) return new MemoryCorrectionResult(MemoryCorrectionOutcome.Pinned, current);
        if (current.ValidUntil is { } until && until <= clock.GetUtcNow())
            return new MemoryCorrectionResult(MemoryCorrectionOutcome.NotCurrent, current);
        if (string.Equals(current.Content.Trim(), text, StringComparison.Ordinal))
            return new MemoryCorrectionResult(MemoryCorrectionOutcome.Unchanged, current);

        // The owner wrote the correction, so it is certain and counts as the owner's own memory.
        var replacement = await memories.ReplaceAsync(memoryId, ownerId, current.Kind, text,
            Math.Max(current.Importance, 0.7f), 0.95f, cancellationToken, sourceType: "user");
        return replacement is null
            ? new MemoryCorrectionResult(MemoryCorrectionOutcome.NotCurrent, current)
            : new MemoryCorrectionResult(MemoryCorrectionOutcome.Corrected, replacement);
    }

    public async Task<MemoryCorrectionResult> ExpireAsync(Guid ownerId, Guid memoryId,
        CancellationToken cancellationToken)
    {
        var current = await memories.GetAsync(memoryId, ownerId, cancellationToken);
        if (current is null) return new MemoryCorrectionResult(MemoryCorrectionOutcome.NotFound);
        if (current.IsPinned) return new MemoryCorrectionResult(MemoryCorrectionOutcome.Pinned, current);
        var ended = await memories.ExpireAsync(memoryId, ownerId, cancellationToken);
        return ended is null
            ? new MemoryCorrectionResult(MemoryCorrectionOutcome.NotCurrent, current)
            : new MemoryCorrectionResult(MemoryCorrectionOutcome.Corrected, ended);
    }

    public async Task<IReadOnlyList<MemoryVersionView>> HistoryAsync(Guid ownerId, Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (await memories.GetAsync(memoryId, ownerId, cancellationToken) is null) return [];
        var history = await repository.ListHistoryAsync(memoryId, ownerId, cancellationToken);
        return history.Select(item => new MemoryVersionView(item.Id, item.Content, item.CreatedAt, item.ValidUntil))
            .ToArray();
    }

    private async Task<MemorySourceView> SourceAsync(Guid ownerId, MemoryRecord memory,
        CancellationToken cancellationToken)
    {
        switch (memory.SourceType)
        {
            case "conversation" when memory.SourceId is { } messageId:
                var hit = await conversations.FindMessageAsync(ownerId, messageId, cancellationToken);
                return hit is null
                    ? new MemorySourceView("conversation", "From a chat that is no longer there", null, null)
                    : new MemorySourceView("conversation", $"From the chat “{hit.ConversationTitle}”",
                        hit.Message.ConversationId, hit.Message.Id);
            case "journal":
                return new MemorySourceView("journal", "From your journal", null, null);
            case "user":
                return new MemorySourceView("user", "You added this", null, null);
            default:
                return new MemorySourceView(memory.SourceType ?? "unknown", "Source unknown", null, null);
        }
    }
}
