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
| POST | `/planner/today/items` (`title` ≤ 200, `minutes` 5–480, default 30) |
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
