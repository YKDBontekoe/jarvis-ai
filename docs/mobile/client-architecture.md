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
| `features/shell/` | Tab bar (`jarvis_tab_bar.dart`), wide-layout rail, the custom tab icons (`tab_icons.dart`), and `utility_pages.dart`, which maps a destination name to its page |
| `features/home/` | Home: the clock header (`clock_header.dart`, `next_up.dart`), the tile grid screen (`jarvis_home.dart`), the first-run checklist |
| `features/tiles/` | The tile system: registry of every feature, sizes, grid packing, saved layout, live data loaders, the grid with edit mode |
| `features/chats/` | Chats: one list of Jarvis conversations and linked WhatsApp chats (`chat_list.dart`, `chats_screen.dart`) |
| `features/everything/` | Everything: every feature by category, with a size preview and pin-to-Home sheet |
| `features/projects/` | Projects list, project page (instructions, chats, files, tasks), editor, and the move-to-project sheet; reached from the Projects tile |
| `features/review/` | Weekly review screen and mood trend chart |
| `features/memory/` | Memory list/editor, knowledge graph map |
| `features/journal/` | Journal list + summary, entry editor (text, ratings, tags), "Talk about my day" hand-off to chat |
| `features/tasks/` | `tasks_screen.dart`, `task_details_screen.dart`, editors |
| `features/reminders/` | Reminders: state and actions in `reminders_screen.dart`, list and cards in `reminders_list.dart`, editor in `reminder_editor.dart` |
| `features/conversations/` | Conversation history, groups, export |
| `features/files/` | Files, collections, platform download helpers |
| `features/integrations/` | Integrations hub: apps, packs, MCP servers, credentials. `app_catalog_screen.dart` searches the app directory, installs in one tap, takes a pasted address, and asks for every key an app needs (`AppKeysDialog`) |
| `features/approvals/`, `audit/` | Approval review, audit log |
| `features/automations/`, `watches/`, `briefing/` | Automations, condition watches, daily briefing |
| `features/notifications/` | Notification details and deep-link routing |
| `features/settings/` | Models (Codex/OpenRouter), voice, nested settings hub |
| `features/skills/`, `persona/`, `profiles/`, `learning/` | Owner tuning surfaces |
| `features/channels/` | WhatsApp & Signal |
| `features/whatsapp/` | WhatsApp page (Everything → WhatsApp) with account switching in the header and All/Unread/Groups filters; separate Choose chats screen for read-along selection; saved previews, unread counts, paginated conversations, reply drafts and Ask Jarvis |
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
- Everything is Geist (bundled in `assets/fonts`, weights 300–700). Page and dialog titles use `JarvisType.display` (semibold, tight tracking); the Home clock uses `JarvisType.clock` (light, 64 pt, and it does not follow the device text size). A custom `TextStyle` that names no `fontFamily` inherits Geist from the theme, but button `textStyle`s replace the theme's, so they need `fontFamily: 'Geist'`.
- Palette is Iris: cool greys plus one indigo accent (`JarvisColors.accent`). The accent marks only what needs the person (unread, due, the primary action). `success` is the accent too; `warning` and `danger` stay for real problems. Do not add new colours for decoration.
- Other shared pieces in `jarvis_ui.dart`: `ToolbarCapsule` (grouped top-bar buttons), `StatusChip` (quiet states such as offline), `EdgeFade` (content dissolves under header and composer), `HeroGlow` (home backdrop), `SwipeActions` (row swipe with haptics; reminders use it for done/snooze).

## Navigation, Home and tiles

`ChatScreen` (`features/chat/`) is the app shell. There is no drawer.

- **Phone**: a bottom tab bar with Home, Chats, the Jarvis orb, Everything and You. The orb opens the open conversation instead of switching tabs. A conversation covers the tabs (`_inChat`); its top bar has Back, and the bar is hidden while it shows. Voice is full-screen too.
- **Wide (>= 840 px)**: the same destinations as a rail on the left. Pages opened from a tile or the rail replace the content area (`_utilityPane`); pages opened from inside a page stack so Back works.
- **You** is Settings. Connection and sign-in problems show as a notice above whichever tab is open.
- `_showHome` still means "show the empty welcome instead of the transcript" inside a conversation. Whether a conversation is on screen at all is `_inChat`. Code that brings someone to a conversation (`_presentChat`, `_presentConversation`) sets it; background work such as the outbox must not.

**Home** (`JarvisHome`) is the clock header (the next calendar event or reminder from `/api/v1/home`, or the time when nothing is coming up), pending approvals, the setup cards (Codex sign-in, get started) and the tile grid. Long-press a tile or tap the sliders to edit: drag to reorder, the corner button changes size, the cross unpins.

**Tiles** are the unit of Home and Everything:

- `tile_registry.dart` lists every feature as a `TileSpec`: id, name, icon, category, supported sizes (`icon` 1x1, `strip` 2x1, `square` 2x2, `wide` 4x2, `large` 4x4), the destination it opens, and an optional loader that returns `TileData` (a number, a subtitle, rows, an attention flag). A loader that throws, or an endpoint that is missing, shows the tile's `fallback` text instead.
- To add a feature to Home: add a `TileSpec` (the test `every tile opens a page the app can show` checks the destination exists in `utilityPageFor`). Chats and Settings have no loader because they read live app state.
- The grid is four columns of square cells. `packTiles` places tiles in order into the first free spot, so a small tile fills a gap beside a tall one.
- The layout is saved on the device (`TileLayoutController`, key `home.tiles.v1`), not on the account, so each device has its own. Unknown ids and unsupported sizes are repaired when it loads.
- Tiles keep their grid size, so their text grows at most 15% with the device text size.
- **Live tiles**: `TileData` can also carry `progress` (ring), `bars` (7-day chart), `timeline` (day strip with a "now" marker), `countdownTo` (ticks with the clock) and `actions`. Visuals live in `tile_visuals.dart`; the waveform on Chats shows while Jarvis is replying and respects reduced motion. Tiles show skeletons while loading and refresh every minute and on return to Home.
- **Quick actions** (`tile_actions.dart`): reminders Done / +10 min, habits Check in / Undo, approvals Decline. `applyTileAction` updates the tile at once, then `runTileAction` calls the API and Home reloads; a failure restores the tile and shows a snackbar. Approvals can only be declined or reviewed on a tile; approving always happens on the approvals page.
- Long-press a tile for a menu (resize, edit Home, remove). In edit mode drag to reorder. Home waits for the saved layout (`TileLayoutController.ready`) before drawing the grid.

**Chats** merges the Jarvis conversation list the shell already loads with the chats of every linked WhatsApp account (`/api/v1/channels` → `/chats`). WhatsApp rows load the same profile picture as the open chat (`GET …/chats/open/picture`) and fall back to initials when WhatsApp has none. WhatsApp unread counts drive the dot on the Chats tab and the Chats tile. Signal has no chat-list endpoint, so it is not in this list; manage it under Channels. Jarvis conversations have no preview text yet because `GET /conversations` returns none.

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
