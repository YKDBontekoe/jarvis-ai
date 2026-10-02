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
| GET | `/conversations` | List, pinned first then most recent (includes bound profile name and `pinned`) |
| GET | `/conversations/{id}` | Messages + responding flag + profile |
| PATCH | `/conversations/{id}` | Rename (`title`) and/or pin (`pinned`); 404 for task-backed chats |
| PUT | `/conversations/{id}/profile` | Switch bound profile snapshot (409 if scope changes without `confirm`) |
| DELETE | `/conversations/{id}` | Delete chat (not task-backed) |
| POST | `/conversations/{id}/messages` | Send user message (starts agent turn). Optional `imageFileIds` (up to 4 of the owner's JPEG/PNG/WebP files, 8 MB each) are shown to the model in that turn only; stored history keeps a note instead of the image |
| POST | `/conversations/{id}/regenerate` | Answer the last user message again, replacing the last reply. 409 while approvals are open or when that reply used tools (so actions are never repeated) |
| POST | `/conversations/{id}/cancel` | Cancel in-flight run |
| GET | `/approvals` | Pending tool approvals |
| POST | `/approvals/{id}/decision` | Approve or decline |

## Automation

| Area | Paths |
|------|-------|
| Reminders | `GET/POST /reminders`, `GET/DELETE /reminders/{id}`, `POST /reminders/{id}/snooze` (`minutes` or `until`), `POST /reminders/{id}/complete` (`conversationId` on each reminder). A place reminder posts `place` (`name`, `latitude`, `longitude`, `radiusMeters` 50–5000, `trigger` `arrive`/`leave`, `repeats`) instead of `dueAt` |
| Owner automations | `GET/POST /automations`, enable/run/history (`conversationId` and `lastRun` on each rule; approvals go through the shared `/approvals` inbox; see [automations.md](../automations.md)) |
| Condition watches | `GET/POST /watches`, `GET/DELETE /watches/{id}` |
| Tasks | `GET/POST /tasks`, `GET /tasks/{id}`, `GET /tasks/{id}/messages`, cancel endpoints |
| Daily briefing | `GET/PUT /briefings/daily` (see `AutomationEndpoints`) |
| Projects | `GET/POST /projects`, `GET/PUT/DELETE /projects/{id}` (details include its chats, files and tasks; delete keeps them and only clears the project), `PUT /conversations/{id}/project`, `PUT /files/{id}/project`, `PUT /tasks/{id}/project` (`{ projectId }`, null takes it out). `POST /conversations` and `POST /tasks` accept `projectId`; conversation DTOs carry `projectId`. A task belongs to a project through its own conversation |
| Weekly review | `GET /reviews/weekly?weeks=8` (settings, mood trend, recent reviews), `GET /reviews/weekly/{id}`, `PUT /reviews/weekly/settings` (`enabled`, `localTime`, `timeZoneId`), `POST /reviews/weekly/generate` (current week, no notification) |

## Memory and learning

| Method | Path |
|--------|------|
| GET/POST | `/memory`, `/memory/search`, `/memory/{id}` |
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
| POST | `/expenses` (body: `amount` > 0, optional `currency` ISO 4217, `merchant` ≤ 80, `category`, `note` ≤ 200, `spentOn`, `receiptFileId`) |
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

Owner-scoped (`people` table). Names are unique per owner ignoring case and accents. `daysUntilBirthday`, `daysSinceContact` and `contactDue` use the daily-briefing time zone (UTC fallback). A new person links to the graph entity of the same name. Audit events (`people` tool) carry the person id only.

Knowledge graph read/update endpoints are split between `KnowledgeGraphEndpoints` and `PersonalAssistantEndpoints` (`/graph/...`).


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
| GET | `/files`, `/files/search` |
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
| `/integrations/connections` | Per-turn MCP connection status (tool counts) |
| `/integrations/packs`, `/integrations/oauth/*` | Guided packs and OAuth |
| `/mcp-servers`, `/mcp-controls/{name}` | Owner MCP registry |

## Channels

`/channels` — WhatsApp/Signal configuration, threads, test send, QR linking (`POST /channels/link`, `GET /channels/link/{linkId}`, `GET /channels/providers`), Signal status, WhatsApp Cloud webhooks.

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
| GET `/home` | Home briefing payload |
| POST `/devices/telemetry` | Battery/location snapshots; a location also checks pending place reminders |
| POST `/devices/invoke/{id}/result` | Device capability callback |
| GET `/coding/runs`, `/coding/runs/{id}` | Coding task history |

## Notifications, push, audit

| Path | Purpose |
|------|---------|
| GET `/notifications` | In-app notifications |
| POST `/notifications/{id}/read` | |
| PUT/DELETE `/push-devices` | FCM registration |
| GET `/audit` | Append-only audit events |

## Usage

`GET /usage` — token and activity aggregates (see `UsageEndpoints`).

## Remote agents (A2A management)

Under `/api/v1/agents` and `/api/v1/a2a-tokens` — registry and inbound token hashes.

## Implementation map

Each route group is implemented in `src/Jarvis.Api/Endpoints/*.cs` and registered in `Program.cs`. When adding routes, prefer existing groups and `ICurrentUser` for owner checks.
