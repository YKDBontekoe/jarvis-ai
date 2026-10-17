using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Feature tools the model only sees when a turn needs them. Everything not listed here (clock, reminders, tasks,
/// watches, memory, files, skills, cross-feature search, MCP setup cards, integration tools) is always offered.
/// A group is offered when the user's latest message mentions it, when one of its tools was called in the last few
/// exchanges, or after the model calls <see cref="LoadToolsName"/>. Listed tools stay callable at all times, so a
/// call to one that is not offered yet still runs (with its approval gate) and brings its group in.
/// </summary>
public static partial class ToolGroups
{
    public const string LoadToolsName = "LoadTools";

    /// <summary>Additional property carrying every tool name the model may call, offered or not.</summary>
    internal const string CallableToolsProperty = "jarvis_callable_tools";

    /// <summary>A group stays offered while one of its tools was called within this many recent user messages.</summary>
    internal const int StickyUserMessages = 3;

    public sealed record ToolGroup(string Id, string Summary, IReadOnlyList<string> Tools, Regex Keywords);

    public static IReadOnlyList<ToolGroup> All { get; } =
    [
        new("money", "Expenses, budgets, subscriptions, bank accounts, income, transactions and stock portfolio",
            ["LogExpense", "GetExpenses", "UpdateExpense", "DeleteExpense", "GetFinanceOverview", "GetBudgets",
             "GetSubscriptions", "GetRenewalReview", "SetBudget", "RemoveBudget", "SetSubscriptionStatus", "RemindBeforeCharge",
             "StartSubscriptionNegotiation", "ImportBankStatement", "GetAccounts", "GetTransactions", "GetPortfolio",
             "AddAccount", "LogIncome", "RecordTrade", "SetHoldingPrice"],
            MoneyKeywords()),
        new("inbox", "Email and message inbox triage, snoozing, and commitments the user made or was promised",
            ["CheckInbox", "TriageInboxThread", "SetInboxState", "SnoozeInboxThread", "TrackInboxItem",
             "GetCommitments", "AddCommitment", "AcceptCommitment", "SetCommitmentStatus"],
            InboxKeywords()),
        new("library", "Saved articles and notes, flashcards, clipping a URL, deep research reports",
            ["SearchLibrary", "GetLibraryItem", "SaveToLibrary", "GetLibraryDigest", "GetDueFlashcards",
             "GradeFlashcard", "AddFlashcards", "ClipUrlToLibrary", "StartDeepResearch", "TeachFromLibraryItem",
             "CheckLessonAnswer"],
            LibraryKeywords()),
        new("planner", "Today's day plan: show it, plan the day, add, complete or remove items",
            ["GetTodayPlan", "AddToDayPlan", "PlanMyDay", "CompleteDayPlanItem", "RemoveDayPlanItem"],
            PlannerKeywords()),
        new("people", "Contacts the user keeps up with: details, birthdays, logging contact, relationship radar",
            ["GetPeople", "SavePerson", "LogContact", "RemovePerson", "GetRelationshipRadar", "GetPersonFollowThrough"],
            PeopleKeywords()),
        new("whatsapp", "Read, search and send WhatsApp messages in chats the user turned on",
            ["ListWhatsAppChats", "ReadWhatsAppChat", "SearchWhatsAppMessages", "SendWhatsAppMessage"],
            WhatsAppKeywords()),
        new("habits", "Habit tracking: list habits, check in, create a habit",
            ["GetHabits", "CheckInHabits", "CreateHabit"],
            HabitKeywords()),
        new("journal", "The user's daily journal: write or read entries",
            ["SaveJournalEntry", "ListJournalEntries"],
            JournalKeywords()),
        new("decisions", "Decision journal: log and resolve decisions, see calibration",
            ["LogDecision", "ResolveDecision", "GetDecisions", "GetCalibration"],
            DecisionKeywords()),
        new("projects", "Projects: what each is for, its status and the next action to take",
            ["GetProjects", "SetProjectOutcome", "SetProjectNextAction"],
            ProjectKeywords()),
        new("comparisons", "Weigh options against criteria: create a comparison, record what is known, see the ranking",
            ["CreateComparison", "SetComparisonCell", "GetComparison"],
            ComparisonKeywords()),
        new("opportunities", "What is worth acting on now: meetings to prepare for, promises to follow up, renewals, decisions, free time",
            ["GetOpportunities"],
            OpportunityKeywords()),
        new("stories", "Draft a recap, retrospective or what-changed story from the user's own timeline; stored as an editable draft, nothing is shared",
            ["DraftLifeStory"],
            StoryKeywords()),
        new("setup", "How far setup is: steps done, skipped or open, and what is not connected",
            ["GetSetupStatus"],
            SetupKeywords()),
        new("notification-settings", "Why something did or did not buzz the phone: per-kind choices and the current mode",
            ["ExplainNotifications"],
            NotificationSettingsKeywords()),
        new("documents", "Draft a one-page project update from the user's own records, nothing is sent",
            ["DraftProjectUpdate"],
            DocumentKeywords()),
        new("apps", "Find which app can do a job for the user, what it could read or change, and how to connect it",
            ["FindAppForGoal"],
            AppGoalKeywords()),
        new("scenarios", "What-if arithmetic: compare a cost or a pace now and with an alternative, nothing is changed",
            ["CompareCostScenario", "CompareTimeScenario"],
            ScenarioKeywords()),
        new("goals", "Goals: how they are going, check-ins in the user's words, a small proposed plan for the week",
            ["GetGoals", "AddGoalCheckIn", "ProposeGoalPlan"],
            GoalKeywords()),
        new("meetings", "Meetings: prepare from the user's own records, debrief into proposed decisions and promises, draft a follow-up",
            ["PrepareMeeting", "DebriefMeeting", "DraftMeetingFollowUp"],
            MeetingKeywords()),
        new("timeline", "Life timeline, on this day, life insights, routine and self-improvement suggestions",
            ["QueryTimeline", "OnThisDay", "GetLifeInsights", "GetRoutineSuggestions", "GetImprovementProposals"],
            TimelineKeywords()),
        new("automations", "Rules that run on a schedule or trigger: list, create, preview, templates, enable, run",
            ["ListAutomations", "CreateAutomation", "PreviewAutomation", "ListAutomationTemplates",
             "CreateAutomationFromTemplate", "EnableAutomation", "DisableAutomation", "RunAutomation"],
            AutomationKeywords()),
        new("missions", "Multi-step missions run by a crew of background tasks: plan, run, pause, cancel",
            ["PlanMission", "RunMission", "GetMissions", "PauseOrResumeMission", "CancelMission", "GetMissionResult"],
            MissionKeywords()),
        new("modes", "Assistant modes such as focus or travel: get or set the current mode",
            ["GetCurrentMode", "SetMode"],
            ModeKeywords()),
        new("devices", "The user's connected phones and computers: list, battery, location, clipboard, notify, open URL",
            ["ListDevices", "GetDeviceBattery", "NotifyDevice", "GetDeviceLocation", "ReadDeviceClipboard",
             "OpenUrlOnDevice"],
            DeviceKeywords()),
        new("integrations", "Manage MCP servers and connections: list, search the catalog, install, add, authorize, change tools, remove, read resources and prompts, and write, test and activate self-built apps",
            ["ListMcpServers", "ListHostMcpServers", "ListMcpConnections", "ListMcpServerTools",
             "DiscoverMcpServerTools", "RequestMcpAuthorization", "SearchMcpCatalog", "InstallMcpFromCatalog",
             "AddMcpServer", "AddMcpStdioServer", "UpdateMcpServer", "SetMcpServerEnabled", "SetMcpServerTools",
             "ReadMcpResource", "GetMcpPrompt", "RemoveMcpServer", "GetCustomMcpTemplate", "WriteCustomMcpServer",
             "TestCustomMcpServer", "ActivateCustomMcpServer", "ListCustomMcpServers", "GetCustomMcpServerSource"],
            IntegrationKeywords()),
        new("graph", "Change the knowledge graph: propose or correct facts, merge or forget entities",
            ["ProposeGraphFact", "CorrectGraphFact", "MergeGraphEntities", "ForgetGraphEntity"],
            GraphKeywords()),
        new("agents", "Delegate work to the user's registered remote agents",
            ["ListRemoteAgents", "DelegateToAgent"],
            AgentKeywords()),
        new("coding", "Run a coding task in a configured repository, or propose a fix to Jarvis's own code",
            ["RunCodingTask", "GetRecentJarvisFaults", "ProposeJarvisFix"],
            CodingKeywords()),
    ];

    private static readonly Dictionary<string, ToolGroup> GroupsById =
        All.ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ToolGroup> GroupsByTool = All
        .SelectMany(group => group.Tools.Select(tool => (tool, group)))
        .ToDictionary(pair => pair.tool, pair => pair.group, StringComparer.Ordinal);

    public static ToolGroup? ForTool(string toolName) => GroupsByTool.GetValueOrDefault(toolName);

    public static ToolGroup? Find(string id) => GroupsById.GetValueOrDefault(id.Trim());

    /// <summary>Groups whose keywords appear in the text.</summary>
    public static IEnumerable<ToolGroup> MatchKeywords(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : All.Where(group => group.Keywords.IsMatch(text));

    /// <summary>
    /// Groups the next model call should see: keyword matches in the latest user text, groups whose tools were called
    /// since the <see cref="StickyUserMessages"/>th most recent user message, and groups loaded with LoadTools.
    /// </summary>
    public static HashSet<string> ActiveGroups(IReadOnlyList<ChatMessage> messages)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userMessages = 0;
        var latestUserText = true;
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
            {
                // Only the user's latest words pick groups by keyword; older turns count through the tools they used.
                if (latestUserText)
                    foreach (var group in MatchKeywords(message.Text)) active.Add(group.Id);
                latestUserText = false;
                if (++userMessages >= StickyUserMessages) break;
                continue;
            }

            foreach (var call in message.Contents.OfType<FunctionCallContent>())
            {
                if (call.Name == LoadToolsName)
                {
                    foreach (var id in RequestedGroups(call.Arguments))
                        if (Find(id) is { } requested) active.Add(requested.Id);
                }
                else if (ForTool(call.Name) is { } used) active.Add(used.Id);
            }
        }
        return active;
    }

    internal static IEnumerable<string> RequestedGroups(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || !arguments.TryGetValue("groups", out var value) || value is null) return [];
        return value switch
        {
            string text => SplitGroups(text),
            JsonElement { ValueKind: JsonValueKind.String } element => SplitGroups(element.GetString()),
            JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .SelectMany(item => SplitGroups(item.GetString())),
            IEnumerable<string> items => items.SelectMany(SplitGroups),
            System.Collections.IEnumerable items => items.Cast<object?>().SelectMany(item => SplitGroups(item?.ToString())),
            _ => SplitGroups(value.ToString())
        };
    }

    private static IEnumerable<string> SplitGroups(string? text) =>
        (text ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The meta-tool that brings groups in for the rest of this turn and the next few. Its description is the
    /// catalog, so the model knows what exists without carrying every schema.
    /// </summary>
    public static AIFunction CreateLoadTools(IReadOnlySet<string> availableTools)
    {
        var catalog = string.Join("; ", All
            .Select(group => (group, tools: group.Tools.Where(availableTools.Contains).ToArray()))
            .Where(item => item.tools.Length > 0)
            .Select(item => $"{item.group.Id} ({item.group.Summary}: {string.Join(", ", item.tools)})"));
        var description =
            "Loads more Jarvis functions so their full input schemas are available from your next step on. " +
            "Call it before using a function that is not in your list yet, then call that function. " +
            "Pass one or more group ids. Groups: " + catalog + ".";
        return AIFunctionFactory.Create(
            ([System.ComponentModel.Description("Group ids to load, for example [\"money\"] or [\"inbox\", \"people\"].")]
                string[] groups) =>
            {
                var loaded = new List<string>();
                var unknown = new List<string>();
                foreach (var id in groups.SelectMany(SplitGroups).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (Find(id) is { } group)
                        loaded.Add($"{group.Id}: {string.Join(", ", group.Tools.Where(availableTools.Contains))}");
                    else
                        unknown.Add(id);
                }
                var result = loaded.Count == 0
                    ? "No groups loaded."
                    : "Loaded. These functions are now in your list: " + string.Join("; ", loaded) + ".";
                if (unknown.Count > 0)
                    result += " Unknown groups: " + string.Join(", ", unknown) + ". Valid groups: " +
                              string.Join(", ", All.Select(group => group.Id)) + ".";
                return result;
            },
            new AIFunctionFactoryOptions { Name = LoadToolsName, Description = description });
    }

    // Keywords are English and Dutch, matched case-insensitively at word starts so plurals and verb forms count.
    private const RegexOptions KeywordOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\b(expens|spen[dt]|spending|paid|pay|cost|budget|subscription|bank|account balance|money|income|salary|transaction|portfolio|stock|shares?\b|etf|invest|net worth|afford|receipt|bill|charge|refund|euro|dollar|uitga|uitgegeven|betaald|betalen|kost|abonnement|geld|inkomen|salaris|aandel|beleg|bon\b|bonnetje|rekening|saldo)|[€$£]", KeywordOptions)]
    private static partial Regex MoneyKeywords();

    [GeneratedRegex(@"\b(inbox|e-?mail|mail|thread|unread|triage|commitment|promis|follow[- ]?up|owe|reply to|respond to|beloofd|belofte|toezegging|ongelezen)", KeywordOptions)]
    private static partial Regex InboxKeywords();

    [GeneratedRegex(@"\b(library|article|read later|saved? (this|it|link)|clip|bookmark|flashcard|study|quiz me|teach me|lesson|explain this article|leer me|les over|deep research|research report|digest|bibliotheek|artikel|opslaan|onderzoek)", KeywordOptions)]
    private static partial Regex LibraryKeywords();

    [GeneratedRegex(@"\b(plan (my|the|for)|day plan|today'?s plan|my day|schedule (my|the) day|agenda|to-?do|planning|dagplan|mijn dag|vandaag plannen)", KeywordOptions)]
    private static partial Regex PlannerKeywords();

    [GeneratedRegex(@"\b(contact|friend|birthday|person|people|relationship|radar|keep in touch|catch up with|haven'?t (talked|spoken)|called|met with|vriend|verjaardag|persoon|mensen|contact|gesproken|gebeld)", KeywordOptions)]
    private static partial Regex PeopleKeywords();

    [GeneratedRegex(@"\b(whats\s?app|wa\b|group chat|message (him|her|them)|text (him|her|them)|appje|appen|bericht)", KeywordOptions)]
    private static partial Regex WhatsAppKeywords();

    [GeneratedRegex(@"\b(habit|streak|check[- ]?in|gewoonte|reeks)", KeywordOptions)]
    private static partial Regex HabitKeywords();

    [GeneratedRegex(@"\b(journal|diary|dagboek|journaal)", KeywordOptions)]
    private static partial Regex JournalKeywords();

    [GeneratedRegex(@"\b(decision|decide|decided|calibration|prediction|beslis|besluit|beslissing|voorspelling)", KeywordOptions)]
    private static partial Regex DecisionKeywords();

    [GeneratedRegex(@"\b(project|next (step|action)|outcome|goal of|project(en)?|volgende (stap|actie)|doel van)", KeywordOptions)]
    private static partial Regex ProjectKeywords();

    [GeneratedRegex(@"\b(compare|comparison|which (one|should)|pros and cons|versus|\bvs\b|choose between|shortlist|vergelijk|vergelijking|welke (moet|zal)|kiezen tussen)", KeywordOptions)]
    private static partial Regex ComparisonKeywords();

    [GeneratedRegex(@"\b(what should i do (next|now)|what('?s| is) coming up|anything (i )?(should|need to) (do|know)|what needs my attention|what am i missing|wat moet ik (nu )?doen|wat komt eraan|wat heeft aandacht nodig)", KeywordOptions)]
    private static partial Regex OpportunityKeywords();

    [GeneratedRegex(@"\b(recap|retrospective|year in review|month in review|what changed (this|last|over)|my story|life story|terugblik|jaaroverzicht|maandoverzicht|wat is er veranderd)", KeywordOptions)]
    private static partial Regex StoryKeywords();

    [GeneratedRegex(@"\b(set ?up|getting started|what('?s| is) (connected|not working)|is (my )?\w+ connected|what (is|isn't) set up|first week|instellen|wat is er ingesteld|is .{1,20} gekoppeld)", KeywordOptions)]
    private static partial Regex SetupKeywords();

    [GeneratedRegex(@"\b(why (didn'?t|did) i (get|receive)|what will (buzz|ping|notify)|notification settings|which notifications|do not disturb|waarom kreeg ik|welke meldingen)", KeywordOptions)]
    private static partial Regex NotificationSettingsKeywords();

    [GeneratedRegex(@"\b(project update|status update|progress report|write (up )?(an? )?update|statusupdate|voortgangsrapport|schrijf (een )?update)", KeywordOptions)]
    private static partial Regex DocumentKeywords();

    [GeneratedRegex(@"\b(connect (my|an?|the)|which app|what app|integrate|integration|hook up|can you (read|access) my (calendar|e-?mail|contacts)|koppel|verbind|welke app)", KeywordOptions)]
    private static partial Regex AppGoalKeywords();

    [GeneratedRegex(@"\b(what if|what[- ]if|scenario|break[- ]even|how long would|how many weeks|would i save|save if|switch(ing)? to|wat als|scenario|hoe lang zou|zou ik besparen|overstappen)", KeywordOptions)]
    private static partial Regex ScenarioKeywords();

    [GeneratedRegex(@"\b(goals?|check[- ]?in on|making progress|my progress|experiment|doelen?|vooruitgang|voortgang|experiment)", KeywordOptions)]
    private static partial Regex GoalKeywords();

    [GeneratedRegex(@"\b(meeting|agenda|debrief|prep(are)? (me )?for|follow[- ]?up|standup|stand-up|1[:-]1|one[- ]on[- ]one|vergadering|overleg|bespreking|afspraak met|voorbereiden)", KeywordOptions)]
    private static partial Regex MeetingKeywords();

    [GeneratedRegex(@"\b(timeline|on this day|this day last|years? ago|insight|routine|pattern|improvement|tijdlijn|vandaag \d+ jaar|jaar geleden|inzicht|patroon|verbeter)", KeywordOptions)]
    private static partial Regex TimelineKeywords();

    [GeneratedRegex(@"\b(automation|automate|every (day|morning|evening|week|monday|tuesday|wednesday|thursday|friday|saturday|sunday)|whenever|rule|template|automatisch|automatiseer|elke (dag|ochtend|avond|week)|regel)", KeywordOptions)]
    private static partial Regex AutomationKeywords();

    [GeneratedRegex(@"\b(mission|crew|missie)", KeywordOptions)]
    private static partial Regex MissionKeywords();

    [GeneratedRegex(@"\b(mode|focus|travel mode|do not disturb|modus|focussen)", KeywordOptions)]
    private static partial Regex ModeKeywords();

    [GeneratedRegex(@"\b(device|phone|laptop|computer|mac|battery|clipboard|where am i|my location|on my (phone|mac|laptop)|notify me on|apparaat|telefoon|batterij|klembord|waar ben ik)", KeywordOptions)]
    private static partial Regex DeviceKeywords();

    [GeneratedRegex(@"\b(mcp|integration|connect|connector|install|plugin|server|authori[sz]e|sign in to|log in to|oauth|api key|token|notion|github|calendar|gmail|slack|linear|home assistant|build (an?|me an?|your own) (app|tool|integration)|write (an?|your own) (app|tool|integration|mcp)|koppel|verbind|integratie|installeer)", KeywordOptions)]
    private static partial Regex IntegrationKeywords();

    [GeneratedRegex(@"\b(knowledge graph|graph|entity|entities|merge|that'?s wrong|not true|incorrect|correct (that|this)|kennisgraaf|klopt niet|onjuist)", KeywordOptions)]
    private static partial Regex GraphKeywords();

    [GeneratedRegex(@"\b(remote agent|delegate|other agent|a2a|agent)", KeywordOptions)]
    private static partial Regex AgentKeywords();

    [GeneratedRegex(@"\b(code|coding|repo|repository|pull request|\bpr\b|bug|fix (jarvis|yourself)|refactor|commit|branch)", KeywordOptions)]
    private static partial Regex CodingKeywords();
}
