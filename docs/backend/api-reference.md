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
| POST | `/conversations/{id}/summary` | Read-only recap: `summary`, `keyPoints`, `actionItems`, `messageCount`. Nothing is stored. 409 when the chat has fewer than 2 messages, 503 when the model is unavailable |
| GET | `/approvals` | Pending tool approvals |
| POST | `/approvals/{id}/decision` | Approve or decline |

## Automation

| Area | Paths |
|------|-------|
| Reminders | `GET/POST /reminders`, `GET/DELETE /reminders/{id}`, `POST /reminders/{id}/snooze` (`minutes` or `until`), `POST /reminders/{id}/complete` (`conversationId` on each reminder) |
| Owner automations | `GET/POST /automations`, enable/run/history (`conversationId` and `lastRun` on each rule; approvals go through the shared `/approvals` inbox; see [automations.md](../automations.md)) |
| Condition watches | `GET/POST /watches`, `GET/DELETE /watches/{id}` |
| Tasks | `GET/POST /tasks`, `GET /tasks/{id}`, `GET /tasks/{id}/messages`, cancel endpoints |
| Daily briefing | `GET/PUT /briefings/daily` (see `AutomationEndpoints`) |
| Weekly review | `GET /reviews/weekly?weeks=8` (settings, mood trend, recent reviews), `GET /reviews/weekly/{id}`, `PUT /reviews/weekly/settings` (`enabled`, `localTime`, `timeZoneId`), `POST /reviews/weekly/generate` (current week, no notification) |

## Memory and learning

| Method | Path |
|--------|------|
| GET/POST | `/memory`, `/memory/search`, `/memory/{id}` |
| PUT/DELETE | `/memory/{id}` |
| GET/PUT | `/settings/learning` |
| GET | `/learning/status` |
| POST | `/learning/run`, `/learning/dream` |

## Journal

| Method | Path |
|--------|------|
| GET/POST | `/journal` (`from`, `to`, `limit` query filters on GET) |
| GET/PUT/DELETE | `/journal/{id}` |
| GET | `/journal/summary?days=30` (streak, averages, per-day series) |

Body: `entryDate`, `content` (≤ 6,000), `highlights`, `gratitude` (≤ 1,000 each), `rating` 1–10, `mood`/`energy`/`stress` 1–5, `tags` (≤ 10), optional `source` (`written`, `voice`, `chat`). At least text or one rating is required. "Today" uses the owner's daily-briefing time zone (UTC fallback). See [memory-knowledge-learning.md](memory-knowledge-learning.md#journal).

Knowledge graph read/update endpoints are split between `KnowledgeGraphEndpoints` and `PersonalAssistantEndpoints` (`/graph/...`).

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
| POST `/devices/telemetry` | Battery/location snapshots |
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
