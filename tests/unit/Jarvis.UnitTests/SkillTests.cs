using Jarvis.Agents.Skills;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Profiles;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SkillTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000beef");

    [Fact]
    public void Parse_reads_front_matter_and_body()
    {
        var draft = SkillMarkdown.Parse("""
            ---
            name: Trip Planning
            description: "Plan trips the way the user likes: \"quiet hotels\", trains first."
            metadata:
              author: someone
            ---

            1. Ask for dates if missing.
            2. Prefer trains under five hours.
            """);

        Assert.Equal("trip-planning", draft.Name);
        Assert.Equal("Plan trips the way the user likes: \"quiet hotels\", trains first.", draft.Description);
        Assert.StartsWith("1. Ask for dates", draft.Instructions);
    }

    [Fact]
    public void Serialize_round_trips_through_parse()
    {
        var skill = new SkillRecord(Guid.NewGuid(), Owner, "weekly-review", "Run the Friday review with \"wins\" first.",
            "1. List completed tasks.\n2. Ask about blockers.", SkillSources.Learned, SkillStatuses.Active, false, 3,
            0, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var markdown = SkillMarkdown.Serialize(skill);
        var parsed = SkillMarkdown.Parse(markdown);

        Assert.Contains("source: jarvis-learned", markdown);
        Assert.Equal(skill.Name, parsed.Name);
        Assert.Equal(skill.Description, parsed.Description);
        Assert.Equal(skill.Instructions, parsed.Instructions);
    }

    [Theory]
    [InlineData("no front matter at all")]
    [InlineData("---\nname: ok-name\ndescription: short\n---\nThis body is long enough to pass.")]
    [InlineData("---\nname: x\ndescription: A description that is long enough\n---\nThis body is long enough to pass.")]
    [InlineData("---\nname: fine-name\ndescription: A description that is long enough\n---\ntoo short")]
    public void Parse_rejects_invalid_skills(string markdown)
    {
        Assert.Throws<ArgumentException>(() => SkillMarkdown.Parse(markdown));
    }

    [Fact]
    public async Task SaveSkill_creates_an_active_learned_skill_and_notifies()
    {
        var (tools, repository, notifications, _) = Create(new LearningSettings(AutoActivateSkills: true));

        var result = await tools.SaveSkillAsync("inbox-triage", "Triage the user's inbox into reply, delegate, archive.",
            "1. Search mail.\n2. Group by sender importance.\n3. Draft replies for the top three.");

        var skill = Assert.Single(repository.Items);
        Assert.Contains("saved and active", result);
        Assert.Equal(SkillStatuses.Active, skill.Status);
        Assert.Equal(SkillSources.Learned, skill.Source);
        Assert.Equal("skill.learned", Assert.Single(notifications.Created).Type);
    }

    [Fact]
    public void Learned_skills_wait_for_review_by_default()
    {
        Assert.False(LearningSettings.Default.AutoActivateSkills);
        Assert.True(LearningSettings.Default.HeartbeatEnabled);
        Assert.True(LearningSettings.Default.DreamingEnabled);
    }

    [Fact]
    public async Task SaveSkill_proposes_when_auto_activation_is_off_and_respects_opt_out()
    {
        var (tools, repository, _, settings) = Create(new LearningSettings(AutoActivateSkills: false));

        var proposed = await tools.SaveSkillAsync("meal-plan", "Plan weekly meals around the user's diet.",
            "1. Check preferences in memory.\n2. Propose five dinners.");
        Assert.Contains("proposed", proposed);
        Assert.Equal(SkillStatuses.Proposed, repository.Items.Single().Status);
        Assert.Contains("must activate", await tools.LoadSkillAsync("meal-plan"));

        await settings.SaveAsync(Owner, SettingsSections.Learning, new LearningSettings(AutoCreateSkills: false),
            CancellationToken.None);
        var refused = await tools.SaveSkillAsync("another-skill", "A description that is long enough.",
            "Steps that are long enough to be valid.");
        Assert.Contains("turned off", refused);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task SaveSkill_never_overwrites_a_locked_user_skill_or_stores_secrets()
    {
        var (tools, repository, _, _) = Create(new LearningSettings());
        await repository.UpsertAsync(Owner, new SkillDraft("email-drafts", "How the user wants emails drafted.",
            "Always sign off with 'Best, Sam'."), SkillSources.User, SkillStatuses.Active, false, null,
            CancellationToken.None);

        var locked = await tools.SaveSkillAsync("email-drafts", "Different description for email drafts.",
            "Overwrite the user's signature preference.");
        var secret = await tools.SaveSkillAsync("deploy-steps", "How to deploy the user's service.",
            "Use the token ghp_abcdefghijklmnopqrstuvwxyz0123456789 to push.");

        Assert.Contains("locked", locked);
        Assert.Contains("credential", secret);
        Assert.Equal("Always sign off with 'Best, Sam'.", repository.Items.Single().Instructions);
    }

    [Fact]
    public async Task LoadSkill_returns_instructions_and_counts_use()
    {
        var (tools, repository, _, _) = Create(new LearningSettings(AutoActivateSkills: true));
        await tools.SaveSkillAsync("weekly-review", "Run the user's Friday weekly review.",
            "1. List completed tasks.\n2. Ask about blockers.");

        var loaded = await tools.LoadSkillAsync("Weekly Review");

        Assert.Contains("1. List completed tasks.", loaded);
        Assert.Equal(1, repository.Items.Single().UseCount);
    }

    [Fact]
    public async Task Context_lists_only_active_skills_by_name_and_description()
    {
        var repository = new InMemorySkillRepository();
        await repository.UpsertAsync(Owner, new SkillDraft("active-skill", "Use for active things.", "Body text long enough."),
            SkillSources.Learned, SkillStatuses.Active, true, null, CancellationToken.None);
        await repository.UpsertAsync(Owner, new SkillDraft("proposed-skill", "Use for proposed things.", "Body text long enough."),
            SkillSources.Learned, SkillStatuses.Proposed, true, null, CancellationToken.None);

        var provider = new SkillsContextProvider(repository, Owner, null);
        var agent = new Microsoft.Agents.AI.ChatClientAgent(new CapturingClient(), new Microsoft.Agents.AI.ChatClientAgentOptions
        {
            AIContextProviders = [provider]
        });
        var client = (CapturingClient)agent.ChatClient.GetService(typeof(CapturingClient))!;
        await agent.RunAsync("hello");

        Assert.Contains("- active-skill: Use for active things.", client.LastPrompt);
        Assert.DoesNotContain("proposed-skill", client.LastPrompt);
        Assert.DoesNotContain("Body text", client.LastPrompt);
    }

    [Fact]
    public async Task Context_hides_skills_the_bound_profile_does_not_enable()
    {
        var repository = new InMemorySkillRepository();
        var allowed = await repository.UpsertAsync(Owner,
            new SkillDraft("briefing", "Morning briefing steps.", "Body text long enough."),
            SkillSources.User, SkillStatuses.Active, true, null, CancellationToken.None);
        await repository.UpsertAsync(Owner, new SkillDraft("coding", "How to review pull requests.",
            "Body text long enough."), SkillSources.User, SkillStatuses.Active, true, null, CancellationToken.None);
        var snapshot = new AssistantProfileSnapshot(Guid.CreateVersion7(), 1, "Work", null, null, null, null, true,
            true, [allowed!.Id], false, [], null, null, null, null, MemoryScopes.All, true, true, true, true,
            DateTimeOffset.UtcNow);

        var provider = new SkillsContextProvider(repository, Owner, snapshot);
        var agent = new Microsoft.Agents.AI.ChatClientAgent(new CapturingClient(), new Microsoft.Agents.AI.ChatClientAgentOptions
        {
            AIContextProviders = [provider]
        });
        var client = (CapturingClient)agent.ChatClient.GetService(typeof(CapturingClient))!;
        await agent.RunAsync("hello");

        Assert.Contains("- briefing: Morning briefing steps.", client.LastPrompt);
        Assert.DoesNotContain("coding", client.LastPrompt);
    }

    private static (SkillAgentTools, InMemorySkillRepository, RecordingNotifications, InMemorySettingsStore) Create(
        LearningSettings learning)
    {
        var repository = new InMemorySkillRepository();
        var notifications = new RecordingNotifications();
        var settings = new InMemorySettingsStore();
        settings.SaveAsync(Owner, SettingsSections.Learning, learning, CancellationToken.None).GetAwaiter().GetResult();
        return (new SkillAgentTools(repository, settings, notifications, new NullAudit(), new OwnerUser(), null),
            repository, notifications, settings);
    }

    private sealed class OwnerUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    internal sealed class CapturingClient : Microsoft.Extensions.AI.IChatClient
    {
        public string LastPrompt { get; private set; } = string.Empty;
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastPrompt = string.Join("\n", messages.Select(message => message.Text));
            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "ok")));
        }
        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new Microsoft.Extensions.AI.ChatResponseUpdate(Microsoft.Extensions.AI.ChatRole.Assistant, response.Text);
        }
    }
}

internal sealed class InMemorySkillRepository : ISkillRepository
{
    public List<SkillRecord> Items { get; } = [];

    public Task<IReadOnlyList<SkillRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SkillRecord>>(Items.Where(x => x.OwnerId == ownerId).ToArray());

    public Task<SkillRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

    public Task<SkillRecord?> FindByNameAsync(Guid ownerId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(x => x.OwnerId == ownerId && x.Name == name));

    public Task<IReadOnlyList<SkillRevisionRecord>> ListRevisionsAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SkillRevisionRecord>>([]);

    public Task<SkillRecord?> UpsertAsync(Guid ownerId, SkillDraft draft, string source, string statusForNew,
        bool respectLock, string? changeNote, CancellationToken cancellationToken)
    {
        var index = Items.FindIndex(x => x.OwnerId == ownerId && x.Name == draft.Name);
        var now = DateTimeOffset.UtcNow;
        if (index < 0)
        {
            var created = new SkillRecord(Guid.NewGuid(), ownerId, draft.Name, draft.Description, draft.Instructions,
                source, statusForNew, source == SkillSources.User, 1, 0, null, now, now);
            Items.Add(created);
            return Task.FromResult<SkillRecord?>(created);
        }
        if (respectLock && Items[index].IsLocked) return Task.FromResult<SkillRecord?>(null);
        Items[index] = Items[index] with
        {
            Description = draft.Description, Instructions = draft.Instructions, Version = Items[index].Version + 1,
            UpdatedAt = now
        };
        return Task.FromResult<SkillRecord?>(Items[index]);
    }

    public Task<SkillRecord?> SetStatusAsync(Guid id, Guid ownerId, string status, CancellationToken cancellationToken) =>
        Update(id, skill => skill with { Status = status });

    public Task<SkillRecord?> SetLockedAsync(Guid id, Guid ownerId, bool locked, CancellationToken cancellationToken) =>
        Update(id, skill => skill with { IsLocked = locked });

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(x => x.Id == id) > 0);

    public Task RecordUseAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        Update(id, skill => skill with { UseCount = skill.UseCount + 1, LastUsedAt = DateTimeOffset.UtcNow });

    private Task<SkillRecord?> Update(Guid id, Func<SkillRecord, SkillRecord> change)
    {
        var index = Items.FindIndex(x => x.Id == id);
        if (index < 0) return Task.FromResult<SkillRecord?>(null);
        Items[index] = change(Items[index]);
        return Task.FromResult<SkillRecord?>(Items[index]);
    }
}

internal sealed class RecordingNotifications : INotificationRepository
{
    public List<NotificationRecord> Created { get; } = [];

    public Task<IReadOnlyList<NotificationRecord>> ListNotificationsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecord>>(Created);

    public Task<NotificationRecord?> GetNotificationAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(Created.FirstOrDefault(x => x.Id == id));

    public Task<bool> MarkReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<NotificationRecord> CreateAsync(Guid ownerId, string type, string title, string body, Guid? sourceId,
        CancellationToken cancellationToken)
    {
        var record = new NotificationRecord(Guid.NewGuid(), type, title, body, sourceId, DateTimeOffset.UtcNow, null);
        Created.Add(record);
        return Task.FromResult(record);
    }
}

internal sealed class NullAudit : IAuditEventStore
{
    public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass, bool success,
        Guid? approvalId, string? metadataJson, CancellationToken cancellationToken, Guid? agentRunId = null) =>
        Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass, approvalId,
            DateTimeOffset.UtcNow, success, metadataJson));

    public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
}
