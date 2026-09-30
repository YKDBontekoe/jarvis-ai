using Jarvis.Agents.Journal;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Journal;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Journal;
using Jarvis.Domain.Memory;
using Jarvis.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class JournalTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000cccc");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Fact]
    public async Task Creating_an_entry_mirrors_it_into_memory_with_ratings()
    {
        var (service, journal, memories) = Create();

        var entry = await service.CreateAsync(Owner, new JournalDraft(Today, "  Long walk with Anna.  ", "Sunset",
            "Good coffee", 8, 4, 3, 2, ["#Family", "family", " Health "]), default);

        Assert.Equal("Long walk with Anna.", entry.Content);
        Assert.Equal(["family", "health"], entry.Tags);
        var memory = Assert.Single(memories.Items);
        Assert.Equal(entry.MemoryId, memory.Id);
        Assert.Equal("journal", memory.Kind);
        Assert.Equal("journal", memory.SourceType);
        Assert.Equal(entry.Id, memory.SourceId);
        Assert.Contains("Wednesday, 30 September 2026", memory.Content);
        Assert.Contains("day rating 8/10", memory.Content);
        Assert.Contains("mood 4/5", memory.Content);
        Assert.Contains("Highlights: Sunset", memory.Content);
        Assert.Contains("Long walk with Anna.", memory.Content);
        Assert.Equal(entry.MemoryId, journal.Items.Single().MemoryId);
    }

    [Fact]
    public async Task Updating_an_entry_rewrites_its_memory_and_keeps_pin_state()
    {
        var (service, _, memories) = Create();
        var entry = await service.CreateAsync(Owner, Draft("First draft"), default);
        memories.Items[0] = memories.Items[0] with { IsPinned = true };

        var updated = await service.UpdateAsync(entry.Id, Owner, Draft("Second draft", mood: 5), default);

        Assert.NotNull(updated);
        var memory = Assert.Single(memories.Items);
        Assert.Contains("Second draft", memory.Content);
        Assert.Contains("mood 5/5", memory.Content);
        Assert.True(memory.IsPinned);
    }

    [Fact]
    public async Task Updating_recreates_a_memory_the_user_deleted()
    {
        var (service, journal, memories) = Create();
        var entry = await service.CreateAsync(Owner, Draft("Entry"), default);
        memories.Items.Clear();

        var updated = await service.UpdateAsync(entry.Id, Owner, Draft("Entry, edited"), default);

        var memory = Assert.Single(memories.Items);
        Assert.Equal(memory.Id, updated!.MemoryId);
        Assert.Equal(memory.Id, journal.Items.Single().MemoryId);
    }

    [Fact]
    public async Task Deleting_an_entry_deletes_its_memory_and_is_owner_scoped()
    {
        var (service, journal, memories) = Create();
        var entry = await service.CreateAsync(Owner, Draft("Private"), default);

        Assert.False(await service.DeleteAsync(entry.Id, Other, default));
        Assert.Single(journal.Items);
        Assert.Single(memories.Items);

        Assert.True(await service.DeleteAsync(entry.Id, Owner, default));
        Assert.Empty(journal.Items);
        Assert.Empty(memories.Items);
    }

    [Fact]
    public async Task Entry_survives_when_the_memory_mirror_fails()
    {
        var (service, journal, memories) = Create();
        memories.FailCreates = true;

        var entry = await service.CreateAsync(Owner, Draft("Still saved"), default);

        Assert.Null(entry.MemoryId);
        Assert.Single(journal.Items);
    }

    [Fact]
    public async Task Merge_appends_text_and_replaces_supplied_ratings_for_the_same_day()
    {
        var (service, journal, memories) = Create();
        await service.CreateAsync(Owner, Draft("Morning was slow.", mood: 2, tags: ["work"]), default);

        var (entry, merged) = await service.MergeAsync(Owner,
            Draft("Evening was great.", mood: 5, energy: 4, tags: ["friends", "work"]), default);

        Assert.True(merged);
        Assert.Single(journal.Items);
        Assert.Equal("Morning was slow.\n\nEvening was great.", entry.Content);
        Assert.Equal(5, entry.Mood);
        Assert.Equal(4, entry.Energy);
        Assert.Equal(["work", "friends"], entry.Tags);
        Assert.Contains("Evening was great.", Assert.Single(memories.Items).Content);
    }

    [Fact]
    public async Task Merge_creates_an_entry_when_the_day_has_none()
    {
        var (service, journal, _) = Create();

        var (_, merged) = await service.MergeAsync(Owner, Draft("Fresh"), default);

        Assert.False(merged);
        Assert.Single(journal.Items);
    }

    [Fact]
    public async Task Summary_averages_ratings_and_counts_the_streak()
    {
        var (service, _, _) = Create();
        foreach (var (offset, mood) in new[] { (0, 4), (-1, 2), (-2, 3), (-4, 5) })
            await service.CreateAsync(Owner, Draft("Day", mood: mood, date: Today.AddDays(offset)), default);
        await service.CreateAsync(Other, Draft("Not mine", mood: 1), default);

        var summary = await service.SummarizeAsync(Owner, 30, Today, default);

        Assert.Equal(4, summary.TotalEntries);
        Assert.Equal(3, summary.CurrentStreak);
        Assert.Equal(3.5, summary.AverageMood);
        Assert.Null(summary.AverageStress);
        Assert.Equal(4, summary.Series.Count);
        Assert.True(summary.Series[0].Date < summary.Series[^1].Date);
    }

    [Fact]
    public void Streak_may_end_yesterday_but_not_earlier()
    {
        HashSet<DateOnly> dates = [Today.AddDays(-1), Today.AddDays(-2)];
        Assert.Equal(2, JournalService.Streak(dates, Today));
        Assert.Equal(0, JournalService.Streak(dates, Today.AddDays(1)));
    }

    [Fact]
    public void Validation_rejects_empty_out_of_range_and_future_drafts()
    {
        Assert.Contains("content", JournalRules.Validate(Draft(null), Today).Keys);
        Assert.Contains("rating", JournalRules.Validate(Draft("x", rating: 11), Today).Keys);
        Assert.Contains("mood", JournalRules.Validate(Draft("x", mood: 0), Today).Keys);
        Assert.Contains("stress", JournalRules.Validate(Draft("x", stress: 6), Today).Keys);
        Assert.Contains("entryDate", JournalRules.Validate(Draft("x", date: Today.AddDays(3)), Today).Keys);
        Assert.Contains("source", JournalRules.Validate(Draft("x") with { Source = "telepathy" }, Today).Keys);
        Assert.Contains("tags", JournalRules.Validate(
            Draft("x", tags: Enumerable.Range(0, 11).Select(i => $"t{i}").ToArray()), Today).Keys);
        Assert.Contains("content", JournalRules.Validate(
            Draft(new string('a', JournalRules.MaxContentLength + 1)), Today).Keys);
        Assert.Empty(JournalRules.Validate(Draft(null, mood: 3), Today));
        Assert.Empty(JournalRules.Validate(Draft("Fine", date: Today.AddDays(1)), Today));
    }

    [Fact]
    public void Memory_text_is_capped_for_very_long_entries()
    {
        var entry = new JournalEntry(Guid.NewGuid(), Owner, Today, "written", new string('a', 20_000), null, null, null,
            null, null, null, [], null, Now, Now);

        Assert.True(JournalMemoryFormatter.Format(entry).Length <= 8_000);
    }

    [Fact]
    public async Task Tool_saves_an_entry_in_the_owners_local_day_and_audits()
    {
        var (service, journal, memories) = Create();
        var audit = new RecordingAudit();
        // 23:30 UTC on the 30th is already the 1st in Amsterdam (UTC+2).
        var tools = CreateTools(service, audit, "Europe/Amsterdam", new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero));

        var result = await tools.SaveJournalEntryAsync("I finished the report and felt proud.", mood: 4,
            source: "voice");

        var entry = Assert.Single(journal.Items);
        Assert.Equal(new DateOnly(2026, 10, 1), entry.EntryDate);
        Assert.Equal("voice", entry.Source);
        Assert.Equal(4, entry.Mood);
        Assert.Contains(entry.Id.ToString(), result);
        Assert.Equal("journal.created", Assert.Single(audit.Actions));
        Assert.Contains("felt proud", Assert.Single(memories.Items).Content);
    }

    [Fact]
    public async Task Tool_appends_to_todays_entry_and_never_labels_agent_entries_as_written()
    {
        var (service, journal, _) = Create();
        var audit = new RecordingAudit();
        var tools = CreateTools(service, audit);

        await tools.SaveJournalEntryAsync("Breakfast with Sam.", source: "written");
        var second = await tools.SaveJournalEntryAsync("Dinner was quiet.");

        var entry = Assert.Single(journal.Items);
        Assert.Equal("chat", entry.Source);
        Assert.Equal("Breakfast with Sam.\n\nDinner was quiet.", entry.Content);
        Assert.StartsWith("Added to", second);
        Assert.Equal(["journal.created", "journal.updated"], audit.Actions);
    }

    [Fact]
    public async Task Tool_reports_invalid_input_instead_of_saving()
    {
        var (service, journal, _) = Create();
        var tools = CreateTools(service, new RecordingAudit());

        Assert.Contains("Mood must be", await tools.SaveJournalEntryAsync("Hi", mood: 9));
        Assert.Contains("YYYY-MM-DD", await tools.SaveJournalEntryAsync("Hi", date: "yesterday"));
        Assert.Empty(journal.Items);
    }

    [Fact]
    public async Task Tool_lists_recent_entries_with_ratings()
    {
        var (service, _, _) = Create();
        await service.CreateAsync(Owner, Draft("Quiet Sunday.", mood: 3, rating: 6), default);
        var tools = CreateTools(service, new RecordingAudit());

        var listing = await tools.ListJournalEntriesAsync(7);

        Assert.Contains("2026-09-30", listing);
        Assert.Contains("mood 3/5", listing);
        Assert.Contains("day 6/10", listing);
        Assert.Contains("Quiet Sunday.", listing);
        Assert.Equal("There are no journal entries in that period.",
            await CreateTools(Create().Service, new RecordingAudit()).ListJournalEntriesAsync());
    }

    [Fact]
    public void Journal_is_a_valid_memory_kind()
    {
        Assert.True(MemoryKinds.IsValid("journal"));
        Assert.True(MemoryKinds.IsValidFilter("journal"));
    }

    private static JournalDraft Draft(string? content, int? rating = null, int? mood = null, int? energy = null,
        int? stress = null, string[]? tags = null, DateOnly? date = null) =>
        new(date ?? Today, content, null, null, rating, mood, energy, stress, tags);

    private static (JournalService Service, FakeJournalRepository Journal, FakeMemories Memories) Create()
    {
        var journal = new FakeJournalRepository();
        var memories = new FakeMemories();
        return (new JournalService(journal, memories, NullLogger<JournalService>.Instance, new FixedClock(Now)),
            journal, memories);
    }

    private static JournalAgentTools CreateTools(JournalService service, RecordingAudit audit, string? zone = null,
        DateTimeOffset? now = null)
    {
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => zone is null
            ? null
            : new DailyBriefingPreferenceRecord(Owner, true, new TimeOnly(8, 0), zone, "wf", null, null)));
        return new JournalAgentTools(service, briefings, audit, new FixedUser(), NullLogger.Instance,
            new FixedClock(now ?? Now));
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeJournalRepository : IJournalRepository
    {
        public List<JournalEntry> Items { get; } = [];

        public Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            Items.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<JournalEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<JournalEntry?> UpdateAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(x => x.Id == entry.Id && x.OwnerId == entry.OwnerId);
            if (index < 0) return Task.FromResult<JournalEntry?>(null);
            // Like the database, an update never changes the memory link.
            Items[index] = entry with { MemoryId = Items[index].MemoryId };
            return Task.FromResult<JournalEntry?>(Items[index]);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task SetMemoryIdAsync(Guid id, Guid ownerId, Guid? memoryId, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(x => x.Id == id && x.OwnerId == ownerId);
            if (index >= 0) Items[index] = Items[index] with { MemoryId = memoryId };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JournalEntry>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JournalEntry>>(Items
                .Where(x => x.OwnerId == ownerId && (from is null || x.EntryDate >= from) &&
                            (to is null || x.EntryDate <= to))
                .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt).Take(limit).ToArray());
    }

    private sealed class FakeMemories : IMemoryService
    {
        public List<MemoryRecord> Items { get; } = [];
        public bool FailCreates { get; set; }

        public Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance,
            float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken,
            string sourceType = "user", Guid? sourceId = null, Guid? profileId = null)
        {
            if (FailCreates) throw new InvalidOperationException("memory store unavailable");
            var record = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, sourceType,
                sourceId, Now, Now, validUntil, isPinned, profileId);
            Items.Add(record);
            return Task.FromResult(record);
        }

        public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
            float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken)
        {
            var index = Items.FindIndex(x => x.Id == id && x.OwnerId == ownerId);
            if (index < 0) return Task.FromResult<MemoryRecord?>(null);
            Items[index] = Items[index] with
            {
                Kind = kind, Content = content, Importance = importance, Confidence = confidence,
                ValidUntil = validUntil, IsPinned = isPinned
            };
            return Task.FromResult<MemoryRecord?>(Items[index]);
        }

        public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id && x.OwnerId == ownerId);
            return Task.CompletedTask;
        }

        public Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
            float importance, float confidence, CancellationToken cancellationToken,
            string sourceType = "conversation", Guid? sourceId = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
            CancellationToken cancellationToken, string? kind = null) => throw new NotSupportedException();
    }
}
