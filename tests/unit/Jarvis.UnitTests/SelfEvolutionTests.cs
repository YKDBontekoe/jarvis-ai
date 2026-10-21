using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.Improvements;
using Jarvis.Agents.Skills;
using Jarvis.Application.Conversations;
using Jarvis.Application.Improvements;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Domain.Improvements;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Skills that know their tools, Jarvis writing its own lessons and retiring its own skills, capability gaps, and the
/// unattended build of a self-built app for a gap.
/// </summary>
public sealed class SelfEvolutionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000e701");
    private static readonly Guid Conversation = Guid.Parse("01996b8c-6000-7000-8000-00000000e702");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Skills and their tools ----

    [Fact]
    public void Required_tools_round_trip_through_skill_markdown()
    {
        var skill = new SkillRecord(Guid.NewGuid(), Owner, "train-check", "Check train departures before a trip.",
            "1. Call get_departures for the station.\n2. Summarise delays.", SkillSources.Learned,
            SkillStatuses.Active, false, 2, 0, null, Now, Now, RequiredToolList: ["get_departures", "get_delays"]);

        var markdown = SkillMarkdown.Serialize(skill);
        var parsed = SkillMarkdown.Parse(markdown);

        Assert.Contains("  requires-tools: get_departures, get_delays", markdown);
        Assert.Equal(["get_departures", "get_delays"], parsed.RequiredTools);
    }

    [Fact]
    public void A_skill_file_without_tools_keeps_whatever_tools_the_skill_has()
    {
        var parsed = SkillMarkdown.Parse("---\nname: plain-skill\ndescription: A description that is long enough\n---\nThis body is long enough to pass.");

        Assert.Null(parsed.RequiredTools);
    }

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("tool with spaces")]
    [InlineData("-leading-dash")]
    public void Tool_names_must_look_like_tool_names(string tool)
    {
        Assert.Throws<ArgumentException>(() => SkillMarkdown.NormalizeTools([tool]));
    }

    [Fact]
    public void A_skill_relies_on_at_most_ten_tools_and_duplicates_collapse()
    {
        Assert.Throws<ArgumentException>(() =>
            SkillMarkdown.NormalizeTools(Enumerable.Range(0, 11).Select(index => $"tool_{index}")));
        Assert.Equal(["a_tool"], SkillMarkdown.NormalizeTools([" a_tool ", "a_tool", ""]));
    }

    [Fact]
    public async Task SaveSkill_keeps_the_tools_it_relies_on_and_refuses_tools_it_cannot_call()
    {
        var (tools, repository, _, catalog) = Skills(new LearningSettings(AutoActivateSkills: true));
        catalog.Set(["LoadSkill", "get_departures"]);

        var refused = await tools.SaveSkillAsync("train-check", "Check train departures before a trip.",
            "1. Call get_departures for the station.", requiredTools: ["get_departures", "made_up_tool"]);
        Assert.Contains("cannot call made_up_tool", refused);
        Assert.Empty(repository.Items);

        await tools.SaveSkillAsync("train-check", "Check train departures before a trip.",
            "1. Call get_departures for the station.", requiredTools: ["get_departures"]);
        Assert.Equal(["get_departures"], repository.Items.Single().RequiredTools);

        // Saving again without tools keeps them.
        await tools.SaveSkillAsync("train-check", "Check train departures before a trip, then delays.",
            "1. Call get_departures for the station.\n2. Mention delays.");
        Assert.Equal(["get_departures"], repository.Items.Single().RequiredTools);
    }

    [Fact]
    public async Task LoadSkill_says_which_tools_are_missing_instead_of_handing_out_steps_that_cannot_work()
    {
        var (tools, repository, _, catalog) = Skills(new LearningSettings(AutoActivateSkills: true));
        await tools.SaveSkillAsync("train-check", "Check train departures before a trip.",
            "1. Call get_departures for the station.", requiredTools: ["get_departures"]);

        catalog.Set(["LoadSkill"]);
        var missing = await tools.LoadSkillAsync("train-check");
        Assert.Contains("not available now: get_departures", missing);
        Assert.Equal(0, repository.Items.Single().UseCount);

        catalog.Set(["LoadSkill", "get_departures"]);
        var loaded = await tools.LoadSkillAsync("train-check");
        Assert.Contains("Tools it uses: get_departures.", loaded);
        Assert.Contains("1. Call get_departures", loaded);
    }

    [Fact]
    public async Task ListSkills_shows_how_each_skill_is_doing()
    {
        var (tools, repository, _, catalog) = Skills(new LearningSettings(AutoActivateSkills: true));
        await tools.SaveSkillAsync("train-check", "Check train departures before a trip.",
            "1. Call get_departures for the station.", requiredTools: ["get_departures"]);
        repository.Items[0] = repository.Items[0] with { OutcomeRuns = 5, OutcomeSuccesses = 4, UseCount = 7 };
        catalog.Set(["ListSkills"]);

        var listed = await tools.ListSkillsAsync();

        Assert.Contains("used 7×, went well 4 of 5", listed);
        Assert.Contains("Not available now: get_departures.", listed);
    }

    [Fact]
    public async Task RetireSkill_turns_off_a_learned_skill_and_tells_the_owner()
    {
        var (tools, repository, notifications, _) = Skills(new LearningSettings(AutoActivateSkills: true));
        await tools.SaveSkillAsync("old-flow", "An outdated way to plan meals.", "1. Do the old thing step by step.");

        var result = await tools.RetireSkillAsync("old-flow", "Replaced by meal-plan, which goes better.");

        Assert.Contains("is off", result);
        Assert.Equal(SkillStatuses.Disabled, repository.Items.Single().Status);
        Assert.Equal("skill.retired", notifications.Created.Last().Type);
    }

    [Fact]
    public async Task RetireSkill_never_touches_a_skill_the_owner_wrote()
    {
        var (tools, repository, _, _) = Skills(LearningSettings.Default);
        await repository.UpsertAsync(Owner, new SkillDraft("my-way", "The owner's own procedure for this.",
            "1. Do it exactly like the owner said."), SkillSources.User, SkillStatuses.Active, false, null, default);

        var result = await tools.RetireSkillAsync("my-way", "I think it is outdated.");

        Assert.Contains("written by the user", result);
        Assert.Equal(SkillStatuses.Active, repository.Items.Single().Status);
    }

    [Fact]
    public void The_tool_catalog_treats_everything_as_available_until_the_agent_is_built()
    {
        var catalog = new AgentToolCatalog();
        Assert.Empty(catalog.Missing(["anything"]));
        Assert.True(catalog.Contains("anything"));

        catalog.Set(["a"]);
        Assert.Equal(["b"], catalog.Missing(["a", "b"]));
    }

    [Fact]
    public void Activating_a_self_built_app_suggests_a_skill_naming_its_tools()
    {
        Assert.Contains("requiredTools [get_departures, get_delays]",
            CustomMcpAgentTools.SkillHint(["get_departures", "get_delays"]));
        Assert.Equal(string.Empty, CustomMcpAgentTools.SkillHint([]));
    }

    // ---- Lessons Jarvis writes itself ----

    [Fact]
    public async Task RecordLesson_puts_a_lesson_in_force_when_confident_changes_may_apply_themselves()
    {
        var world = new ImprovementWorld();
        var tools = world.Tools(["CreateReminder"]);

        var result = await tools.RecordLessonAsync("CreateReminder",
            "Pass dates to CreateReminder as ISO 8601 with the user's offset.");

        Assert.Contains("in force", result);
        var lesson = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Equal(["CreateReminder"], lesson.Payload.Tools);
        Assert.Contains("ISO 8601", (await world.Tools(["ListLessons"]).ListLessonsAsync()));
    }

    [Fact]
    public async Task RecordLesson_waits_for_review_when_the_owner_reviews_everything()
    {
        var world = new ImprovementWorld(new LearningSettings(AutoApplyLowRiskMemory: false));

        var result = await world.Tools(["CreateReminder"]).RecordLessonAsync("CreateReminder",
            "Pass dates to CreateReminder as ISO 8601 with the user's offset.");

        Assert.Contains("waiting for the user's review", result);
        Assert.Empty(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Equal(ImprovementStatuses.Pending, Assert.Single(world.Repository.Items).Status);
    }

    [Fact]
    public async Task RecordLesson_refuses_unknown_tools_a_second_lesson_and_an_owner_who_opted_out()
    {
        var world = new ImprovementWorld();
        var tools = world.Tools(["CreateReminder"]);
        const string text = "Pass dates to CreateReminder as ISO 8601 with the user's offset.";

        Assert.Contains("no tool named", await tools.RecordLessonAsync("MadeUp", text));
        await tools.RecordLessonAsync("CreateReminder", text);
        Assert.Contains("already has a lesson", await tools.RecordLessonAsync("CreateReminder", "Another rule for the same tool here."));

        var optedOut = new ImprovementWorld(new LearningSettings(ProposeImprovements: false));
        Assert.Contains("turned off", await optedOut.Tools(["CreateReminder"]).RecordLessonAsync("CreateReminder", text));
        Assert.Empty(optedOut.Repository.Items);
    }

    // ---- Capability gaps ----

    [Theory]
    [InlineData("Live train departures", "live-train-departures")]
    [InlineData("  control Philips_Hue lights ", "control-philips-hue-lights")]
    [InlineData("call 0612345678", null)]
    [InlineData("email jan@example.com", null)]
    [InlineData("open https://example.com", null)]
    [InlineData("a b c d e f g", null)]
    [InlineData("x", null)]
    public void Capability_labels_keep_no_personal_details(string input, string? expected)
    {
        Assert.Equal(expected, CapabilityGapRules.NormalizeSlug(input));
    }

    [Fact]
    public void ReportCapabilityGap_records_only_the_label()
    {
        var world = new ImprovementWorld();

        var result = world.Tools([]).ReportCapabilityGap("Live train departures");

        Assert.Contains("live-train-departures", result);
        var signal = Assert.Single(world.Signals);
        Assert.Equal((LearningSignalKinds.CapabilityGap, "live-train-departures"), signal);
        Assert.Contains("no names", world.Tools([]).ReportCapabilityGap("call my mother on 0612345678"));
        Assert.Single(world.Signals);
    }

    [Fact]
    public void A_gap_needs_reports_from_more_than_one_conversation()
    {
        var once = new[] { Gap("live-train-departures", Guid.NewGuid()), Gap("live-train-departures", Conversation) };
        var sameConversation = new[] { Gap("hue-lights", Conversation), Gap("hue-lights", Conversation) };
        var stale = new[] { Gap("old-thing", Guid.NewGuid(), 40), Gap("old-thing", Guid.NewGuid(), 41) };

        var found = CapabilityGapMinerEngine.Mine([.. once, .. sameConversation, .. stale], Now);

        var finding = Assert.Single(found);
        Assert.Equal(("live-train-departures", 2, 2), (finding.Slug, finding.Reports, finding.Conversations));
    }

    [Fact]
    public async Task The_miner_offers_a_gap_and_keeps_a_built_app_after_its_reports_age_out()
    {
        var world = new ImprovementWorld();
        world.Store.Signals.AddRange([Gap("live-train-departures", Guid.NewGuid()), Gap("live-train-departures", Guid.NewGuid())]);

        var mined = await world.Miner().RefreshAsync(Owner, default);

        Assert.Equal(1, mined.Capabilities);
        var proposal = Assert.Single(world.Repository.Items);
        Assert.Equal(ImprovementKinds.Capability, proposal.Kind);
        Assert.Equal("gap:live-train-departures", proposal.Fingerprint);
        Assert.Equal("Jarvis keeps lacking: live train departures", proposal.Title);

        // A tested draft was filed on it, then the reports aged out of the window.
        var built = Payload(proposal) with
        {
            BuildStatus = CapabilityBuildStatuses.Built, CustomServerId = Guid.NewGuid(), Sha256 = "abc", AppName = "Trains"
        };
        await world.Repository.UpdateAsync(proposal with { PayloadJson = JsonSerializer.Serialize(built, Json) }, default);
        world.Store.Signals.Clear();
        await world.Miner().RefreshAsync(Owner, default);

        var kept = Assert.Single(world.Repository.Items);
        Assert.True(Payload(kept).IsBuilt);
    }

    [Fact]
    public async Task Accepting_a_built_app_switches_on_exactly_the_tested_draft_and_undo_pauses_it()
    {
        var world = new ImprovementWorld();
        var app = world.AddApp(tested: true);
        var proposal = await world.AddGap(new CapabilityPayload("live-train-departures", 3, 2,
            CapabilityBuildStatuses.Built, app.Server.Id, app.Server.DraftSha256, "Trains", ["get_departures"]));

        var view = Assert.IsType<ImprovementView>(await world.Service().AcceptAsync(proposal.Id, Owner, default));

        Assert.Equal(ImprovementStatuses.Accepted, view.Status);
        Assert.Equal([(app.Server.RegistryServerId, true)], world.Enabled);
        Assert.Equal(app.Server.DraftSha256, world.ActivatedSha);

        Assert.True(await world.Service().UndoAsync(proposal.Id, Owner, default));
        Assert.Equal((app.Server.RegistryServerId, false), world.Enabled.Last());
    }

    [Fact]
    public async Task Accepting_a_gap_without_an_app_records_that_the_owner_wants_it()
    {
        var world = new ImprovementWorld();
        var proposal = await world.AddGap(new CapabilityPayload("live-train-departures", 3, 2));

        var listed = Assert.Single(await world.Service().ListAsync(Owner, default));
        Assert.Equal("I want this", listed.AcceptLabel);
        var view = await world.Service().AcceptAsync(proposal.Id, Owner, default);

        Assert.Equal(ImprovementStatuses.Accepted, view!.Status);
        Assert.Empty(world.Enabled);
    }

    // ---- The unattended build ----

    [Fact]
    public async Task Evolution_does_nothing_until_the_owner_turns_it_on()
    {
        var world = new ImprovementWorld();
        await world.AddGap(new CapabilityPayload("live-train-departures", 3, 2));

        Assert.Equal(0, await world.Evolution().EvolveAsync(Owner, default));
        Assert.Empty(world.Builder.Built);
    }

    [Fact]
    public async Task Evolution_files_a_tested_draft_on_the_gap_for_the_owner_to_switch_on()
    {
        var world = new ImprovementWorld(new LearningSettings(AutonomousEvolution: true));
        var proposal = await world.AddGap(new CapabilityPayload("live-train-departures", 3, 2), ImprovementStatuses.Accepted);
        var appId = Guid.NewGuid();
        world.Builder.Result = new CapabilityBuildResult(CapabilityBuildStatuses.Built, appId, "sha-1", "Trains",
            ["get_departures"], []);

        Assert.Equal(1, await world.Evolution().EvolveAsync(Owner, default));

        var updated = (await world.Repository.GetAsync(proposal.Id, Owner, default))!;
        var payload = Payload(updated);
        Assert.Equal(["live-train-departures"], world.Builder.Built);
        Assert.Equal(ImprovementStatuses.Pending, updated.Status);
        Assert.True(payload.IsBuilt);
        Assert.Equal((appId, "sha-1", 1), (payload.CustomServerId!.Value, payload.Sha256!, payload.Attempts));
        Assert.StartsWith("Switch on “Trains”", updated.Title);
        Assert.Equal(CapabilityEvolutionService.NotificationType, world.Notifications.Created.Single().Type);
        Assert.Equal("Switch on", Assert.Single(await world.Service().ListAsync(Owner, default)).AcceptLabel);
    }

    [Fact]
    public async Task A_failed_build_waits_a_week_and_stops_after_three_attempts()
    {
        var world = new ImprovementWorld(new LearningSettings(AutonomousEvolution: true));
        await world.AddGap(new CapabilityPayload("live-train-departures", 3, 2));
        world.Builder.Result = new CapabilityBuildResult(CapabilityBuildStatuses.Failed, Note: "Tests failed.");

        await world.Evolution().EvolveAsync(Owner, default);
        await world.Evolution().EvolveAsync(Owner, default);
        Assert.Single(world.Builder.Built);

        world.Now = Now.AddDays(8);
        await world.Evolution().EvolveAsync(Owner, default);
        world.Now = Now.AddDays(16);
        await world.Evolution().EvolveAsync(Owner, default);
        world.Now = Now.AddDays(24);
        await world.Evolution().EvolveAsync(Owner, default);

        Assert.Equal(CapabilityGapRules.MaxBuildAttempts, world.Builder.Built.Count);
        Assert.Empty(world.Notifications.Created);
    }

    [Fact]
    public void Pick_next_prefers_a_gap_the_owner_accepted_then_the_most_wanted()
    {
        ImprovementProposal Proposal(string slug, int conversations, string status) =>
            new(Guid.NewGuid(), Owner, ImprovementKinds.Capability, "gap:" + slug, slug, "e", 0.5,
                JsonSerializer.Serialize(new CapabilityPayload(slug, conversations, conversations), Json), status, null,
                Now, Now);

        var picked = CapabilityEvolutionService.PickNext(
        [
            Proposal("popular", 9, ImprovementStatuses.Pending),
            Proposal("wanted", 2, ImprovementStatuses.Accepted),
            Proposal("dismissed", 20, ImprovementStatuses.Dismissed)
        ], Now);

        Assert.Equal("wanted", picked!.Value.Gap.Slug);
    }

    [Fact]
    public void The_builder_reads_the_model_answer_and_refuses_code_with_a_literal_key()
    {
        var ok = ModelCapabilityBuilder.Parse("""
            ```json
            {"feasible": true, "name": "Trains", "description": "Train departures.", "source": "api_key = os.environ['X']", "testTool": "get_departures", "testArguments": {"station": "Utrecht"}}
            ```
            """);
        Assert.NotNull(ok);
        Assert.Equal("Trains", ok.Name);
        Assert.Equal("Utrecht", ok.TestArguments!.Value.GetProperty("station").GetString());

        Assert.False(ModelCapabilityBuilder.Parse("""{"feasible": false, "reason": "No public API."}""")!.Feasible);
        Assert.Null(ModelCapabilityBuilder.Parse("""{"feasible": true, "source": "TOKEN = 'ghp_abcdefghijklmnopqrstuvwxyz123456'"}"""));
        Assert.Null(ModelCapabilityBuilder.Parse("not json"));
    }

    // ---- helpers ----

    private static LearningSignalRecord Gap(string slug, Guid conversation, int daysAgo = 1) =>
        new(Guid.NewGuid(), Owner, LearningSignalKinds.CapabilityGap, conversation, null, null, null, slug, null,
            Now.AddDays(-daysAgo));

    private static CapabilityPayload Payload(ImprovementProposal proposal) =>
        JsonSerializer.Deserialize<CapabilityPayload>(proposal.PayloadJson, Json)!;

    private static (SkillAgentTools Tools, InMemorySkillRepository Repository, RecordingNotifications Notifications,
        AgentToolCatalog Catalog) Skills(LearningSettings learning)
    {
        var repository = new InMemorySkillRepository();
        var notifications = new RecordingNotifications();
        var settings = new InMemorySettingsStore();
        settings.SaveAsync(Owner, SettingsSections.Learning, learning, CancellationToken.None).GetAwaiter().GetResult();
        var catalog = new AgentToolCatalog();
        return (new SkillAgentTools(repository, settings, notifications, new NullAudit(), new OwnerUser(), null,
            catalog: catalog), repository, notifications, catalog);
    }

    private sealed class OwnerUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class ImprovementWorld
    {
        public ImprovementWorld(LearningSettings? learning = null) =>
            Settings.SaveAsync(Owner, SettingsSections.Learning, learning ?? LearningSettings.Default, default)
                .GetAwaiter().GetResult();

        public DateTimeOffset Now { get; set; } = SelfEvolutionTests.Now;
        public InMemoryImprovementRepository Repository { get; } = new();
        public InMemorySettingsStore Settings { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public List<(string Kind, string? Category)> Signals { get; } = [];
        public StubStore Store { get; } = new();
        public StubBuilder Builder { get; } = new();
        public List<(string Id, bool Enabled)> Enabled { get; } = [];
        public string? ActivatedSha { get; private set; }
        private readonly Dictionary<Guid, CustomMcpServerDetail> _apps = [];

        public CustomMcpServerDetail AddApp(bool tested)
        {
            var check = CustomMcpScriptValidator.Validate(CustomMcpTemplate.Source);
            var id = Guid.NewGuid();
            var summary = new CustomMcpServerSummary(id, "Trains", "Train departures.", 1, check.Sha256, null, null,
                "registry-" + id.ToString("N")[..8], [],
                tested ? new CustomMcpTestResult(true, check.Sha256, ["get_departures"], null, null, Now) : null,
                Now, Now);
            return _apps[id] = new CustomMcpServerDetail(summary, CustomMcpTemplate.Source, null, []);
        }

        public async Task<ImprovementProposal> AddGap(CapabilityPayload payload, string status = ImprovementStatuses.Pending)
        {
            var proposal = new ImprovementProposal(Guid.NewGuid(), Owner, ImprovementKinds.Capability,
                CapabilityGapRules.Fingerprint(payload.Slug), CapabilityText.Title(payload), CapabilityText.Evidence(payload),
                0.6, JsonSerializer.Serialize(payload, Json), status, null, Now, Now);
            await Repository.AddAsync(proposal, default);
            return proposal;
        }

        public ImprovementService Service() => new(Repository, Fake<IMemoryService>.Create(),
            new InMemorySkillRepository(), Notifications, Settings, new NullAudit(), new Clock(() => Now), null,
            Fake<ICustomMcpServerStore>.Create(
                ("GetAsync", args => _apps.GetValueOrDefault((Guid)args[1]!)),
                ("ActivateAsync", args =>
                {
                    var app = _apps[(Guid)args[1]!];
                    if (app.Server.DraftSha256 != (string)args[2]!) return null;
                    ActivatedSha = (string)args[2]!;
                    return app.Server with { ActiveSha256 = ActivatedSha, ActiveRevision = app.Server.Revision };
                })),
            Fake<IUserMcpServerRegistry>.Create(("SetEnabledAsync", args =>
            {
                Enabled.Add(((string)args[1]!, (bool)args[2]!));
                return null;
            })));

        public ImprovementAgentTools Tools(string[] toolNames)
        {
            var catalog = new AgentToolCatalog();
            catalog.Set(toolNames);
            return new ImprovementAgentTools(Service(), Repository, Settings,
                Fake<ILearningRecorder>.Create(("RecordSignal", args =>
                {
                    Signals.Add(((string)args[2]!, (string?)args[5]));
                    return null;
                })),
                new OwnerUser(), new AgentBuildContext(Owner, null, Conversation), catalog, new Clock(() => Now));
        }

        public ImprovementMiner Miner() => new(Store, Repository, Service(), new InMemorySkillRepository(),
            Fake<IMemoryService>.Create(), new NoDrafts(), Settings, new Clock(() => Now));

        public CapabilityEvolutionService Evolution() =>
            new(Repository, Builder, Settings, Notifications, new NullAudit(), new Clock(() => Now));
    }

    private sealed class StubStore : ILearningStore
    {
        public List<LearningSignalRecord> Signals { get; } = [];
        public Task AddSignalAsync(LearningSignalRecord signal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddTraceAsync(TurnTraceRecord trace, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<LearningSignalRecord>> ListSignalsAsync(Guid ownerId, DateTimeOffset since,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LearningSignalRecord>>(Signals.ToArray());

        public Task<IReadOnlyList<TurnTraceRecord>> ListTracesAsync(Guid ownerId, DateTimeOffset since, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TurnTraceRecord>>([]);

        public Task<int> PruneAsync(Guid ownerId, DateTimeOffset tracesBefore, DateTimeOffset signalsBefore,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class StubBuilder : ICapabilityBuilder
    {
        public List<string> Built { get; } = [];
        public CapabilityBuildResult Result { get; set; } = new(CapabilityBuildStatuses.Failed);

        public Task<CapabilityBuildResult> BuildAsync(Guid ownerId, CapabilityPayload gap,
            CancellationToken cancellationToken)
        {
            Built.Add(gap.Slug);
            return Task.FromResult(Result);
        }
    }

    private sealed class NoDrafts : ISkillDrafter
    {
        public Task<SkillDraft?> DraftAsync(Guid ownerId, ToolSequenceFinding finding,
            CancellationToken cancellationToken) => Task.FromResult<SkillDraft?>(null);
    }

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}
