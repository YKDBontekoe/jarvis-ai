# Flutter client architecture

App root: **`apps/mobile/`** (package `jarvis_mobile`).

## Entry and configuration

- `lib/main.dart` — app bootstrap, routing, theme.
- `lib/api/api_config.dart` — base URL from `--dart-define=JARVIS_API_URL`; native builds default to `http://localhost:5082`, browser builds to their current origin.
- `lib/auth/auth_session.dart` — JWT + refresh in secure storage.

Android emulator: `http://10.0.2.2:5082`. Physical devices need LAN-reachable API and matching CORS.

The same Flutter app is also hosted by the production API at the Jarvis domain
root. It shares the existing account and owner-scoped data. See
[hosted web deployment](../operations/deployment-and-ci.md#hosted-web-client).

## Feature layout

| Path | Screen / concern |
|------|------------------|
| `features/chat/` | Chat transcript, composer, SignalR realtime, approvals, generative UI, browser timeline |
| `features/shell/` | Compact sidebar (Jarvis, Today, WhatsApp), projects and recent chats; “Ask or find” opens intent navigation |
| `features/search/` | Search as you type; explicit submission interprets a natural-language goal with the owner's background model and offers typed workflow actions. Real unread WhatsApp activity supplies starting suggestions. Browse tools keeps every destination accessible without inference |
| `features/home/` | Home briefing widgets |
| `features/projects/` | Projects list, project page (instructions, chats, files, tasks), editor, and the move-to-project sheet; the sidebar lists recent projects |
| `features/review/` | Weekly review screen and mood trend chart |
| `features/memory/` | Memory list/editor, knowledge graph map |
| `features/journal/` | Journal list + summary, entry editor (text, ratings, tags), "Talk about my day" hand-off to chat |
| `features/tasks/` | `tasks_screen.dart`, `task_details_screen.dart`, editors |
| `features/settings/` | Models (Codex/OpenRouter), voice, nested settings hub |
| `features/skills/`, `persona/`, `profiles/`, `learning/` | Owner tuning surfaces |
| `features/channels/` | WhatsApp & Signal |
| `features/whatsapp/` | Sidebar WhatsApp inbox with account switching in the header and All/Unread/Groups filters; separate Choose chats screen for read-along selection; saved previews, unread counts, paginated conversations, reply drafts and Ask Jarvis |
| `features/devices/` | This-device capabilities and telemetry |
| `features/agents/` | Remote agent registry |
| `features/coding/` | Coding runs list |
| `features/usage/` | Usage dashboard |
| `features/voice/` | LiveKit stage, hands-free |

Top-level screens outside `features/`: `conversations_screen.dart`, `reminders_screen.dart`, `files_screen.dart`, `integrations_screen.dart`, `approvals_screen.dart`, `audit_screen.dart`, `condition_watches_screen.dart`, `daily_briefing_screen.dart`.

## API client

Intent navigation uses `GET /navigation` for read-only starting suggestions and `POST /navigation/resolve` with `{ request }` for interpretation. The model chooses a workflow; the application resolves actual owner-scoped resources and returns action choices. Ambiguous names/accounts are shown separately. Selecting a WhatsApp reply action opens that conversation and drafts once using the original request; sending still requires tapping Send. Assistant actions hand the original request to chat and retain its normal tool approvals. Editing or closing the input cancels inference and discards stale responses. The native “Browse tools” list also works when inference is unavailable.

- `lib/api/jarvis_http.dart` — authenticated HTTP wrapper.
- `lib/features/chat/chat_screen_realtime.dart` — SignalR `/hubs/events`.
- `lib/push/firebase_bootstrap.dart` — FCM registration (`PUT /push-devices`).
- `lib/push/notification_actions.dart` — iOS notification buttons. The server sets `aps.category` (`jarvis.reminder`: Done, Snooze 10 min; `jarvis.approval`: Open). `ios/Runner/AppDelegate.swift` registers the categories and keeps the app awake briefly for background buttons; Dart receives the button id through `onMessageOpenedApp` and calls `POST /notifications/{id}/actions`. Android shows no buttons.

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
