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
| `features/shell/` | Sidebar, wide-layout navigation rail |
| `features/home/` | Home briefing widgets |
| `features/projects/` | Projects list, project page (instructions, chats, files, tasks), editor, and the move-to-project sheet; the sidebar lists recent projects |
| `features/review/` | Weekly review screen and mood trend chart |
| `features/memory/` | Memory list/editor, knowledge graph map |
| `features/journal/` | Journal list + summary, entry editor (text, ratings, tags), "Talk about my day" hand-off to chat |
| `features/tasks/` | `tasks_screen.dart`, `task_details_screen.dart`, editors |
| `features/reminders/` | Reminders: state and actions in `reminders_screen.dart`, list and cards in `reminders_list.dart`, editor in `reminder_editor.dart` |
| `features/conversations/` | Conversation history, groups, export |
| `features/files/` | Files, collections, platform download helpers |
| `features/integrations/` | Integrations hub: apps, packs, MCP servers, credentials |
| `features/approvals/`, `audit/` | Approval review, audit log |
| `features/automations/`, `watches/`, `briefing/` | Automations, condition watches, daily briefing |
| `features/notifications/` | Notification details and deep-link routing |
| `features/settings/` | Models (Codex/OpenRouter), voice, nested settings hub |
| `features/skills/`, `persona/`, `profiles/`, `learning/` | Owner tuning surfaces |
| `features/channels/` | WhatsApp & Signal |
| `features/whatsapp/` | Sidebar WhatsApp inbox with account switching in the header and All/Unread/Groups filters; separate Choose chats screen for read-along selection; saved previews, unread counts, paginated conversations, reply drafts and Ask Jarvis |
| `features/devices/` | This-device capabilities and telemetry |
| `features/agents/` | Remote agent registry |
| `features/coding/` | Coding runs list |
| `features/usage/` | Usage dashboard |
| `features/voice/` | LiveKit stage, hands-free |

Every screen lives under `features/<area>/`. The `lib/` root holds only app-wide code: `main.dart`, `theme.dart`, `appearance.dart`, `app_lock.dart`, `error_reporting.dart`, and small helpers (`json_maps.dart`, `http_urls.dart`, `schedule_format.dart`), plus `api/`, `auth/`, `push/` and `ui/`. Put new screens in a feature folder.

## Motion and visual language

- `lib/ui/motion.dart` — `JarvisMotion` tokens: `fast` 150 ms (presses, icon swaps), `base` 220 ms (content changes), `slow` 320 ms (pages), `standard` ease-out curve, travel 8 px, start scale 0.98. Read timings from here instead of hardcoding durations, and use `JarvisMotion.of(context, …)` so reduced motion turns them off.
- `JarvisPageTransitionsBuilder` (fade + grow + small rise) is the pushed-page transition everywhere except iOS, which keeps the native slide for the edge swipe back.
- `lib/ui/jarvis_ui.dart` — `FadeSlideIn` for content that arrives (pass `animate: false` for rows already on screen), `SkeletonList` for loading lists (used by `ListScreenBody`), `afterRouteSettles` to open an editor once a page has finished animating in.
- Chat: only live entries animate in (`_settledEntries` marks loaded history); home and the transcript, and the wide layout's panes, fade through each other.
- Chat quick actions, search and the command palette open `tasks/new`, `reminders/new` and `memory/new` (see `createDestinationSuffix` in `utility_pages.dart`) so the editor is already showing. When it closes the screen calls `onCreateDone`: the chat pops the page (or closes the wide pane) and shows "Reminder set · View"; a cancel just goes back; a failed save stays on the page so nothing typed is lost.
- Search and the palette list quick commands (`features/search/quick_commands.dart`): create commands before anything is typed, and matching "New …" / "Go to …" commands (English and Dutch keywords) above results.
- While an approval waits and its card has scrolled away, an approval dock above the composer jumps back to it. The decision itself stays on the card.
- `JarvisColors.accentGradient` (indigo → violet) is reserved for the primary send action and selection accents; `JarvisColors.scrim` dims behind sheets and dialogs.
- Page and dialog titles use Instrument Serif (from the theme); dense UI stays in Inter. A custom title `TextStyle` without `fontFamily` inherits the serif, so set `fontFamily: 'Inter'` when a title should stay sans (the chat top bar does). Button `textStyle`s replace the theme's, so they need `fontFamily: 'Inter'` too.
- Other shared pieces in `jarvis_ui.dart`: `ToolbarCapsule` (grouped top-bar buttons), `StatusChip` (quiet states such as offline), `EdgeFade` (content dissolves under header and composer), `HeroGlow` (home backdrop), `SwipeActions` (row swipe with haptics; reminders use it for done/snooze).

## Screenshots and layout audit

`test/screenshots/` renders the real app on fixture data with the bundled fonts and real (blurred) shadows. The files have no `_test` suffix, so `flutter test` and CI skip them; run them explicitly:

```sh
cd apps/mobile
flutter test test/screenshots/app_screenshots.dart   # main screens, light + dark → build/screenshots
flutter test test/screenshots/layout_audit.dart      # every utility page at 393pt and 320pt
```

The audit fails on layout overflows and on toolbars squeezed below their height. `debugJarvisHttpAdapter` in `lib/api/api_config.dart` is the hook that serves the whole app from `fixtures.dart`; set `SCREENSHOT_DIR` to write elsewhere.

## API client

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
4. Deep links for notifications: `features/notifications/notification_routing.dart`.

Backend contract: [api-reference.md](../backend/api-reference.md).
