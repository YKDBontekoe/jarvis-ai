# HTTP API reference

Base path: **`/api/v1`** (authorized outside Development). OpenAPI: **`/openapi/v1.json`** in Development.

Real-time: **SignalR** `GET /hubs/events` — streamed assistant text and tool lifecycle events.

Agent2Agent (outside `/api/v1` group auth pattern):

- `GET /.well-known/agent-card.json`
- `POST /a2a` — JSON-RPC with bearer token

## Auth

| Method | Path | Notes |
|--------|------|-------|
| POST | `/api/v1/auth/register` | Anonymous if registration enabled |
| POST | `/api/v1/auth/login` | Returns access + refresh tokens |
| POST | `/api/v1/auth/refresh` | Rotating refresh |
| POST | `/api/v1/auth/logout` | |

## Conversations and approvals

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/conversations` | Create conversation (`profileId` optional) |
| GET | `/conversations` | List, pinned first then most recent (includes bound profile name and `pinned`). Each row also carries `lastMessage` (newest user or assistant message as one plain line, Markdown stripped, at most 160 characters), `lastMessageRole`, `lastMessageAt`, `responding`, and `origin`/`originId` (`reminder` plus the reminder's id when a reminder started the chat) |
| GET | `/conversations/{id}` | Messages + responding flag + profile |
| PATCH | `/conversations/{id}` | Rename (`title`) and/or pin (`pinned`); 404 for task-backed chats |
| PUT | `/conversations/{id}/profile` | Switch bound profile snapshot (409 if scope changes without `confirm`) |
| DELETE | `/conversations/{id}` | Delete chat (not task-backed) |
| POST | `/conversations/{id}/messages` | Send user message (starts agent turn). Optional `imageFileIds` (up to 4 of the owner's JPEG/PNG/WebP files, 8 MB each) are shown to the model in that turn only; stored history keeps a note instead of the image |
| POST | `/conversations/{id}/regenerate` | Answer the last user message again, replacing the last reply. 409 while approvals are open or when that reply used tools (so actions are never repeated) |
| POST | `/conversations/{id}/cancel` | Cancel in-flight run |
| POST | `/conversations/{id}/summary` | Read-only recap: `summary`, `keyPoints`, `actionItems`, `messageCount`. Nothing is stored. 409 when the chat has fewer than 2 messages, 503 when the model is unavailable |
| GET | `/approvals` | Pending tool approvals. Each row includes `category`, `categoryLabel`, and `canRememberCategory` |
| GET | `/approvals/standing` | Categories the owner always allows (`category`, `label`, `grantedAt`, `expiresAt`, `scope`) |
| DELETE | `/approvals/standing?category=` | Turn off one standing grant |
| POST | `/approvals/{id}/decision` | Approve or decline (`approved`). `rememberCategory: true` with an approval also stores a standing grant for that action's category; `rememberHours` (1–8760) makes it expire and `rememberScope` (`all` or `tasks`) limits where it applies |

## Automation

| Area | Paths |
|------|-------|
| Reminders | `GET/POST /reminders`, `GET/DELETE /reminders/{id}`, `POST /reminders/{id}/snooze` (`minutes` or `until`), `POST /reminders/{id}/complete` (`conversationId` on each reminder). A place reminder posts `place` (`name`, `latitude`, `longitude`, `radiusMeters` 50–5000, `trigger` `arrive`/`leave`, `repeats`) instead of `dueAt` |
| Owner automations | `GET/POST /automations`, enable/run/history (`conversationId` and `lastRun` on each rule; approvals go through the shared `/approvals` inbox; see [automations.md](../automations.md)) |
| Condition watches | `GET/POST /watches`, `GET/DELETE /watches/{id}`. `POST` accepts `repeat` (keep watching after an alert) and `cooldownMinutes` (5–10080, default 60); records report `repeat`, `cooldownMinutes`, `lastTriggeredAt`, `triggerCount` |
| Autonomy settings | `GET/PUT /settings/autonomy` (`enabled`, `heartbeatMayStartTasks`, `maxHeartbeatTasksPerDay`, `maxHeartbeatTasksPerRun`, `triageInbox`, `digestInsteadOfDrop`, `level` (`ask_everything`, `standard`, `full`, or opt-in `autonomous`), `autoApproveReadOnly`, `autoApproveMcpReadHints`, `maxAutoApprovalsPerDay`, `reactToEvents`, `maxReactionsPerDay`, `autonomousOutboundCategories`, `maxAutonomousOutboundPerDay`); `GET /settings/autonomy/outbound-categories` lists what the autonomous level can allow. See [life-features.md](life-features.md#autonomy-envelope) and [event-spine.md](../architecture/event-spine.md#autonomy-levels) |
| Activity and related things | `GET /events?since=&kinds=&origins=&subject=&limit=` (the owner's event log, newest first), `GET /entities/{type}/{id}/related` (title and related things), `POST /entities/links` (`from`, `to`, `relation` as `type:id` refs), `DELETE /entities/links?from=&to=&relation=`; see [event-spine.md](../architecture/event-spine.md) |
| Tasks | `GET/POST /tasks`, `GET /tasks/{id}`, `GET /tasks/{id}/messages`, cancel endpoints |
| Daily briefing | `GET/PUT /briefings/daily` (see `AutomationEndpoints`); `POST /briefings/daily/default` with `{timeZoneId}` turns it on at 08:00 only when the owner has no briefing settings yet (an existing row, even "off", is returned unchanged) |
| Projects | `GET/POST /projects`, `GET/PUT/DELETE /projects/{id}` (details include its chats, files and tasks; delete keeps them and only clears the project), `PUT /conversations/{id}/project`, `PUT /files/{id}/project`, `PUT /tasks/{id}/project` (`{ projectId }`, null takes it out). `POST /conversations` and `POST /tasks` accept `projectId`; conversation DTOs carry `projectId`. A task belongs to a project through its own conversation. `PUT /projects/{id}/focus` (`{ outcome?, status?, nextAction?: { type?, id?, note? } }`) replaces the outcome and next action and leaves the rest of the project alone; `status` is active, completed or archived (null keeps it); `nextAction.type` is commitment, decision, reminder or task and must be the owner's own, null clears it. Project details add `nextAction` with the linked thing's current `title` and `state` (open, done, gone). The old `PUT /projects/{id}` never touches the outcome or next action |
| Weekly review | `GET /reviews/weekly?weeks=8` (settings, mood trend, recent reviews), `GET /reviews/weekly/{id}`, `PUT /reviews/weekly/settings` (`enabled`, `localTime`, `timeZoneId`), `POST /reviews/weekly/generate` (current week, no notification) |

## Timeline, inbox, finance, library, modes, missions, routines, decisions

Described in [life-features.md](life-features.md): `/timeline` (`/on-this-day`, `/insights`), `/inbox` and `/commitments`, `/finance` (`/overview`, `/budgets`, `/subscriptions` with `/{id}/negotiate` and `/{id}/cancel-url`, `/import`, `/export`, `/wealth`, `/accounts` with `/{id}/reconcile` and `/{id}/import`, `/portfolio` with `/holdings`, `/trades` and `/refresh`), `/transactions`, `/automations` studio routes (`/event-kinds`, `/templates`, `/simulate`, `/webhooks`) and the public `POST /hooks/{token}`, `/library` (`/clip`, `/notes`, `/digest`, `/research`, `/cards`), `/modes`, `/missions` (`/steps`), `/routines/suggestions` (`/refresh`, `/{id}/accept`, `/{id}/dismiss`), and `/decisions` (`/calibration`, `/{id}/resolve`).

## Memory and learning

| Method | Path |
|--------|------|
| GET/POST | `/memory`, `/memory/search`, `/memory/{id}` |
| GET | `/search?query=&kinds=` | One search over conversations, **messages** (what was said inside a chat; excerpt around the match; the chat's title may not contain it), memories, files, tasks, reminders, skills, graph entities, channel threads, coding runs, **people**, **promises** (suggested ones say so) and **decisions**. Every hit has a typed `route` (`message` carries `conversationId` and `messageId`; `person` / `commitment` / `decision` carry `personId` / `commitmentId` / `decisionId`). Accents are ignored ("Joel" finds "Joël") in everything except message text, which PostgreSQL matches as typed. The agent's `SearchEverything` never returns message hits |
| GET | `/conversations/{id}/messages?around={messageId}&limit=` | The message with history either side, plus `nextCursor`/`hasMore` (older, usual `cursor` paging), `newerCursor`/`hasNewer` (newer, `?after=<cursor>`) and `anchorId`. 404 when the message is not in that conversation |
| PUT/DELETE | `/memory/{id}` |
| GET/PUT | `/settings/learning` |
| GET | `/learning/status` |
| POST | `/learning/run`, `/learning/dream` |

## Day planner

| Method | Path |
|--------|------|
| GET | `/planner/today` (timeline of calendar events, reminders and focus blocks; to-dos; free slots) |
| POST | `/planner/today/items` (`title` ≤ 200, `minutes` 5–480, default 30; optional `sourceRef` `commitment:<id>` plans an open, accepted promise: the title defaults to its description and planning it again returns the same to-do; items carry `sourceRef`) |
| PATCH/DELETE | `/planner/today/items/{id}` (PATCH: `title`, `minutes`, `done`) |
| POST/DELETE | `/planner/today/plan` (place open to-dos in free time / take them off the timeline) |
| PUT | `/planner/today/hours` (`dayStart`, `dayEnd`, at least one hour apart) |

Every route takes an optional `timeZone` query (the device's IANA zone); it falls back to the zone the app last sent, then the daily-briefing zone, then UTC. The plan is stored in owner settings (`planner.day`), so there is no table or migration. Open to-dos carry over to the next day without their blocks. Placement is deterministic (earliest free gap, 5-minute buffer after events, all-day events do not block). Nothing here writes to the calendar.

## Journal

| Method | Path |
|--------|------|
| GET/POST | `/journal` (`from`, `to`, `limit` query filters on GET) |
| GET/PUT/DELETE | `/journal/{id}` |
| GET | `/journal/summary?days=30` (streak, averages, per-day series) |

Body: `entryDate`, `content` (≤ 6,000), `highlights`, `gratitude` (≤ 1,000 each), `rating` 1–10, `mood`/`energy`/`stress` 1–5, `tags` (≤ 10), optional `source` (`written`, `voice`, `chat`). At least text or one rating is required. "Today" uses the owner's daily-briefing time zone (UTC fallback). See [memory-knowledge-learning.md](memory-knowledge-learning.md#journal).

## Expenses

| Method | Path |
|--------|------|
| GET | `/expenses?month=YYYY-MM&category=` (month summary in the main currency: total, previous month, per category, per day, top merchants, other currencies, plus the expenses) |
| POST | `/expenses` (body: `amount` > 0, optional `currency` ISO 4217, `merchant` ≤ 80, `category`, `note` ≤ 200, `spentOn`, `receiptFileId`, `kind` expense/income/transfer, `accountId`, `transferAccountId`) |
| GET/PUT/DELETE | `/expenses/{id}` |
| POST | `/expenses/scan` (`fileId` of an uploaded JPEG/PNG/WebP ≤ 8 MB; returns a draft read by the Vision model, saves nothing) |

Owner-scoped (`expenses` table). Categories: groceries, dining, transport, shopping, housing, bills, health, entertainment, travel, subscriptions, other; missing ones are guessed from the merchant and note. A missing currency reuses the owner's last one (EUR at first); "today" uses the daily-briefing time zone. Amounts in other currencies are listed separately, never converted. Audit events (`expenses` tool) carry the expense id only.

## People

| Method | Path |
|--------|------|
| GET/POST | `/people` (body: `name` ≤ 80, `relationship` ≤ 40, `birthdayMonth`/`birthdayDay`/`birthYear` (year optional), `notes` ≤ 2,000, `contactEveryDays` 1–365, optional `graphEntityId`) |
| GET | `/people/suggestions` (knowledge-graph people not on the list yet, with relationship and birthday when known) |
| GET/PUT/DELETE | `/people/{id}` (GET returns `person` plus current graph `facts` when linked) |
| POST | `/people/{id}/contact` (optional `at`; records "last talked") |
| GET | `/people/radar` (`toneEnabled` and one report per linked person, most worrying first; also brings last-contact dates up to date) |
| PUT | `/people/radar/settings` (`toneEnabled`) |
| GET | `/people/link-suggestions` (people and unlinked one-to-one chats whose names match, for the owner to confirm), `/people/link-candidates` (every unlinked one-to-one chat, read-along ones first) |
| GET | `/people/{id}/follow-through` (rhythm with why it is or is not due, birthday, open accepted promises whose counterparty is exactly this name, nudge state) |
| PUT | `/people/{id}/nudges` (`mode` `on`/`snoozed`/`muted`, `snoozeDays` 1–365 for snoozed) |
| POST | `/people/{id}/nudges/not-useful` (snoozes 30 days, 60 on the second time, and counts; a mute stays muted; undo with PUT `on`) |
| GET/POST | `/people/{id}/links` (POST body `connectionId`, `chatId`; 400 for a group or unknown chat, 409 when the chat belongs to another person) |
| DELETE | `/people/{id}/links/{linkId}` |
| GET | `/people/{id}/radar` (the person's linked chats and their report) |

Owner-scoped (`people` table). Names are unique per owner ignoring case and accents. `daysUntilBirthday`, `daysSinceContact` and `contactDue` use the daily-briefing time zone (UTC fallback). A new person links to the graph entity of the same name. Audit events (`people` tool) carry the person id only. The radar and link routes are described in [life-features.md](life-features.md#relationship-radar).

Knowledge graph read/update endpoints are split between `KnowledgeGraphEndpoints` and `PersonalAssistantEndpoints` (`/graph/...`).

`POST /graph/ask` answers a question from the owner's graph. Body: `question` (≤ 500 characters, required), optional `focusIds` (≤ 5 entity ids the app has in focus) and `history` (earlier `{question, answer}` turns; the last 4 are used). Returns `answer`, the `entityIds` and `links` (`from`, `to`, `predicate`) it relied on so the map can highlight them, the cited `facts` as sentences, up to three `followUps`, and `grounded` (false when no model was available and the answer only lists the facts found). Retrieval is deterministic (`GraphQuestionService`): named and focused entities, the user for "I/my", their facts and their neighbours', shortest paths between them that don't run through the user, facts whose wording matches the question, and ended facts for "used to" questions. The model (`ModelGraphAnswerModel`, background purpose, no tools) only phrases an answer from those numbered facts.


## Habits

| Method | Path |
|--------|------|
| GET | `/habits?includeArchived=` (returns `today`, `habits` with streak stats, and `settings`) |
| POST | `/habits` (`name`, `icon`, `cadence` `daily`/`weekly`, `targetPerWeek` 1–7, optional `timeZoneId`) |
| GET/PUT/DELETE | `/habits/{id}` |
| PUT | `/habits/{id}/archived` (`archived`) |
| POST | `/habits/{id}/check-ins` (`date` optional, up to 7 days back; `done` default true) |
| GET/PUT | `/habits/settings` (`eveningCheckIn`, `checkInTime` `HH:mm`, `timeZoneId`) |

Weeks run Monday to Sunday; a weekly habit's streak counts weeks that reached `targetPerWeek`. The evening check-in is the per-owner `HabitCheckInWorkflow`, which sends a `habit.checkin` notification (channel category `check_ins`) naming habits still open that day. Audit entries carry ids only.

## Files

| Method | Path |
|--------|------|
| GET/POST/PUT/DELETE | `/comparisons`, `/comparisons/{id}` | Weigh options against criteria. A definition has `criteria` (id, name, weight 0–10), `options` (id, name, `cells`: criterion id → `{ score 0–10 or null for unknown, note, source, asOf }`) and the server adds `result`: per option a weighted average over the *known* criteria (`score`, `coverage`, `unknownCriteria`, `rank`, `tied`, `thin`), the `leaderId`, `sensitivity` (does removing a criterion change the leader) and `warnings`. An unknown is never a zero; scores within 0.05 tie. Limits: 12 criteria, 8 options, 100 comparisons per owner (409 past that) |
| PUT | `/comparisons/{id}/cell` | `{ option, criterion, cell }` sets one answer, found by id or name |
| POST | `/comparisons/{id}/library` | Saves the comparison as a tagged Library report (unknowns, notes, dated sources and what could change the answer stay in the text) |
| POST | `/comparisons/{id}/decide` | `{ optionId, probability, reviewOn }` logs one decision (the context carries the score and what is still unknown); repeating it for the same option returns the same decision, a different option is 409. The decision link cannot be dropped by editing. Nothing is bought or sent |
| GET/POST/PUT/DELETE | `/meetings`, `/meetings/{id}` | Meetings (title, optional start and project, `template` general / one_on_one / project_update / interview / decision, up to 12 participants; a participant with `personId` must be the owner's own and takes its name from People). The record's `document` holds `purpose`, `agenda` (lines with `shared`), `privateNotes`, `prep`, `debriefText` and `candidates`. 300 per owner |
| GET | `/meetings/events?days=` | Upcoming calendar events to pick from; empty (not an error) when no calendar is connected or it fails |
| POST | `/meetings/{id}/prep` | Builds the prep pack from the owner's own records: project outcome and next step, open promises with the participants (overdue first), unresolved decisions that mention them or the project, project files, what earlier meetings left unanswered; every fact carries a `ref`, missing information becomes a `question`. Fills the agenda only when it is empty |
| POST | `/meetings/{id}/debrief` | `{ text }` (≤ 8,000 chars) lists what it seems to say as `candidates` (decision, i_owe, they_owe, question; English and Dutch, rule-based; a vague date stays `dueText` with no `dueOn`). Creates nothing. Reading again keeps earlier accept/dismiss choices |
| POST | `/meetings/{id}/candidates/{candidateId}/accept` \| `/dismiss` | Accept creates a Commitment (source `meeting`) or logs a Decision (needs `probability` and `reviewOn`); accepting twice creates one; a question cannot be accepted; an accepted candidate cannot be dismissed. Dismiss changes only the candidate |
| POST | `/meetings/{id}/followup` | A draft (`sent` is always false) built from accepted items and the agenda lines marked shared; private notes, private agenda lines and unaccepted items are never included |
| GET | `/reviews/weekly/follow-through?weekStart=` | What the week left behind, counted from the owner's records in their time zone: promises fulfilled / dropped / overdue / a week or more overdue, decisions to review next week, projects without a next step (or with a finished one), the items behind each count, and at most 3 suggestions. Read-only: creates nothing |
| POST | `/reviews/weekly/follow-through/accept` | `{ kind, targetId, on?, note?, linkType?, linkId? }`: `plan_commitment` moves one open promise to day `on` (today or later; same day again changes nothing); `project_next_step` sets the project's next step from `note` or a link and keeps its outcome. Each changes exactly one existing record |
| POST | `/reviews/weekly/follow-through/dismiss` | `{ kind, targetId }` hides that suggestion for 28 days; 400 for an unknown kind |
| GET | `/files`, `/files/search` |
| POST | `/files/{id}/ask` | `{ profileId? }` starts a chat about one file: a new conversation titled "About …" with that file attached as its only source; 201 `{ conversationId, fileId, fileName, status }`. 404 for an unknown or deleting file; 409 when the profile restricts files to collections that do not contain it (attaching never widens a profile) |
| GET | `/memory/review` | Memories worth a second look: learned lately (not by the owner), low confidence, expiring; each with `reasons`, `source` (label and, for a chat, `conversationId`/`messageId`), profile name and how many times it was corrected |
| POST | `/memory/{id}/correct`, `/memory/{id}/expire`, GET `/memory/{id}/history` | Correct replaces a current memory (the old one ends now and stays as history; the new one is the owner's own certain statement); expire ends it with no replacement. 409 for a pinned memory or one that is no longer current. History lists the replaced versions, newest first |
| POST | `/files` (multipart upload) |
| GET | `/files/{id}/content` |
| POST | `/files/{id}/reprocess` |
| DELETE | `/files/{id}` |

## Settings

| Group | Paths |
|-------|-------|
| Models | `/settings/models`, `/settings/models/codex`, `/settings/models/embedding` (active embedding model and indexing progress), `/openrouter-key`, `/test`, catalog |
| Voice | `/settings/voice` |
| Devices | `/settings/devices` |
| Persona | `/persona` (see `PersonaEndpoints`) |
| Profiles | `/profiles` |
| Collections | `/collections` |

## Skills

`/skills` — CRUD, import/export, status, lock.

## Integrations and MCP

| Path | Purpose |
|------|---------|
| `/integrations/credentials` | List configured providers (no values) |
| `/integrations/{provider}/credentials` | Put/delete secrets |
| `/integrations/connections` | Per-turn MCP connection status: tool counts, read-only tools, `selfBuilt`, `callsLast7Days`, `lastUsedAt` |
| `/integrations/packs`, `/integrations/oauth/*` | Guided packs and OAuth |
| `/mcp-servers`, `/mcp-controls/{name}` | Owner MCP registry |
| `POST /mcp-servers/{idOrHostName}/test` | Connect once: `{ok, state, toolCount, tools, errorKind, message}`; `errorKind` is `unauthorized`, `missing_key`, `timeout`, `unreachable`, `crashed`, `runner_required`, `not_activated` or `unavailable` |
| `/mcp-catalog`, `/mcp-catalog/featured`, `POST /mcp-catalog/install` | App directory search (entries carry `installed`, `installedServerId`), featured apps by category, install |
| `/custom-mcp-servers`, `/custom-mcp-servers/{id}` + `POST …/test`, `…/activate {sha256}`, `…/revert {revision}`, `DELETE` | Apps Jarvis wrote: code, revisions, last test, approval |

## Channels

`/channels` — WhatsApp/Signal configuration, threads, test send, QR linking (`POST /channels/link`, `GET /channels/link/{linkId}`, `GET /channels/providers`), Signal status, WhatsApp Cloud webhooks.

`/channels/{id}/chats` — read along on a QR-linked WhatsApp: list phone chats with the owner's choices (`GET`), turn a chat on or off and toggle automatic reminders (`PUT /{chatId}`), messages (`GET /{chatId}/messages`, `DELETE` clears what Jarvis saved), media bytes for a photo, sticker, voice note, document or video preview (`GET /open/media?messageId=`), a drafted reply (`POST /{chatId}/suggest`), send after the owner taps Send (`POST /{chatId}/send`, with optional `replyTo` set to a saved message id to send a quoted WhatsApp reply), react to a saved message with one emoji or `""` to remove it (`POST /{chatId}/react` with `messageId` and `emoji`), and Ask Jarvis about the chat (`POST /{chatId}/ask`, runs a normal agent turn in a per-chat conversation). Message JSON includes `media` and `quote` when WhatsApp sent more than text; voice notes carry `media.waveform` (up to 64 loudness samples, 0-100) when WhatsApp provided one. Audit entries carry ids only.

The chat list includes the account, bridge session state, previews from saved read-along messages, Jarvis unread counts, and a `catchUp` summary (`summary`, `toReply[{who, about}]`, `messageCount`) for read-along chats with unread messages. `GET /channels/{id}/chats/status` reports account/state; `POST /channels/{id}/chats/{chatId}/read {messageId}` marks through a loaded message. Message paging accepts `beforeId` for stable ordering even with equal timestamps, or the older `before` timestamp parameter. Unread state is owner-scoped and does not send WhatsApp read receipts. `POST /channels/{id}/chats/{chatId}/quick-replies` returns `{replies}`: up to three short replies the owner could send next, in the chat's language, for read-along chats whose last message is not the owner's (empty otherwise). They are cached per chat and newest message, and never sent by themselves.

## Voice (user)

`POST /voice/session` — LiveKit token + session metadata.

Internal routes under `/voice/internal/{conversationId}/...` are for the voice runtime (secret-protected).

## Browser

`GET /conversations/{id}/browser-sessions`, `GET /browser-sessions/{id}`.

## Surfaces (generative UI)

`GET /conversations/{id}/surfaces`, `POST /ui-surfaces/{id}/actions`.

## Home, devices, coding

| Path | Purpose |
|------|---------|
| GET `/home/context` | What Home leads with: `dayLine` (one sentence about the day), `hero` (one card: `brief`, `soon`, `battery` or `wind_down`, with `reason` and `facts` for the why sheet; null when nothing stands out), `people` (people linked to a WhatsApp chat, with relationship, usual reply minutes, hours waiting and whether it has gone quiet), `noticed` (routine suggestions, quiet contacts, birthdays within two days), `tomorrow`, `dayRecap`. Each source is read on its own; one that fails or takes over 3 s is left out |
| POST `/home/context/dismiss` | `{id, kind, mute?}` hides a card for 7 days, or the whole kind with `mute`. A routine insight is also declined in Routines. 400 for an unknown kind |
| GET `/home` | Home briefing payload. `latestBriefing` (`id`, `title`, `body`, `createdAt`, `read`) is today's morning briefing notification, or null when none arrived in the last 18 hours. `commitments` lists accepted (not suggested) open promises due today or earlier in the owner's zone (at most 5; `daysOverdue`, `reminderId`), and `decisions` lists unresolved decisions whose review day has come (at most 5), both soonest-due first |
| POST `/devices/telemetry` | Battery/location snapshots; a location also checks pending place reminders |
| POST `/devices/invoke/{id}/result` | Device capability callback |
| GET `/coding/runs`, `/coding/runs/{id}` | Coding task history |

## Notifications, push, audit

| Path | Purpose |
|------|---------|
| GET `/notifications` | In-app notifications |
| POST `/notifications/{id}/read` | |
| POST `/notifications/{id}/actions` | Notification buttons on `reminder.due`: `{"action":"done"}` or `{"action":"snooze","minutes":10}` (1 to 1440). Other types, approvals included, return 400: approvals are decided in the app only |
| PUT/DELETE `/push-devices` | FCM registration |
| GET `/audit` | Append-only audit events |

## Improvements

Owner-scoped; see [memory-knowledge-learning.md](memory-knowledge-learning.md#signals-traces-and-improvement-proposals).

| Route | Purpose |
|-------|---------|
| GET `/improvements` | Pending proposals (`id`, `kind`, `title`, `evidence`, `confidence`, `status`, `canUndo`) and recent changes that can still be undone |
| POST `/improvements/refresh` | Run the skill and review miners now (honours `proposeImprovements`); returns the counts added |
| POST `/improvements/{id}/accept` | Apply a pending proposal; idempotent. 404 if it is not the caller's or no longer valid |
| POST `/improvements/{id}/dismiss` | Refuse a pending proposal for good; 404 otherwise |
| POST `/improvements/{id}/undo` | Reverse an accepted or automatically applied change within 30 days |

Accept, dismiss and undo are audited by id and kind only.

## Usage

`GET /usage` — token and activity aggregates (see `UsageEndpoints`). The response includes an `improvement` object (ratings and trend, regenerate rate, weak tools, learned skills, proposal counts). It also includes `turnQuality` (`TurnQualityMetrics`): chat model calls with median and p90 reply time, failure rate and prompt-cache hit rate; traced turns (any turn that used memory, a skill or a tool, or did not finish) with median and p90 duration and failed/cancelled counts; tool calls, failure rate and the three slowest tools (at least 5 calls each); approvals asked, declined and handled automatically; and `flags` (`slow_replies` when p90 reply time is over 30 s, `failing_turns` at 10% failed turns, `approval_friction` when 10+ approvals were asked and fewer than half were automatic, `low_cache` under 20% cached input). Medians need at least 5 samples, otherwise they are null. At most 5,000 rows of each kind are read per period.

## Remote agents (A2A management)

Under `/api/v1/agents` and `/api/v1/a2a-tokens` — registry and inbound token hashes.

## Implementation map

Each route group is implemented in `src/Jarvis.Api/Endpoints/*.cs` and registered in `Program.cs`. When adding routes, prefer existing groups and `ICurrentUser` for owner checks.

## Goals

Stored in owner settings (`goals`, no table). A goal links existing habits, one project and promises by id (validated against the owner); suggestions are proposals.

| Method | Route |
|--------|-------|
| GET/POST | `/goals` (GET `includeArchived`; POST `title` ≤ 200, `motivation`, `horizon`, `target`, `checkInEveryDays` 3–60 (default 7), `weeklyMinutes` 15–1200 (default 90), `projectId`, `habitIds` ≤ 5, `commitmentIds` ≤ 8) |
| GET/PATCH/DELETE | `/goals/{id}` (PATCH takes the same fields plus `status` `active`/`paused`/`achieved`/`archived`, `clearProject`, `clearHorizon`) |
| POST | `/goals/{id}/check-ins` (`progress` required, `friction`, `adjustment`, `feeling` `on_track`/`slipping`/`stuck`) |
| POST | `/goals/{id}/plan` (proposes ≤ 3 small steps that fit `weeklyMinutes`; accepted, done and declined steps are kept; 409 while paused) |
| POST | `/goals/{id}/plan/items/{itemId}` (`action` `accept`/`reduce` (+`minutes`, shorter)/`decline`/`done`/`schedule` (accepted only; adds to Today, `timeZone` query; a promise step keeps its `commitment:` source)) |
| POST | `/goals/{id}/experiments`, `.../{experimentId}/notes`, `.../{experimentId}/finish` (`keep`) (one running experiment, 3–60 days; notes are observations) |
| POST | `/goals/{id}/review/dismiss` (the keep/change/stop suggestion stays quiet 28 days) · `/goals/{id}/reset-season` (history kept, progress and plan restart) |

A declined step is not proposed again for 28 days. The keep/change/stop suggestion is rule-based on the owner's own check-ins of the current season and needs two; a passed horizon suggests finishing or moving it.

## Subscription renewal review

Per-subscription review state lives in owner settings (`subscriptions.reviews`, no table). Nothing here contacts anyone.

| Method | Route |
|--------|-------|
| GET | `/finance/subscriptions/{id}/review` (facts from the bank data with the estimate flagged, notice day, six paths each with its next action, the owner's choice, confirmed saving) |
| POST | `/finance/subscriptions/{id}/review/choice` (`choice` `keep` (quiet 90 days) / `investigate` / `negotiate` / `cancel` / `remind` (sets the reminder) / `archive` (stops tracking); negotiate and cancel only note the choice) |
| POST | `.../review/comparison` (starts one comparison with the current plan as the first option; the same id every time) |
| POST | `.../review/pack` (`goal` `lower_price`/`cancel`, optional `projectId`: a report saved to the Library with account details as `[ADD]` placeholders and a confirmation checklist) |
| POST | `.../review/follow-up` (`expectedBy`, default a week: the provider's reply as one accepted promise owed to the owner) |
| POST | `.../review/outcome` (`confirmedAmount` must be lower than the current price; records a saving as a monthly equivalent with its basis and completes the follow-up; the detected charge is not rewritten) |
| POST | `.../review/cancellation` (`evidence` ≥ 8 characters, the merchant's confirmation; only then the subscription becomes cancelled) |

The agent tool `SetSubscriptionStatus` now needs `evidence` for `cancelled` and goes through the same check.

## Statement import batches

A committed statement import that adds rows becomes a batch in owner settings (`finance.imports`, latest 20, no table). Importing the same file again adds nothing and creates no batch.

| Method | Route |
|--------|-------|
| GET | `/finance/imports` (each batch: rows, imported, duplicates, how many of its rows still exist, how many were edited since, undone state) |
| POST | `/finance/imports/{id}/undo` (removes only rows the batch created that still exist and were never edited; edited rows, duplicates of earlier rows and anything else with the same merchant stay; undoing again changes nothing; audit entry carries counts only) |

`POST /finance/import` answers with `batchId` on a commit that added rows.

## Promise links and follow-up nudges

Commitments carry optional `projectId` and `personId`, set only by the owner (`PATCH /commitments/{id}` with `projectId`, `personId`, `clearProject`, `clearPerson`; unknown or foreign ids are 400, migration `AddCommitmentLinks`). A linked promise counts under that person even when the counterparty name differs; an unlinked one only matches an identical full name, never a nickname.

| Method | Route |
|--------|-------|
| GET | `/commitments/follow-ups` (`language` `en`/`nl`: up to five open, accepted, owed-to-me promises at least 3 days late, longest first, each with a draft; nothing is sent) |
| POST | `/commitments/{id}/follow-up/snooze` (`days` 1–60) · `/commitments/{id}/follow-up/dismiss` (quiet for good; stored in owner settings `commitments.followups`) |

## What-if scenarios

Stateless arithmetic plus a save step; nothing here reads or changes budgets, accounts, subscriptions or the calendar.

| Method | Route |
|--------|-------|
| POST | `/scenarios/evaluate` (`kind` `savings`: `savingsBaseline` / `savingsAlternative` with `monthlyCost`, `months` (1–120, equal on both sides), `oneTimeCost`, `currency` (the same three letters on both sides); `kind` `time`: `timeBaseline` / `timeAlternative` with `hoursPerWeek`, `taskHours`, `bufferPercent`; answers with both outcomes, deltas that carry their arithmetic in words, and notes; 400 names the field) |
| POST | `/scenarios/save` (`scenario`, optional `projectId`, and optionally `decisionProbability` with `decisionReviewOn`, both or neither; writes one Library report tagged `scenario` and, when asked, one decision; the decision input is checked before anything is written) |

## Goal-led app discovery

Starts from what the owner wants, not from a connector list. Setup itself stays in the existing pack, OAuth and credential routes; no secret passes through these.

| Method | Route |
|--------|-------|
| GET | `/apps/goals` (`q` words in English or Dutch: matching goals each with the pack, `status` `ready`/`not_connected`, the real `nextStep`, capabilities (`reads`, `prepares`, `changes`, `needs`) derived from the pack's actual tool names, a harmless `try` only when ready, and `pinned`; `unmatched` with an honest `suggestion` when nothing covers the goal) |
| POST | `/apps/goals/{goalId}/try` (calendar: reads today's events for real, or answers `problem` with what to check when the link cannot be read; mail and contacts: a read-only chat prompt; 404 until the app is connected) |
| PUT/DELETE | `/apps/goals/{goalId}/pin` (pins an app action, max six, only when connected; stored in owner settings `apps.pins`) |

A calendar is ready with a saved link or an MCP server; mail and contacts only with an MCP server (a stored token alone cannot read anything).

## Automation templates (F10.1.7)

`GET /automations/templates` now also returns, per template, `successCriterion` (what good looks like) and `preview` (what each action would do, from the same dry-run the simulator uses; an event template previews with an example event of its own kind). New named templates: `meeting-prep` (30 minutes before a calendar event, writes a prep note), `document-follow-up` (suggestions from an uploaded file only), `bill-reminder` (Friday 09:00, subscriptions due in 7 days) and `weekly-project-review` (Friday 16:00). All four are read-or-suggest tasks that need no approval, and creating one only makes a draft.

## Lessons from a saved item (F28 first slice)

Built without a model from the item's own text, so the same text always gives the same lesson and answer key; nothing is stored until the owner keeps cards.

| Method | Route |
|--------|-------|
| GET | `/library/{id}/lesson` (up to five quoted sentences with their offsets, three fill-in-the-blank questions and one explain-it-back prompt, a time estimate and an honesty note; 400 when the text is too thin to teach from) |
| POST | `/library/{id}/lesson/check` (`questionId`, `answer`: verdict `correct` / `close` / `incorrect` / `unsure`, the expected answer and the quoted source; empty or "I don't know" is `unsure`; explain-it-back is checked against terms from the source and says it is not a judgement of understanding) |
| POST | `/library/{id}/lesson/cards` (`questionIds`, only fill-in questions, at most five, into the existing SM-2 cards with the source sentence on the back; repeats are skipped) |

## Documents (F27 first slice: project update)

Drafts live in owner settings (`documents.drafts`, at most 20, no table). Creating, saving and exporting never send or share anything.

| Method | Route |
|--------|-------|
| GET/POST | `/documents` (POST `kind` `project_update`, `projectId`, `audience` `team`/`client`/`self`, `language` `en`/`nl`: sections Where we are, Next step, Open promises (linked to the project), Background (the project's Library notes, max three), Open questions; every filled section carries `refs` (`project:`, `commitment:`, `library:`); unknowns are `[ADD: …]` placeholders and listed) |
| GET/DELETE | `/documents/{id}` (GET adds a short review checklist: placeholders left, uncited sections, people named, "saving does not send") |
| PUT | `/documents/{id}/sections/{sectionId}` (`body`, the owner's words; only that section changes, the previous text is kept) |
| POST | `.../sections/{sectionId}/revise` (`action` `shorten` or `rebuild` from the sources) · `.../sections/{sectionId}/undo` |
| POST | `/documents/{id}/save` (a Library report in the project with a Sources line per section, titled with the version; saving the same version again returns the same item) |
| GET | `/documents/{id}/export?format=md\|txt` (a file download) |

`POST /library/notes` now also takes an optional `projectId` (validated against the owner) so notes can be filed in a project.

## Opportunity radar (F31)

Deterministic rules over the owner's own facts; reading proposes only and never sends, creates or changes anything. State (feedback, muted kinds, history) lives in owner settings (`opportunities`).

| Method | Route |
|--------|-------|
| GET | `/opportunities` (up to eight cards: `title`, `whyNow`, `evidence` refs, `destination`, `urgency` 1–3, `windowEnds`, `also` for related chances merged under one project / person / subscription; plus `history` of what was useful and `mutedKinds`) |
| POST | `/opportunities/feedback` (`key`, `verdict` `useful`/`later`/`handled`/`wrong`/`fewer`; 404 for a key not on the radar right now) |
| POST | `/opportunities/unmute` (`kind`; undoes "fewer like this") |

Rules: a meeting in the next 24 hours with no prep note; an open accepted promise due within 3 days or late (older than 30 days it only asks whether it still matters); a subscription's notice day within 7 days (unless kept); a decision whose review day is within 7 days; free time of 45+ minutes today with a project's open next step. "Later" returns after 3 days, "handled", "wrong" and "useful" hide that opportunity, "fewer" or two "wrong" for a kind mute it for 30 days.

### Preferences: home time zone

One precedence for every feature (`ZonePolicy`): the zone a single action names, else the zone a saved schedule carries, else the owner's home zone: the one they chose (`preferences.home`), then the zone borrowed from the morning brief (labelled `brief`, not a choice), then UTC as an admitted default (`default`). A phone's zone is only offered in the app; it never changes anything on its own, and changing the home zone moves nothing already scheduled (reminders and the brief keep their own zones). Used by Goals, Documents, the week follow-through, people check-ins and everything that reads the timeline day.

| Method | Path |
|---|---|
| GET | `/preferences` (`homeTimeZone`, `source` `chosen`/`brief`/`default`, `chosen`, `localNow`, `explanation`) |
| PUT | `/preferences/home-zone` (`timeZoneId`; empty or null goes back to automatic; 400 for unknown zones) |
| POST | `/preferences/home-zone/preview` (what would change and what would not, and the time there now; changes nothing) |

Written behaviour (tests): a local time that does not exist (clocks jump forward) runs an hour later; a repeated local time uses the first occurrence; midnight rolls over in the owner's zone, not UTC.

### Voice companion cards

When a voice tool call answers something with rows (what needs you, a comparison), the same call stores a card with those rows and publishes `voice.card` to the conversation's group, so the screen and the spoken words name the same real entities. The voice summarises; the card holds the rows. Cards live in owner settings (`voice.cards`, latest 30). "Save that" means the only card shown in the last 3 minutes; with two or more the tool returns a short choice instead of guessing. A put-away card cannot be saved. The recap counts saved, put away and still open, and states that nothing was sent, accepted or changed outside Jarvis by voice.

| Method | Path |
|---|---|
| GET | `/voice/{conversationId}/cards`, `/voice/{conversationId}/recap` |
| POST | `/voice/cards/{cardId}/save` (one private Library note, tags `voice`, `private`; again returns the same item), `/voice/cards/{cardId}/dismiss` |

Chat tools in a voice session: `SaveShownCard` (optional `which`) and `GetSessionRecap`.

### Self-built apps: review and builder

Activation of a self-built app (`POST /custom-mcp-servers/{id}/activate`, and the chat tool) now needs the sha256 of the draft that was reviewed (400 `sha256` when omitted, so an old client cannot activate "whatever is current"), and that exact code must have passed a test. Without a passing test for the same hash, `overrideReason` (10 to 200 characters) is required; otherwise 409 `not_tested`. A reason is kept in the audit trail (`custom_app.activated_untested`). A startup test is labelled as that: it lists tools but does not prove each one works.

| Method | Path |
|---|---|
| GET | `/custom-mcp-servers/{id}/review` (changed lines against the live version, dependencies added and removed, keys, `testStatus` `none`/`stale`/`failed`/`passed` with a plain `testLabel`, limits) |
| GET | `/custom-mcp-servers/starters` (four ideas) |
| POST | `/custom-mcp-servers/starters/compose` (`purpose` required; `inputs`, `data`, `result`, `changesOutside`) → `{prompt}` for chat |

### Mission starters and deliverables

Starters only turn a few answers into a goal; the existing planner then makes the plan, which waits in `ready` until the owner starts it. The deliverable is assembled from the mission's own steps: nothing is invented, and the checks are run by code (every step finished, none failed or stopped, a final result exists, at least one web source is named). They say the work exists, not that it is right, and the response says so. Model cost is not tracked per mission, so only elapsed time is shown.

| Method | Path |
|---|---|
| GET | `/missions/templates` (decision, brief, compare, meeting: `questions` with `required`) |
| POST | `/missions/templates/{templateId}/compose` (`answers`; 400 names the missing question) → `{title, goal}` |
| GET | `/missions/{id}/deliverable` (`headline`, `sections`, `evidence` links, `checks`, `limitations`, `nextDecisions`, `effort`, `reviewNote`, `libraryItemId`) |
| POST | `/missions/{id}/deliverable/save` (private Library report tagged `mission`; saving twice returns the same item; 400 when there is no result) |

### Notification preferences

What reaches the phone, per kind: `approvals` and `problems` (always buzz, cannot be quieted), `reminders`, `briefings`, `updates`. The push worker asks `INotificationPreferenceService.DecideAsync` before sending: a kind the owner set to `quiet` is not pushed (`app_only`), otherwise the current mode either lets it through (`push`) or holds it for the "while you were away" digest (`held`). The owner's choice is applied before the mode. The in-app notification list is never filtered. State lives in owner settings (`notifications.preferences`); if settings cannot be read the push is sent.

| Method | Path |
|---|---|
| GET | `/notifications/preferences` (current mode and summary; per kind: `setting`, `locked`, `now` outcome and reason, and the outcome in every mode) |
| PUT | `/notifications/preferences/{category}` (`push` `on`/`quiet`; 400 for locked kinds, unknown kinds or values) |

### Setup

Guided first steps and honest capability status. Steps (`talk`, `reminder`, `calendar`, `source`, `brief`) are done when the thing exists (a chat, a reminder, a connected calendar, a Library item, an enabled brief), so a reinstall or reconnect resumes without repeating anything. Only the owner's skips are stored (owner settings `setup.journey`); `calendar`, `source` and `brief` can be skipped, `talk` and `reminder` cannot. Capability statuses: `ready`, `not_connected`, `needs_reauth`, `unavailable`, `unsupported` (push registration cannot be checked from the server, so it says so). Nothing is sent or created to probe anything.

| Method | Path |
|---|---|
| GET | `/setup` (`goals`, `capabilities`, `done`, `total`, `nextGoalId`, `summary`) |
| POST | `/setup/goals/{goalId}/decline`, `/setup/goals/{goalId}/reopen` (400 for unknown or required steps) |
| GET | `/setup/support-bundle` (version, on/off states, goal states; reviewed in the app before copying; no names, hostnames, tokens or addresses) |

### Life stories

Recaps built from the timeline (`ITimelineService`) for a chosen window (at most 120 days). Decisions, tasks and journal are included by default; money, contacts and chats only when named in `kinds`. Items point at records through `sourceRef`; edits, removal and order change only the story. Patterns come from timeline insights only with 14+ days of data and always carry their window and limits. State lives in owner settings (`stories.drafts`, at most 20 drafts).

| Method | Path |
|---|---|
| GET / POST | `/stories` (POST: `from`, `to`, `style` `recap`/`retrospective`/`what_changed`, optional `kinds`) |
| GET / DELETE | `/stories/{id}` |
| PUT | `/stories/{id}/items/{itemId}` (`text`), `/stories/{id}/items/{itemId}/removed` (`removed`) |
| POST | `/stories/{id}/items/{itemId}/move` (`direction` `up`/`down`; 409 at the end), `/stories/{id}/save` (private Library report, one per version) |
| GET | `/stories/{id}/export?redact=journal,people,expense,…` (Markdown; leaves out those kinds, counts and patterns) |

### Trips

A trip workspace in owner settings (`trips`, at most 10 trips, 12 options, 80 itinerary items, 60 days). Options carry a price, currency, `quotedOn`, the dates quoted for (`forStart`/`forEnd`) and `sourceUrl`; the view derives a quote state per option: `current` (within 7 days, source and matching dates), `outdated`, `unquoted` (no source or date) and `other_dates`. Only chosen options in the trip currency are summed into the budget; other currencies are listed, not converted. One transport and one stay can be chosen; a `sold_out` option cannot. The checklist comes from the trip's facts and points to official sources for entry rules. Holds are proposals only: nothing is booked, bought or written to a calendar. Changing dates is previewed first; applying shifts items and marks them unverified.

| Method | Path |
|---|---|
| GET / POST | `/trips` (POST: `title`, `destination`, `start`, `end`, `travelers`, `abroad`, `budget`, `currency`, …) |
| GET / PUT / DELETE | `/trips/{id}` |
| POST / DELETE | `/trips/{id}/options`, `/trips/{id}/options/{optionId}` |
| PUT | `/trips/{id}/options/{optionId}/chosen` (`chosen`), `/trips/{id}/checklist/{entryId}` (`done`), `/trips/{id}/dates` (`start`, `end`) |
| POST | `/trips/{id}/items`, `/trips/{id}/dates/preview` |
| DELETE | `/trips/{id}/items/{itemId}` |

### Mode context and focus

`GET /modes/context` returns the card for the current mode: why (`source`, `reason`), time left, what is muted, what still reaches the phone, essentials that stay available, and a statement that a mode never grants a permission. `POST /modes/focus` (`intention` up to 200 characters, `minutes` 5–240, default 45) starts a timed manual Focus with one chosen next action; one session at a time. `POST /modes/focus/end` (`reflection` up to 500, optional) ends it and returns to automatic; an expired session is closed the same way and keeps no reflection unless given. `GET /modes/focus` lists the last 10 sessions. `POST /modes/correct` turns an automatic (guessed) mode into Normal until the guess would have ended; a mode the owner chose is refused (400). Sessions live in owner settings (`context-modes.focus`).

### Home routines

Routine templates (`morning`, `focus`, `leave`, `evening`), the devices a source reports and a per-routine plan the owner can edit. The only source today is an example fixture (`source: "fixture"`), so Jarvis is not connected to a real home and every run is a **simulation**: each action ends as `simulated`, `read` (with the value), `unavailable`, `not_run` (security controls such as locks are never run by a routine and need the owner's own approval), `skipped` (taken out by the owner) or `reversed`. A step that names a device uses that device and is `unavailable` when it is away; it never falls back to a different one. State lives in owner settings (`home.routines`: choices and the last 10 runs).

| Method | Path |
|---|---|
| GET | `/home-routines` (templates, devices, notice), `/home-routines/runs`, `/home-routines/{id}/plan` |
| PUT | `/home-routines/{id}/choices` (`removed` action ids, `devices` map of action id to a device that can do it; 400 otherwise) |
| POST | `/home-routines/{id}/run`, `/home-routines/runs/{runId}/reverse` (only simulated reversible actions; once) |

### Working styles (profile presets and suggestions)

Five presets (`planning`, `research`, `writing`, `tutor`, `operator`) are templates for the existing assistant profiles: persona text and memory choices only. They never restrict to files or skills the owner has not picked (`needsSources` says when the owner still has to choose them) and creating one never makes it the default. `POST /profiles/suggest` (`text`, optional `currentProfileId`) returns a suggestion that names the words it matched, compares scope with the current profile (`scopeChange`), lists anything that would widen access (`broadens`) and always needs the owner's confirmation then; it creates and switches nothing. `GET /profiles/{id}/scope` returns a compact summary (skills, documents, memory, language, learning, limits) and notes that conversations already started keep the profile version they began with.

| Method | Path |
|---|---|
| GET | `/profiles/presets`, `/profiles/{id}/scope` |
| POST | `/profiles/presets/{presetId}/create` (400 if a profile with that name exists), `/profiles/suggest` |

### Outcome learning

Owner feedback tied to something Jarvis made (`artifactKind`: `project_update`, `story`, `comparison`, `mission_result`, `trip`, `scenario`, `opportunity`, `suggestion`) with one of `accepted`, `revised`, `dismissed`, `not_useful`, `wrong_assumption`, `too_much_effort`, `successful` and optionally `aspect` `too_long`/`too_short`. Silence is never stored. Three matching complaints in 30 days (or three dismissals of a volunteered kind in 14 days) produce a pending **behavior change**: a style preference or quieting a kind of suggestion, with its reason and a labelled made-up before/after. A change carries no tool, permission or scope field. Rejected or undone changes are never offered again; feedback is not pooled across profiles; a profile with `contributeToLearning` off, `captureSignals` off or `proposeImprovements` off stores or proposes nothing. Applied changes become a short style note for the agent (for that profile only), and the effect is reported as "too early to say" until a week has passed and then as a small-sample comparison, never as causation. State lives in owner settings (`outcomes`).

| Method | Path |
|---|---|
| POST | `/outcomes/feedback` |
| GET | `/outcomes/changes`, `/outcomes/learned` |
| POST | `/outcomes/changes/{id}/apply`, `/reject`, `/undo` |

### Catch-up (what a quiet mode held back)

`GET /modes/catch-up` groups the pushes a mode kept off the phone (and the ones the digest already summed up in the last 24 hours) by notification kind: `approvals`, `reminders`, `problems`, `briefings`, `updates`. `stillHolding` says the mode is still quiet. `PUT /modes/catch-up/domains` (`domain`, `included`) leaves a kind out of the recap or puts it back; approvals and problems cannot be left out (400). A kind left out is not summed up in the digest push (and no push is sent when everything is left out) but its notifications stay in the app. `POST /modes/catch-up/clear` removes what was already summed up; anything still held stays. Preferences live in owner settings (`context-modes.catchup`).
