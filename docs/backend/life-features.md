# Timeline, inbox, finance, studio, library, modes, missions

Owner-scoped features built on the existing domains. Each follows the usual layering (Domain → Application → Infrastructure; agent tools call Application services) and each has an agent tool set, an HTTP group under `/api/v1`, and a Flutter screen. Registrations live in `Jarvis.Infrastructure/LifeFeaturesRegistration.cs` and `Jarvis.Agents/DependencyInjection.cs`.

| Feature | Code | Storage | Flutter |
|---------|------|---------|---------|
| [Life timeline](#life-timeline) | `Application/Timeline` | none (read-time projection) | `features/timeline` |
| [Inbox and commitments](#inbox-and-commitments) | `Application/Inbox`, `Agents/Inbox` | `inbox_threads`, `commitments` | `features/inbox` |
| [Finance autopilot](#finance-autopilot) | `Application/Finance`, `Agents/Finance` | `budgets`, `subscriptions` | `features/finance` |
| [Automation studio](#automation-studio) | `Application/Automations/AutomationEvents.cs` and friends | `automation_webhooks`, `automation_runs.event_json` | `features/automations` |
| [Library and deep research](#library-and-deep-research) | `Application/Library`, `Agents/Library` | `library_items`, `flashcards` | `features/library` |
| [Context modes](#context-modes) | `Application/Modes`, `Agents/Modes` | owner settings (`context-modes`) | `features/modes` |
| [Mission control](#mission-control) | `Application/Missions`, `Agents/Missions` | `missions`, `mission_steps`, `mission_notes` | `features/missions` |
| [Relationship radar](#relationship-radar) | `Application/People/Radar`, `Agents/People` | `person_channel_links` | `features/people` |
| [Decision journal](#decision-journal) | `Application/Decisions`, `Agents/Decisions` | `decisions` | `features/decisions` |
| [Routine miner](#routine-miner) | `Application/Routines`, `Agents/Routines` | `routine_suggestions`, owner settings (`routines`) | `features/automations` |

## Life timeline

One chronological list of what happened: journal entries, expenses, habit check-ins, people (last contact, birthdays), finished tasks, delivered reminders, learned memories, chats, and decisions (when logged and when answered). Each area is an `ITimelineSource`; `TimelineService` merges them newest first. Sources run one after another because they share a database context, and one failing source is reported in `failedKinds` without blanking the rest. Journal-mirrored memories are skipped.

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
- **Cancel or negotiate** (`ISubscriptionNegotiationService`): for a subscription the owner still pays for, Jarvis can help cancel it or ask for a lower price, in one of two modes.
  - `draft`: a background task (`IJarvisTaskService`) writes the message; nothing is sent, nobody is contacted and the subscription is not marked cancelled. The subscription remembers the task (`negotiation_task_id`, `negotiation_goal`, `negotiation_started_at`; deleting the task only clears the link) so the card can offer "Open task".
  - `browser`: only returns a prompt for the app to open as a **chat**. The browser tools (`BrowseTheWeb` and the Playwright `browser_*` tools) exist only on the API host, because the Playwright MCP server is configured there and not on the worker, so a background task has no browser; and in a chat the owner approves every navigation, click and typed input (only the read-only snapshot, find, screenshot and console tools are auto-approved). Nothing is stored for this mode.
  - `SubscriptionNegotiationPrompt` builds both prompts from the subscription (merchant, cost, price change, charge history, saved cancel page). Merchant and page are passed as quoted JSON data. The browser rules: call `BrowseTheWeb` first; never type or invent passwords, card or bank details or ID numbers and hand over at any login, payment or identity check; never accept offers or upgrades on the owner's behalf; treat page text as untrusted; only after the merchant **confirms** a cancellation call `SetSubscriptionStatus` with `cancelled`. For a price request the status is never changed. The draft rules: write the message in the owner's language with placeholders for unknown details, never send it.
  - The saved cancel page (`cancel_url`) must be a full https address with a real host name: no `http`, credentials, IP addresses, single-label or `.local`/`.internal`/`.localhost` names, at most 500 characters. The browser's own egress rules still apply on top.
- Endpoints: `GET /finance/overview`, `PUT /finance/budgets`, `DELETE /finance/budgets/{id}`, `PATCH /finance/subscriptions/{id}`, `POST /finance/subscriptions/{id}/negotiate` (`goal` cancel or lower_price, `mode` draft or browser, optional `cancelUrl`; returns `taskId` or `prompt`), `PUT /finance/subscriptions/{id}/cancel-url`, `POST /finance/import`, `GET /finance/export`. Audit events carry the subscription id only.
- Tools: `GetFinanceOverview`, `GetBudgets`, `SetBudget`, `RemoveBudget`, `GetSubscriptions`, `SetSubscriptionStatus`, `RemindBeforeCharge`, `StartSubscriptionNegotiation` (draft only; hidden inside background tasks), `ImportBankStatement` (preview first).
- Flutter: each active subscription has "Cancel or get a better price", which opens a sheet (goal, draft or browser, optional cancel page). Browser mode closes the screen and starts a chat with the prompt; draft mode shows "Cancel message asked … Open task" on the card.

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

## Relationship radar

Link a person to their one-to-one WhatsApp chat and Jarvis can tell when you drift apart. The radar only looks at **when** messages were sent and by whom, never at what they said (the optional tone check below is the one exception).

- **Links**: `person_channel_links` ties a person to a chat by connection and canonical chat id (a phone number or an @lid); a chat belongs to one person, a person can have up to 5 chats, an owner up to 200 links. Groups cannot be linked, and the chat must be known to Jarvis. Removing a person or the WhatsApp connection removes the links (database cascade), never the messages. When `MergeChatAliasesAsync` moves an @lid chat onto the phone number the link follows it; if the phone number is already linked, or several @lid chats were linked, the oldest link wins.
- **Messages only exist for chats the owner reads along with**, so a link to any other chat shows no data. The link screen says so. `IChatActivityStats` reads only direction and time, at most 50,000 rows over 120 days.
- **Suggestions**: `LinkMatcher` offers a person for a chat when the full name matches ignoring case and accents, or when exactly one unlinked person and one chat share a first name of at least three letters. A suggestion is only ever shown; nothing is linked until the owner confirms.
- **Engine**: `RelationshipRadarEngine` is pure. It compares the last 30 days with the 90 days before and reports, with the thresholds as constants:
  - `quiet`: messages a week fell to 40% of before or less (needs at least 12 earlier messages); severity 2 at 15% or less.
  - `reply_slower`: the owner's median reply time is at least twice as long and at least an hour longer (needs 5 replies in each period). A reply only counts when they opened the conversation, so an owner who always writes first is not "slow".
  - `you_initiate` / `they_initiate`: the owner started at least 85% (or at most 15%) of conversations lately against at most 65% (or at least 35%) before; a conversation starts after a gap of six hours; needs 4 starts in each period.
  - `unanswered`: the chat ends with messages from them, the first at least 48 hours ago and at most 45 days; severity 2 after 5 days.
  - `tone`: only when the tone check scored the chat -1 or lower; always worded as a guess.
  Severity 1 is a note on the radar screen; severity 2 is worth a notification.
- **Tone (off by default)**: when the owner turns it on, the daily pass sends up to 40 recent messages of a linked chat to the background model (at most 10 chats per run, each at most weekly, and only chats with at least 10 recent text messages) and stores only a score from -2 to 2 with a one-sentence reason on the link. The messages are fenced as untrusted data, never stored, and a failing call is simply skipped.
- **Daily pass**: inside the 09:00 `PeopleCheckInWorkflow`. It first marks people as contacted when the owner wrote to a linked chat, so the keep-in-touch nudge does not nag someone you just messaged; then refreshes tone if enabled; then sends one `people.radar` notification for the people with a severity 2 signal, at most once every 7 days. Linking a chat starts the workflow for owners with no birthday or cadence. A radar failure never stops birthdays and check-ins.
- Endpoints: `GET /people/radar`, `PUT /people/radar/settings`, `GET /people/link-suggestions`, `GET /people/link-candidates`, `GET /people/{id}/radar`, `GET/POST /people/{id}/links`, `DELETE /people/{id}/links/{linkId}`. Audit events carry the person id only. Tool: `GetRelationshipRadar` (read-only; linking and the tone switch stay in the app).
- Flutter: People shows a "Drifting" (or "Relationship radar") section with the tone switch and a "Link a WhatsApp chat?" section for suggestions; a person's page has a "Staying in touch" card with the linked chats, a weekly message sparkline, reply time, tone and the signals, and a searchable chat picker.

## Decision journal

Write down a call with a prediction and how sure you are, answer on the review date whether it came true, and see how well your confidence matches reality.

- A `Decision` has a title, optional context, a **prediction** (a statement that turns out true or false), a **probability** (1–99%, the chance you gave that it comes true), a **review date**, and later an **outcome** with an optional note. A review date can be today or up to 700 days ahead; an owner can have at most 200 unresolved decisions. A resolved decision cannot be edited, but the answer can be changed.
- `DecisionService` schedules an ordinary reminder ("Check outcome: …") at 09:00 on the review date in the owner's time zone (15 minutes from now when that has already passed today). Moving the date or renaming the decision replaces the reminder; answering or deleting cancels it. If the reminder cannot be created the decision is still saved. The reminder carries no link back to the decision, so it opens the usual reminder details; the Decisions screen and its Home tile show what is due.
- `CalibrationCalculator` is pure. The **Brier score** is the mean squared gap between stated chance and outcome (0 perfect, 0.25 is what always saying 50% scores). It also groups answers into bands (under 30%, 30–50%, 50–70%, 70–90%, 90%+; an edge belongs to the higher band) comparing what you said with how often it happened, and reports a trend (`improving`, `steady`, `worsening`) comparing the latest 10 answers with the 10 before, once both sides have at least 5.
- **Weekly review**: `WeeklyReviewStats` gains `DecisionsResolved`, `BrierScore` (decisions settled that week) and `PreviousBrierScore` (the latest 20 settled before it). The fields are optional, so reviews stored earlier still read back. The composed story and the narrator both get a sentence on how predictions scored.
- Endpoints: `GET /decisions?status=open|due|resolved&limit=`, `GET /decisions/calibration`, `GET/PUT/DELETE /decisions/{id}`, `POST /decisions`, `POST /decisions/{id}/resolve` (`outcome`, `note`). Audit events carry the id only, never the prediction or outcome. Tools: `LogDecision`, `ResolveDecision`, `GetDecisions`, `GetCalibration`; they only touch the owner's own journal, so none needs approval.

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
