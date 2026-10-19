using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MemoryReviewTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static MemoryRecord Memory(string content = "Rosa prefers Fridays", string source = "conversation",
        float confidence = .9f, DateTimeOffset? created = null, DateTimeOffset? validUntil = null, bool pinned = false,
        Guid? sourceId = null, Guid? profile = null) =>
        new(Guid.CreateVersion7(), Owner, "fact", content, .7f, confidence, source, sourceId,
            created ?? Now.AddDays(-1), created ?? Now.AddDays(-1), validUntil, pinned, profile);

    [Fact]
    public void Recently_learned_memories_are_listed_but_what_the_owner_wrote_is_not()
    {
        var learned = Memory();
        var written = Memory("Owner wrote this", source: "user");
        var old = Memory("Old fact", created: Now.AddDays(-30));

        var picked = MemoryReviewRules.Select([learned, written, old], Now);

        Assert.Equal([learned.Id], picked.Select(item => item.Memory.Id));
        Assert.Equal([MemoryReviewReasons.Recent], picked[0].Reasons);
    }

    [Fact]
    public void Low_confidence_and_expiring_memories_are_listed_whoever_wrote_them()
    {
        var unsure = Memory("Maybe vegetarian", source: "user", confidence: .4f, created: Now.AddDays(-60));
        var lapsing = Memory("Travelling until Friday", source: "user", created: Now.AddDays(-60),
            validUntil: Now.AddDays(3));
        var fine = Memory("Fine", source: "user", created: Now.AddDays(-60));

        var picked = MemoryReviewRules.Select([unsure, lapsing, fine], Now);

        Assert.Equal(2, picked.Count);
        Assert.Contains(picked, item => item.Memory.Id == unsure.Id && item.Reasons.SequenceEqual([MemoryReviewReasons.LowConfidence]));
        Assert.Contains(picked, item => item.Memory.Id == lapsing.Id && item.Reasons.SequenceEqual([MemoryReviewReasons.Expiring]));
    }

    [Fact]
    public void Memories_that_already_ended_and_pinned_low_confidence_are_left_out()
    {
        var ended = Memory(validUntil: Now.AddMinutes(-1));
        var pinned = Memory("Pinned", source: "user", confidence: .1f, created: Now.AddDays(-60), pinned: true);

        Assert.Empty(MemoryReviewRules.Select([ended, pinned], Now));
    }

    [Fact]
    public void The_most_flagged_memory_leads_then_the_newest()
    {
        var both = Memory("Both", confidence: .3f, created: Now.AddDays(-5));
        var newer = Memory("Newer", created: Now.AddHours(-1));
        var older = Memory("Older", created: Now.AddDays(-9));

        Assert.Equal(["Both", "Newer", "Older"],
            MemoryReviewRules.Select([older, newer, both], Now).Select(item => item.Memory.Content));
    }

    [Fact]
    public void The_review_is_capped()
    {
        var many = Enumerable.Range(0, 60).Select(index => Memory($"Fact {index}"));
        Assert.Equal(MemoryReviewRules.MaxItems, MemoryReviewRules.Select(many, Now).Count);
    }

    private static MemoryReviewService Service(IMemoryService memories, IMemoryRepository? repository = null,
        IConversationStore? conversations = null) =>
        new(memories, repository ?? Fake<IMemoryRepository>.Create(
                ("ListHistoryAsync", _ => (IReadOnlyList<MemoryRecord>)[])),
            conversations ?? Fake<IConversationStore>.Create(),
            Fake<IAssistantProfileService>.Create(("ListAsync", _ => (IReadOnlyList<AssistantProfileRecord>)[])),
            new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task A_correction_replaces_the_memory_as_the_owners_own_certain_statement()
    {
        var current = Memory("Rosa prefers Wednesdays");
        string? replacedWith = null;
        string? sourceType = null;
        var service = Service(Fake<IMemoryService>.Create(
            ("GetAsync", _ => current),
            ("ReplaceAsync", args =>
            {
                replacedWith = (string)args[3]!;
                sourceType = (string)args[7]!;
                return current with { Id = Guid.CreateVersion7(), Content = replacedWith };
            })));

        var result = await service.CorrectAsync(Owner, current.Id, "  Rosa prefers Thursdays now ", CancellationToken.None);

        Assert.Equal(MemoryCorrectionOutcome.Corrected, result.Outcome);
        Assert.Equal("Rosa prefers Thursdays now", replacedWith);
        Assert.Equal("user", sourceType);
    }

    [Fact]
    public async Task A_pinned_memory_is_never_replaced_behind_the_owners_back()
    {
        var pinned = Memory(pinned: true);
        var service = Service(Fake<IMemoryService>.Create(("GetAsync", _ => pinned)));

        Assert.Equal(MemoryCorrectionOutcome.Pinned,
            (await service.CorrectAsync(Owner, pinned.Id, "Something else", CancellationToken.None)).Outcome);
        Assert.Equal(MemoryCorrectionOutcome.Pinned,
            (await service.ExpireAsync(Owner, pinned.Id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Missing_ended_and_unchanged_memories_have_their_own_outcome_and_bad_text_is_rejected()
    {
        var ended = Memory(validUntil: Now.AddDays(-1));
        var same = Memory("Same text");
        var service = Service(Fake<IMemoryService>.Create(("GetAsync", args =>
            (Guid)args[0]! == ended.Id ? ended : (Guid)args[0]! == same.Id ? same : null)));

        Assert.Equal(MemoryCorrectionOutcome.NotFound,
            (await service.CorrectAsync(Owner, Guid.CreateVersion7(), "Whatever", CancellationToken.None)).Outcome);
        Assert.Equal(MemoryCorrectionOutcome.NotCurrent,
            (await service.CorrectAsync(Owner, ended.Id, "Newer text", CancellationToken.None)).Outcome);
        Assert.Equal(MemoryCorrectionOutcome.Unchanged,
            (await service.CorrectAsync(Owner, same.Id, " Same text ", CancellationToken.None)).Outcome);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CorrectAsync(Owner, same.Id, "no", CancellationToken.None));
    }

    [Fact]
    public async Task The_review_names_the_source_chat_profile_and_how_often_it_was_corrected()
    {
        var messageId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var learned = Memory(sourceId: messageId, profile: profileId);
        var message = new Message(chatId, "user", "Rosa prefers Fridays", messageId);
        var service = new MemoryReviewService(
            Fake<IMemoryService>.Create(("ListAsync", _ => (IReadOnlyList<MemoryRecord>)[learned])),
            Fake<IMemoryRepository>.Create(("ListHistoryAsync", _ => (IReadOnlyList<MemoryRecord>)[Memory(), Memory()])),
            Fake<IConversationStore>.Create(("FindMessageAsync", _ => new MessageSearchHit(message, "Planning"))),
            Fake<IAssistantProfileService>.Create(("ListAsync", _ => (IReadOnlyList<AssistantProfileRecord>)[Profile(profileId, "Work")])),
            new FixedClock(Now));

        var entry = Assert.Single(await service.GetReviewAsync(Owner, CancellationToken.None));

        Assert.Equal("conversation", entry.Source.Type);
        Assert.Equal("From the chat “Planning”", entry.Source.Label);
        Assert.Equal(chatId, entry.Source.ConversationId);
        Assert.Equal(messageId, entry.Source.MessageId);
        Assert.Equal("Work", entry.ProfileName);
        Assert.Equal(2, entry.Corrections);
    }

    [Fact]
    public async Task A_source_chat_that_is_gone_says_so_instead_of_linking()
    {
        var learned = Memory(sourceId: Guid.CreateVersion7());
        var service = new MemoryReviewService(
            Fake<IMemoryService>.Create(("ListAsync", _ => (IReadOnlyList<MemoryRecord>)[learned])),
            Fake<IMemoryRepository>.Create(("ListHistoryAsync", _ => (IReadOnlyList<MemoryRecord>)[])),
            Fake<IConversationStore>.Create(("FindMessageAsync", _ => (MessageSearchHit?)null)),
            Fake<IAssistantProfileService>.Create(("ListAsync", _ => (IReadOnlyList<AssistantProfileRecord>)[])),
            new FixedClock(Now));

        var entry = Assert.Single(await service.GetReviewAsync(Owner, CancellationToken.None));

        Assert.Null(entry.Source.ConversationId);
        Assert.Contains("no longer there", entry.Source.Label);
    }

    private static AssistantProfileRecord Profile(Guid id, string name) =>
        new(id, Owner, name, null, false, 1, null, null, null, true, false, [], false, [], null, null, null, null,
            "all", true, true, true, true, Now, Now);
}
