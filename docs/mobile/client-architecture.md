# Flutter client architecture

App root: **`apps/mobile/`** (package `jarvis_mobile`).

## Entry and configuration

- `lib/main.dart` — app bootstrap, routing, theme.
- `lib/api/api_config.dart` — base URL from `--dart-define=JARVIS_API_URL` (default `http://localhost:5082`).
- `lib/auth/auth_session.dart` — JWT + refresh in secure storage.

Android emulator: `http://10.0.2.2:5082`. Physical devices need LAN-reachable API and matching CORS.

## Feature layout

| Path | Screen / concern |
|------|------------------|
| `features/chat/` | Chat transcript, composer, SignalR realtime, approvals, generative UI, browser timeline |
| `features/shell/` | Sidebar, wide-layout navigation rail |
| `features/home/` | Home briefing widgets |
| `features/memory/` | Memory list/editor, knowledge graph map |
| `features/journal/` | Journal list + summary, entry editor (text, ratings, tags), "Talk about my day" hand-off to chat |
| `features/tasks/` | `tasks_screen.dart`, `task_details_screen.dart`, editors |
| `features/settings/` | Models (Codex/OpenRouter), voice, nested settings hub |
| `features/skills/`, `persona/`, `profiles/`, `learning/` | Owner tuning surfaces |
| `features/channels/` | WhatsApp & Signal |
| `features/devices/` | This-device capabilities and telemetry |
| `features/agents/` | Remote agent registry |
| `features/coding/` | Coding runs list |
| `features/usage/` | Usage dashboard |
| `features/voice/` | LiveKit stage, hands-free |

Top-level screens outside `features/`: `conversations_screen.dart`, `reminders_screen.dart`, `files_screen.dart`, `integrations_screen.dart`, `approvals_screen.dart`, `audit_screen.dart`, `condition_watches_screen.dart`, `daily_briefing_screen.dart`.

## API client

- `lib/api/jarvis_http.dart` — authenticated HTTP wrapper.
- `lib/features/chat/chat_screen_realtime.dart` — SignalR `/hubs/events`.
- `lib/push/firebase_bootstrap.dart` — FCM registration (`PUT /push-devices`).

## Generative UI

- `generative_ui.dart`, `generative_ui_card.dart` — native cards (`RenderUi`): choices, forms (including secret token fields), status, and lists. Authorization actions may open a public HTTPS URL.
- MCP setup starts in chat (`OfferMcpSetup`); Settings → Integrations is the encrypted vault.
- Only the latest card is interactive; older cards collapse to receipts.
- Actions: `POST /api/v1/ui-surfaces/{id}/actions`.

## Voice

Uses `livekit_client`; obtains session from `POST /voice/session`. Settings under `features/settings/voice_settings_screen.dart`.

## Tests

From `apps/mobile`:

```sh
flutter test
```

Widget tests live alongside features under `test/` (if present).

## When changing the client

1. Match existing patterns in the nearest feature folder (controller + screen split in chat).
2. Use `jarvis_http` for API calls; do not hardcode owner ids.
3. For new settings sections, extend `features/settings/settings_view.dart` navigation.
4. Deep links for notifications: `notification_routing.dart`.

Backend contract: [api-reference.md](../backend/api-reference.md).
