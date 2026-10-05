# Timeline, inbox, finance, studio, library, modes, missions

Seven owner-scoped features built on the existing domains. Each follows the usual layering (Domain → Application → Infrastructure; agent tools call Application services) and each has an agent tool set, an HTTP group under `/api/v1`, and a Flutter screen. Registrations live in `Jarvis.Infrastructure/LifeFeaturesRegistration.cs` and `Jarvis.Agents/DependencyInjection.cs`.

| Feature | Code | Storage | Flutter |
|---------|------|---------|---------|
| [Life timeline](#life-timeline) | `Application/Timeline` | none (read-time projection) | `features/timeline` |
| [Inbox and commitments](#inbox-and-commitments) | `Application/Inbox`, `Agents/Inbox` | `inbox_threads`, `commitments` | `features/inbox` |
| [Finance autopilot](#finance-autopilot) | `Application/Finance`, `Agents/Finance` | `budgets`, `subscriptions` | `features/finance` |
| [Automation studio](#automation-studio) | `Application/Automations/AutomationEvents.cs` and friends | `automation_webhooks`, `automation_runs.event_json` | `features/automations` |
| [Library and deep research](#library-and-deep-research) | `Application/Library`, `Agents/Library` | `library_items`, `flashcards` | `features/library` |
| [Context modes](#context-modes) | `Application/Modes`, `Agents/Modes` | owner settings (`context-modes`) | `features/modes` |
| [Mission control](#mission-control) | `Application/Missions`, `Agents/Missions` | `missions`, `mission_steps`, `mission_notes` | `features/missions` |
| [Routine miner](#routine-miner) | `Application/Routines`, `Agents/Routines` | `routine_suggestions`, owner settings (`routines`) | `features/automations` |

## Life timeline

One chronological list of what happened: journal entries, expenses, habit check-ins, people (last contact, birthdays), finished tasks, delivered reminders, learned memories, and chats. Each area is an `ITimelineSource`; `TimelineService` merges them newest first. Sources run one after another because they share a database context, and one failing source is reported in `failedKinds` without blanking the rest. Journal-mirrored memories are skipped.

- `GET /timeline?from=&to=&kinds=&q=&limit=` (default last 30 days, at most 400 days, 500 moments)
- `GET /timeline/on-this-day?years=` (earlier years on today's month and day)
- `GET /timeline/insights?days=` (14 to 180). `TimelineInsightEngine` is plain arithmetic: Pearson correlations between mood, energy, stress, habit follow-through, spending and finished tasks (at least 7 paired days and |r| ≥ 0.4), 14-day mood/energy/stress trends, weekday spending and mood patterns, best day. Findings are phrased as patterns, never causes.
- Agent tools: `QueryTimeline`, `OnThisDay`, `GetLifeInsights` (read-only; work in background tasks).

## Inbox and commitments

The inbox follows chats the owner reads along with (WhatsApp) and mail threads the agent tracks with `TrackInboxItem`. `POST /inbox/sync` (also `GET /inbox?sync=true`) turns new messages into threads with rule-based triage (`InboxHeuristics`, Dutch and English cues): needs reply, waiting, for information, done. New messages reopen a finished or snoozed thread and clear its summary and draft. A snooze that ran out brings the thread back.

- Model triage (`POST /inbox/{id}/triage`, tool `TriageInboxThread`) adds a one-line summary, a draft reply, priority, and suggested commitments. It has no tools; chat text is fenced as data. Drafts are never sent: sending stays with the approval-gated WhatsApp send tool.
- Commitments (`i_owe` / `owed_to_me`) have a due date and a status. A dated, accepted commitment creates a reminder at 09:00 local on that day through `IReminderService`; completing or dropping it cancels the reminder. Commitments found by triage stay **suggested** until accepted (`PATCH /commitments/{id}` with `status: "accepted"`), so text in a message cannot create reminders by itself.
- Endpoints: `GET /inbox`, `POST /inbox/sync`, `PUT /inbox/{id}/state`, `POST /inbox/{id}/snooze`, `POST /inbox/{id}/triage`, `DELETE /inbox/{id}`, `GET/POST /commitments`, `PATCH/DELETE /commitments/{id}`.
- Tools: `CheckInbox`, `TriageInboxThread`, `SetInboxState`, `SnoozeInboxThread`, `TrackInboxItem`, `GetCommitments`, `AddCommitment`, `AcceptCommitment`, `SetCommitmentStatus`.

## Finance autopilot

Extends expenses (`expenses.source` also allows `import`).

- **Budgets** per category or `total`; `BudgetExpenseObserver` sends a `budget.alert` notification once at 80% and once at 100% per month (only for this month's expenses, never while importing old statements). Status adds a month-end projection from day 5.
- **Subscriptions** are detected from 14 months of spending (`SubscriptionDetector`): weekly, monthly, quarterly or yearly rhythm, a steady amount (±25%), not groceries, dining, transport or shopping. Price changes are remembered, vanished ones become `cancelled`, the owner can dismiss a detection and ask for a reminder 0 to 14 days before the next charge.
- **Forecast**: spent so far + recurring charges still due this month + the daily pace of other spending (last month's pace in the first days).
- **Alerts**: unusually high amounts against a shop's or category's history, price increases, budget warnings.
- **Import**: `POST /finance/import` with the CSV text and `commit: false|true`. English and Dutch headers, `,` `;` or tab, signed amounts, debit/credit columns, ING-style Af/Bij. Only money going out becomes spending; repeats are skipped; at most 1 MB and 2,000 rows. **Export**: `GET /finance/export?from=&to=&category=` as CSV with spreadsheet-formula defusing.
- Endpoints: `GET /finance/overview`, `PUT /finance/budgets`, `DELETE /finance/budgets/{id}`, `PATCH /finance/subscriptions/{id}`, `POST /finance/import`, `GET /finance/export`.
- Tools: `GetFinanceOverview`, `GetBudgets`, `SetBudget`, `RemoveBudget`, `GetSubscriptions`, `SetSubscriptionStatus`, `RemindBeforeCharge`, `ImportBankStatement` (preview first).

## Automation studio

Adds an `event` trigger and tooling to [owner automations](../automations.md).

- **Event triggers**: `{ "kind": "event", "eventKind": "…", "contains": "…", "source": "…" }`. Kinds: `webhook`, `message_received` (WhatsApp read-along), `file_uploaded`, `task_completed`, `journal_saved`, `expense_logged`, `inbox_needs_reply`. Producers call `IAutomationEventBus.PublishAsync` (via `TryPublishAsync`, which never throws). The bus starts one run per matching enabled rule; the event fingerprint is the idempotency key, so redelivery starts nothing twice, and each rule's cooldown prevents loops. Statement imports do not publish expense events.
- The event travels with the run (`automation_runs.event_json`). Action text may use `{{event.title}}`, `{{event.detail}}`, `{{event.source}}`, `{{event.kind}}`; inside task and agent prompts the values are fenced with « » and followed by a note that they are data.
- **Branching**: any action may carry `"if": { "field": "event.detail", "op": "contains|not_contains|equals|not_equals", "value": "…" }`. A skipped branch costs no action and asks no approval.
- **Set mode**: new action `set_mode` (`mode`, optional `minutes`) switches [context modes](#context-modes); no approval.
- **Loop guard**: an automation triggered by `task_completed` may not contain `task` or `agent_run` actions.
- **Simulator**: `POST /automations/simulate` (definition + sample event) and `POST /automations/{id}/simulate` return what would run, what is skipped and why, the rendered texts, and which steps need approval. Nothing is saved or sent.
- **Templates**: `GET /automations/templates`, `POST /automations/templates/{id}/create` (a draft; switched off).
- **Webhooks**: `GET/POST /automations/webhooks`, `DELETE /automations/webhooks/{id}`. The URL token (`jwh_…`) is shown once and only its SHA-256 hash is stored. `POST /api/v1/hooks/{token}` is public (rate limited, body ≤ 16 KB) and answers 404 for unknown tokens. JSON `title`/`subject`/`message` becomes the event title, `description`/`body`/`text` the detail.
- Tools: `PreviewAutomation`, `ListAutomationTemplates`, `CreateAutomationFromTemplate` (plus the existing automation tools).

## Library and deep research

- **Clip** public HTTPS pages (`POST /library/clip`, tool `ClipUrlToLibrary`, approval-gated for the agent). `PublicWebPageFetcher` connects only to public addresses, re-validates every redirect (up to 3), reads text and HTML only, at most 2 MB. `HtmlTextExtractor` keeps article text. A model digest (`ILibraryDigester`) adds a summary, key points, tags and flashcards; without a model the opening sentences are used. Pages are saved once per normalized URL.
- **Notes and reports** (`POST /library/notes`, tool `SaveToLibrary`) share the library; `GET /library?q=&tag=&kind=` searches title, summary, tags and text (list results omit the full text).
- **Flashcards** use SM-2 (`Sm2`): `GET /library/cards/due`, `POST /library/cards/{id}/review` (`button` again/hard/good/easy or `quality` 0–5). Tools `GetDueFlashcards`, `GradeFlashcard`, `AddFlashcards` support quizzing in chat.
- **Deep research**: `POST /library/research` or tool `StartDeepResearch` creates a durable task with a research protocol (sub-questions, N sources, cited report, `SaveToLibrary kind=report`). Depth `quick|standard|deep` = 3/6/10 sources.
- `GET /library/digest?days=` and tool `GetLibraryDigest` summarize what was saved and what is due.
- Saved page text is always returned to the model marked as untrusted. There is no OS share-sheet extension yet; add links in the app or ask Jarvis.

## Context modes

Modes: `normal`, `focus`, `commuting`, `meeting`, `sleep`, `travel`, `weekend`. The owner can switch one by hand (optionally for a number of minutes) or let Jarvis decide (`ModeInference`): manual choice first, then sleeping hours (default 23:00–07:00), a calendar event in progress (via `ICalendarFeed`, cached 2 minutes), the weekend, otherwise normal.

- A mode's policy: `notifications` (`all`, `important` = reminders, approvals, watches, `none`) and a tone hint added to the chat context. Owners can override each mode's policy.
- `NotificationPushWorker` asks `IModeService.ShouldPushAsync` before sending; a held push is marked delivered and the notification stays in the app. If the mode state cannot be read, pushes go out.
- Stored in owner settings (section `context-modes`), no tables.
- Endpoints: `GET /modes`, `PUT /modes/active` (`mode`, `minutes`; `auto` clears), `PUT /modes/settings`, `PUT/DELETE /modes/{mode}/policy`. Tools: `GetCurrentMode`, `SetMode`. The Flutter **ambient display** (Modes → Ambient display) shows the time, mode and what is next for a desk or tablet.

## Mission control

A mission splits a goal into up to 8 steps with roles (`researcher`, `planner`, `browser`, `coder`, `finance`, `writer`, `generalist`) and dependencies on **earlier** steps only, so it is always a DAG. `IMissionPlanner` (reasoning model) plans; bad output becomes a one-step mission. A new mission waits in `ready` until the owner starts it.

- `MissionService.AdvanceAsync` is the supervisor pass: collect finished tasks, cancel steps blocked by a failure, claim and start steps whose dependencies are done (at most 3 in parallel, claim is a conditional update so two workers never start one step), close the mission and notify. The worker runs it every 10 seconds (`workers/Jarvis.Worker/MissionSupervisor.cs`); state is in PostgreSQL, so nothing is lost on restart.
- Each step is an ordinary background task, so approvals, usage and the task list work unchanged. Earlier results are passed in fenced as untrusted data.
- **Blackboard**: crew tasks get `PostToBlackboard` / `ReadBlackboard`; notes appear in later prompts. Crew tasks cannot plan, start or cancel missions.
- Owner control: pause/resume, cancel (stops running tasks), edit a waiting step, skip a step (dependents carry on), retry a failed step (blocked steps come back).
- Endpoints: `GET/POST /missions`, `GET/DELETE /missions/{id}`, `POST /missions/{id}/start|pause|resume|cancel`, `PUT /missions/steps/{id}`, `POST /missions/steps/{id}/skip|retry`. At most 5 active missions per owner.
- Tools: `PlanMission`, `RunMission` (approval), `GetMissions`, `PauseOrResumeMission`, `CancelMission`.

## Routine miner

Jarvis looks for repeated behaviour in the life timeline and offers a ready automation for each pattern. `RoutineMinerEngine` is pure: it reads 8 weeks of timeline moments (journal, expense, habit, finished task) in the owner's time zone and finds two kinds of pattern.

- **Time habits**: the same thing done inside a ±45 minute stretch of the day on a weekday set. A weekday counts when it has at least 3 hits and at least 60% of that weekday's days in the window. The suggestion is a `schedule` trigger at the median time with a notification (or, for a recurring finished task, a task action).
- **Follow-ups**: event A (journal saved, expense logged, task finished) followed by the same thing B within 2 hours in at least 70% of at least 4 occurrences. The suggestion is an `event` trigger with a notification and a 4-hour cooldown. A task-finished trigger never gets a task action, so an automation can not start itself.
- Every suggestion carries a full automation definition that passes `AutomationRuleValidator`. At most 6 are kept, strongest first.

`RoutineSuggestionService` stores them in `routine_suggestions` (unique per owner and `fingerprint`). A refresh runs after the nightly dream, and also when the list is read and the last run is over 24 hours old, so it works with dreaming off. Pending rows follow the data and are dropped when the pattern disappears; accepted and dismissed rows are the owner's decision and are never changed, so a dismissed pattern does not come back. At most one `routine.suggested` notification is sent per week.

- Accepting runs the definition through the validator and creates the automation as a **draft**; the owner still switches it on in the Automations screen. Each suggestion also returns the `AutomationSimulator` result (when, then, approvals) so the owner sees what it would do.
- Endpoints: `GET /routines/suggestions`, `POST /routines/suggestions/refresh`, `POST /routines/suggestions/{id}/accept`, `POST /routines/suggestions/{id}/dismiss`. Tool: `GetRoutineSuggestions` (read-only; accepting stays in the app). Flutter shows them as "Suggested for you" at the top of Automations.

## Safety summary

- All data is owner-scoped; audit events carry ids and counts, never message text, amounts, or page content.
- Text from other people, web pages, files and other agents is treated as untrusted in prompts and tool output.
- New outbound or spending actions stay behind approvals: opening a web page for the library, starting a mission, agent-run and message actions in automations, sending WhatsApp messages.
