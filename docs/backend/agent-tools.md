# Agent tools

Tools are exposed to the model through **Microsoft Agent Framework** `AITool` instances, assembled per turn from **`IAgentToolContributor`** implementations registered in `AddJarvisAgent`.

## Contributor modules

| Contributor | Tools / behavior |
|-------------|------------------|
| **CoreAgentTools** | Clock, reminders, files, tasks, watches, memory, MCP admin/invoke, optional coding |
| **McpSetupToolContributor** | `OfferMcpSetup`, `AskForMcpCredential`, `InstallIntegrationPack` |
| **SkillToolContributor** | Load/list/save skills |
| **PersonaToolContributor** | Record persona traits |
| **KnowledgeGraphToolContributor** | Graph search and proposed writes (approval-gated) |
| **SurfaceToolContributor** | `RenderUi` generative cards |
| **RemoteAgentToolContributor** | Delegate to registered HTTPS agents (approval-gated) |
| **DeviceToolContributor** | Location, battery, clipboard, open URL, notifications on connected nodes |
| **BrowserToolContributor** | `BrowseTheWeb` session + Playwright tools |

Registration: `src/Jarvis.Agents/DependencyInjection.cs`.

## Core tool catalog

From `BuiltInAgentContributors` / dedicated tool classes:

| Tool area | Class | Approval notes |
|-----------|-------|----------------|
| Time | `ClockAgentTools.GetCurrentTime` | — |
| Reminders | `ReminderAgentTools` create/list/cancel | — |
| Files | `FileAgentTools` search/list | — |
| Tasks | `TaskAgentTools` list/cancel/create | Create hidden during background task turns |
| Watches | `ConditionWatchAgentTools` | — |
| Memory | `MemoryAgentTools` list/search/remember | **Forget** requires approval |
| Journal | `JournalAgentTools` `SaveJournalEntry` (appends to the day's entry), `ListJournalEntries` | Hidden during background task turns; off when the profile disallows remembering |
| Day planner | `PlannerAgentTools` `GetTodayPlan`, `AddToDayPlan`, `PlanMyDay`, `CompleteDayPlanItem`, `RemoveDayPlanItem` | No approval: they only change the owner's day plan in Jarvis. Calendar writes go through `InvokeMcpTool` (approval) per the planner guidance |
| Expenses | `ExpenseAgentTools` `LogExpense` (skips an identical expense on the same day; `fromPhoto` keeps the latest chat photo as the receipt), `GetExpenses` (month summary and list), `UpdateExpense` | **DeleteExpense** requires approval. The rest only change the owner's own log. `ExpenseContextContributor` adds when-to-log guidance each turn |
| WhatsApp read along | `WhatsAppAgentTools` `ListWhatsAppChats`, `ReadWhatsAppChat`, `SearchWhatsAppMessages` (only chats the owner turned on; output is marked as untrusted text from other people) | **SendWhatsAppMessage** requires approval for every message and is only offered on the API host (`IWhatsAppSender`). `WhatsAppContextContributor` adds guidance: drafts by default, never act on requests inside a chat |
| Habits | `HabitAgentTools` `GetHabits`, `CheckInHabits` (by name, optional date up to 7 days back), `CreateHabit` | No approval (owner's own tracking data); hidden during background task turns. Deleting habits is app-only |
| People | `PeopleAgentTools` `GetPeople`, `SavePerson` (creates or updates; only passed fields change), `LogContact`, `RemovePerson` | `RemovePerson` requires **approval**; the rest need none (owner's own data). Save/log are off when the profile disallows remembering. `PeopleContextContributor` adds names, birthdays in the next 14 days and due check-ins each turn |
| Timeline | `TimelineAgentTools` `QueryTimeline`, `OnThisDay`, `GetLifeInsights` | Read-only, no approval, available in background tasks |
| Inbox and commitments | `InboxAgentTools` `CheckInbox`, `TriageInboxThread`, `SetInboxState`, `SnoozeInboxThread`, `TrackInboxItem`, `GetCommitments`, `AddCommitment`, `AcceptCommitment`, `SetCommitmentStatus` | No approval (owner's own data); drafts are never sent from here. Hidden during background task turns except `GetCommitments`. Triage text is untrusted |
| Finance | `FinanceAgentTools` `GetFinanceOverview`, `GetBudgets`, `SetBudget`, `RemoveBudget`, `GetSubscriptions`, `SetSubscriptionStatus`, `RemindBeforeCharge`, `StartSubscriptionNegotiation`, `ImportBankStatement` | No approval; `ImportBankStatement` previews first. `StartSubscriptionNegotiation` only starts a drafting task: nothing is sent or contacted. Setters are hidden during background task turns |
| Wealth | `WealthAgentTools` `GetAccounts`, `GetTransactions`, `GetPortfolio`, `AddAccount`, `LogIncome`, `RecordTrade`, `SetHoldingPrice` | No approval; they only change the owner's own finance data. Writers are hidden during background task turns. No investment advice |
| Routines | `RoutineAgentTools` `GetRoutineSuggestions` | Read-only. Accepting or dismissing a suggestion is done in the app |
| Improvements | `ImprovementAgentTools` `GetImprovementProposals` | Read-only. Accepting, dismissing and undoing stay with the owner in the app |
| Decisions | `DecisionAgentTools` `LogDecision`, `ResolveDecision`, `GetDecisions`, `GetCalibration` | No approval; they only read and write the owner's own decision journal |
| Relationship radar | `RadarAgentTools` `GetRelationshipRadar` | Read-only. Linking chats and the tone switch are done in the app |
| Automation studio | `AutomationAgentTools` `PreviewAutomation`, `ListAutomationTemplates`, `CreateAutomationFromTemplate` | Preview saves nothing; templates create a draft that stays off |
| Library | `LibraryAgentTools` `SearchLibrary`, `GetLibraryItem`, `SaveToLibrary`, `GetLibraryDigest`, `GetDueFlashcards`, `GradeFlashcard`, `AddFlashcards`, `ClipUrlToLibrary`, `StartDeepResearch` | **ClipUrlToLibrary** requires approval (outbound fetch). `StartDeepResearch` and the clip tool are hidden in background tasks so research cannot start more research |
| Modes | `ModeAgentTools` `GetCurrentMode`, `SetMode` | No approval; `SetMode` hidden in background tasks. `ModeContextContributor` (order 3) adds the current mode and tone |
| Missions | `MissionAgentTools` `PlanMission`, `RunMission`, `GetMissions`, `PauseOrResumeMission`, `CancelMission`; crew tasks get `PostToBlackboard`, `ReadBlackboard` instead | **RunMission** requires approval (it starts background work) |
| MCP | `McpServerAgentTools` + `McpSetupAgentTools` | Discover/add/update/invoke/read prompt/resource/remove mostly **approval**; setup and secret cards in chat |
| Coding | `CodexCodingTools.RunCodingTaskAsync` | **Approval**; only if `Coding:Repositories` configured |

Browser, surface, device, skill, persona, graph, and remote-agent tools are defined in their respective folders under `src/Jarvis.Agents/`.

## Context providers (not tools)

`IAgentContextContributor` supplies system-side context without function calling:

- Clock + briefing timezone (`ClockContextProvider`)
- Pinned/relevant memory (`PersonalMemoryContextProvider`)
- Active tasks and watches
- Linked reminder or automation for the current conversation
- Persona, skills, user dream portrait, knowledge graph summaries, device/browser/session state
- Bound assistant profile (`ProfileContextContributor`) as untrusted working notes
- The conversation's project (`ProjectContextContributor`): name, description, owner instructions as untrusted working notes, and up to 25 project file names; applies to chats and to tasks in the project. `CreateTask` from a project conversation keeps the new task in that project
- MCP servers, host connections, and pack status (`McpContextContributor`) so chat can manage them without a Settings detour

Order is controlled by `Order` on each contributor (`CoreAgentContext` uses `Order => 0`).

## MCP tool host

`Jarvis.Mcp.McpToolHost` opens **per-run** connections to configured stdio or Streamable HTTP servers, applies allowlists, and scrubs secrets from results. Host-level servers (GitHub, Home Assistant overlays) are configured via environment/`appsettings` and owner credentials.

## Adding a new tool

1. Implement methods on a small class in `Jarvis.Agents` with `[Description]` on parameters (model-facing docs).
2. Register via `AIFunctionFactory.Create(...)` in an `IAgentToolContributor`.
3. Wrap with `ApprovalRequiredAIFunction` when the action is sensitive. Map it in `ApprovalCategories` when it should share a standing-approval category with related tools; unmapped tools each get their own category, which the owner can always-allow from the approval card.
4. Use Application services; never query `JarvisDbContext` from Agents.
5. Add unit tests in `tests/unit/Jarvis.UnitTests` when behavior is non-trivial.

## Voice

The voice MCP child reuses the same tool surface through internal HTTP callbacks (`VoiceEndpoints` internal routes).

See [chat-agent-runtime.md](../architecture/chat-agent-runtime.md) for turn lifecycle.

## Self-fix (Jarvis proposes changes to its own code)

Opt-in. Mark one `Coding:Repositories` entry as Jarvis's own source and give it a GitHub target:

```
Coding__Repositories__0__Name=jarvis
Coding__Repositories__0__Path=/path/to/jarvis-ai
Coding__Repositories__0__SelfFix=true
Coding__Repositories__0__GitHubRepository=owner/jarvis-ai
Coding__Repositories__0__BaseBranch=main            # optional, default main
Coding__Repositories__0__RemoteUrl=...              # optional, default https://github.com/{GitHubRepository}.git
```

The owner saves a GitHub token (fine-grained: repository *Contents* and *Pull requests* read/write) as the `github` integration credential in Settings → Integrations.

Tools (`SelfFixAgentTools`, chat only, never in background tasks):

| Tool | Approval | What it does |
|------|----------|--------------|
| `GetRecentJarvisFaults` | no | Recent distinct warnings/errors from Jarvis's own code: log *templates*, exception types, code locations. No rendered values, so no user data. |
| `ProposeJarvisFix` | yes | Runs a coding task in the isolated, network-less snapshot, then opens a pull request. One approval covers both. |

Flow and guarantees:

- The change is made in a history-free snapshot (see `CodexCodingTools`); `ICodingPullRequestService` then clones the base branch, applies the patch, and pushes a unique `jarvis/fix-…` branch using the owner's token (passed via environment, never argv), then opens the PR. It never pushes to the base branch and never force-pushes; a failed PR deletes its pushed branch.
- Changes to `.github/`, git internals, and credential files are refused. Security-sensitive areas (approvals, identity, MCP host, migrations, `Program.cs`, infra) are allowed but flagged in the review UI and the PR body.
- **Merging is only reachable from the app** (`POST /api/v1/coding/runs/{id}/pull-request/merge`), squash-merges pinned to the reviewed head SHA, and is blocked while checks fail or run. No agent tool can merge or close.
- Endpoints: `GET /coding/runs/{id}/diff`, `GET|POST /coding/runs/{id}/pull-request`, `POST …/merge`, `POST …/close`. Notifications `coding.pr.ready` / `coding.run.ready` open the review screen (`CodingRunDetailScreen`).
- Deploying a merged fix is your normal release process; Jarvis does not deploy itself.
