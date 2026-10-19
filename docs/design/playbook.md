# Jarvis design playbook

The single reference for how the Jarvis app looks, moves and reads. Read it before any UI change in `apps/mobile`, and update it in the same change when you add or change a token, component or pattern. Mockups live in [mockups/](mockups/README.md).

Precedence: the owner's own words, then this playbook, then your own taste.

## 1. Principles

1. **Lead with what needs you now.** Every screen opens on its most useful thing: Right now, who is waiting, what is due. Settings and long lists come after.
2. **Every smart card says why and can be put away.** Inferred content has a reason line ("From your calendar") and a way to dismiss it (Not now, Fewer like this, Not useful). Dismissals are remembered server-side (`POST /home/context/dismiss`).
3. **Jarvis never acts silently.** Drafts and quick replies fill a composer and wait for Send. Accepting a routine saves a draft and never switches it on. Sensitive agent actions go through approvals.
4. **One focal element per screen.** Only one dark or saturated surface at a time (the Right now card on Home). Everything else is calm surface on canvas.
5. **Motion has meaning.** Things move to show where they came from, what changed, or that Jarvis is working. Never for decoration.

## 2. Tokens

All colours come from `JarvisColors.of(context)` (`apps/mobile/lib/theme.dart`). Do not write `Color(0x…)` literals in `lib/features/`. The fixed exceptions are the Right now card's moment palettes in `features/home/right_now_card.dart` (the dark "soon" card and the wind-down navy), which are deliberate brand moments.

| Token | Light | Dark | Use |
|-------|-------|------|-----|
| `canvas` | `#f7f7fa` | `#0f0f14` | Page background |
| `surface` | `#ffffff` | `#18181f` | Cards, sheets, the tab bar |
| `surfaceMuted` | `#efeff4` | `#202029` | Secondary buttons, fields, chips |
| `surfaceRaised` | `#e5e5ee` | `#2a2a35` | Pressed or raised surfaces |
| `outline` | `#e6e6ec` | `#26262f` | Hairlines, dividers |
| `outlineStrong` | `#d3d3de` | `#363642` | Borders that must show |
| `ink` | `#16161d` | `#f2f2f7` | Primary text, primary buttons |
| `inkSoft` | `#5a5b68` | `#a9aab8` | Secondary text |
| `muted` | `#8a8b9a` | `#7c7d8d` | Tertiary text, caps labels |
| `onInk` | `#ffffff` | `#0f0f14` | Text on `ink` |
| `accent` | `#5b4ef0` | `#8f86ff` | Links, counts, selection, Jarvis |
| `accentDeep` | `#4338ca` | `#c4c0ff` | Text on `accentSoft` |
| `accentSoft` | `#eeedfe` | `#221f45` | Selected rows, quick-reply chips |
| `rose` | `#b8a9f5` | `#c9bdfb` | Relationship tags (a lilac, despite the name) |
| `warning` / `warningSoft` | `#b45309` / `#fcf3e6` | `#fbbf24` / `#3a2a12` | Overdue, due now, battery |
| `danger` / `dangerSoft` | `#dc2626` / `#fcebeb` | `#f87171` / `#3a1c1c` | Destructive, errors |
| `info` / `infoSoft` | `#2563eb` / `#e7eefd` | `#7aa2ff` / `#1a2347` | Tasks, neutral info |

`success` is the accent on purpose: done is calm, not green. `violet` and `sky` are only for the orb and name gradient.

**Radii.** `JarvisRadii` gives sm 8, md 14, lg 20 and xl 26. `tileRadius` (`features/tiles/tile_card.dart`) is 24 for Home cards. Chat rows and chips use 18 and 999 (a pill). Hero cards use 28.

**Shadows.** `JarvisShadows.soft(brightness)` for cards on canvas. Avoid borders and shadows on the same element.

## 3. Type

Geist for everything (`JarvisType`, `theme.dart`).

| Role | Style |
|------|-------|
| Page title | `JarvisType.displayOf` 34 w700, letter spacing −1.2 (Chats, Settings) |
| Home greeting | 30 w700, letter spacing −1 (26 below 360pt wide). Clamped to 1.3× text scale |
| Card title / section | 20 w600, letter spacing −0.5 ("Needs you", "Jarvis noticed") |
| Row title | 15.5–16 w600, letter spacing −0.25 |
| Body | 15 / 1.4; 14.5 in previews |
| Meta | 13–12.5, `inkSoft` or `muted` |
| Caps label | `JarvisType.capsOf`: 11 w600, letter spacing +1, uppercase ("WAITING ON YOU") |
| Big numerals | `JarvisType.clock`, or 30–34 w700 with `FontFeature.tabularFigures()` |

- Times and counts always use tabular figures.
- Give every `TextButton` / `FilledButton` `textStyle` an explicit `fontFamily: 'Geist'`. Without it, screenshots render black boxes.
- Long titles wrap at word boundaries. If one word cannot fit, shrink with `FittedBox(fit: BoxFit.scaleDown)` rather than break it.

## 4. Layout

- Content max width is 640 on Home and Chats (`ConstrainedBox`), and 808 for shell notices. Side padding is 16.
- Minimum tap target is 44×44.
- **Everything must work at 320pt wide and at 2× text** (`test/screenshots/ui_polish.dart`) without overflow:
  - button groups that may not fit use `Wrap`, not `Row`;
  - text next to fixed-width controls goes in `Expanded`/`Flexible` with `maxLines` and an ellipsis;
  - tags that sit beside a title are `Flexible` and only as wide as their label (`ChatTag`).
- **Text scaling:** body text follows the system size fully. Only display headings clamp (greeting 1.3×, day line 1.6×, 3 lines).
- **Nothing overflows at 3×.** `layout_audit.dart` (`large-text`, 320pt wide) and `ui_polish.dart` fail on any overflow at the largest accessibility size. In a `Row`, text that can grow is `Flexible` or `Expanded` and ellipsizes; a title next to an action uses `Wrap` so the action can drop under it; a line with a button stacks when `textScalerOf(context).scale(1) >= 1.5`; every `DropdownButtonFormField` sets `isExpanded: true`.
- Section rhythm on Home is 20–28 between sections and 8–14 inside a card.
- Wide layout (≥ 900): a navigation rail, a list pane, and a detail pane. A WhatsApp chat linked to a person adds the About panel (320 wide) when the pane is ≥ 860 (`PersonPanel.fits`).

## 5. Motion

Tokens are in `ui/motion.dart`. Durations: `JarvisMotion.fast` 150, `base` 220, `slow` 380. Curves: `standard` (ease out), `emphasized`, `exit`. Springs are in `ui/effects.dart`: `JarvisSprings.soft` (sliding indicators) and `pop` (badges).

| Need | Use |
|------|-----|
| Section or row entrance | `FadeSlideIn(index:)`, first load only, staggered by index |
| Content that changes in place | `MotionSwitcher(resize: true)` keyed by the content's id |
| Height changes | `MotionSize` |
| A badge or count appearing | `PopIn` |
| Something just arrived | One `Sheen` pass, plus a fading `accentSoft` wash (Chats rows) |
| Changing numbers | `RollingNumber` |
| Anything tappable | `PressFeedback(scale: .95–.985)` |
| Swipe actions | `SwipeActions` (spring snap back) |
| Page or tab changes | `PageSwitcher` |
| Jarvis busy | `TypingDots`, `JarvisOrb(thinking:)` |

Rules:
- At most two things move at once. Nothing takes longer than 380 ms except live loops.
- Loops (typing dots, the breathing orb) run only while Jarvis is actually busy.
- `JarvisMotion.of(context, d)` returns zero when the system asks for reduced motion. Every custom animation checks `JarvisMotion.reduced(context)` and becomes instant or a 150 ms fade.
- A resolved row slides out (`exit`, 150) before its space closes (`MotionSize`, 220).
- Light haptics on actions (`HapticFeedback.lightImpact` / `selectionClick`), medium on long-press.
- In tests, use reduced motion or `settle()` loops; never `pumpAndSettle` on screens with live loops.

## 6. Components

Reuse before you build. All of these live under `apps/mobile/lib/`.

| Component | File | For |
|-----------|------|-----|
| `SurfaceCard`, `SectionHeader`, `GroupedSection` | `ui/jarvis_ui.dart` | Cards and grouped lists |
| `EmptyState`, `ErrorState`, `LoadingState`, `SkeletonList`, `InlineNotice` | `ui/jarvis_ui.dart` | Empty, error, loading and notice states |
| `StatusPill`, `StatusChip`, `SegmentedPills`, `HeaderAction`, `CircleIconButton`, `ToolbarCapsule` | `ui/jarvis_ui.dart` | Controls |
| `EdgeFade`, `HeroGlow`, `ContentWidth`, `PageTitle` | `ui/jarvis_ui.dart` | Layout helpers |
| `showJarvisConfirm`, `showJarvisDialog` | `ui/jarvis_ui.dart` | Confirmation (never the platform dialog) |
| `JarvisOrb` | `ui/jarvis_orb.dart` | Jarvis's presence; `ProfileOrb` in `features/chats/chat_row.dart` tints it per assistant profile |
| `ChatRow`, `ChatRowAvatar`, `ChatTag`, `UnreadCount` | `features/chats/chat_row.dart` | Chat lists, tags, count pills |
| `QuickReplies` | `features/chats/quick_replies.dart` | Up to three suggested replies; tapping fills the composer |
| `TypingDots` | `ui/typing_dots.dart` | Jarvis is writing |
| `RightNowCard`, `showWhySheet` | `features/home/right_now_card.dart` | The one leading card on Home |
| `NeedsYouCard` | `features/home/needs_you_card.dart` | Things waiting on the owner |
| `NoticedCard`, `EveningCard`, `DayOneCard`, `BriefCard`, `AskBar` | `features/home/` | Home sections |
| `HeroCard`, `FeatureRow`, `PillAction`, `GroupLabel`, `SoftButton`, `InkButton` | `ui/feature_kit.dart` | The vocabulary for feature screens. One `HeroCard` (ink card, caps label, headline, optional segmented progress, footer) leads the screen; everything under it is `FeatureRow`s (icon badge, caps caption, title, detail, trailing, pill actions; entrance stagger; stacks under the text at large sizes) grouped by a `GroupLabel` (caps, optional count). Actions are `PillAction` (ink when primary, tonal otherwise, `compact` for several per row, `onDark` on a hero). Never bare `TextButton`s in cards. |
| `PersonPanel` | `features/whatsapp/person_panel.dart` | About a person, beside a chat on wide screens |
| `UiSurfaceCard`, `DockedSurfaceBar` | `features/chat/generative_ui.dart` | Cards Jarvis renders with RenderUi. An open question docks in the composer's place (`pinned`); `DockedSurfaceBar` keeps it in view while the owner types instead |

## 7. Patterns

- **Right now card.** One slot at the top of Home that morphs between `soon` (countdown), `brief`, `battery` and `wind_down` (tomorrow). It has colours per moment, at most two actions, a reason line, and a Why sheet with the facts plus Not now / Fewer cards like this. It replaces Next up only when the server sends a hero (`/home/context`).
- **Needs you.**
  - Order: approvals, then due reminders, then people waiting, then tasks, then catch-ups.
  - People are ranked by `waitingOnYou` (`features/home/home_insights.dart`): close people first, then how far past your usual reply time, then who has waited longest. The Ask bar and the Chats "Waiting on you" section use the same ranking.
  - The card shows 3 rows, picking the first of each kind before any second one ("Show N more").
  - The first one-to-one chat gets quick replies.
  - An empty card collapses to "You're all caught up".
- **Jarvis noticed.** One insight at a time (swipe or dots), with evidence and an optional confidence bar. Its actions: Set it up (draft routine), Say hi (opens the chat), Plan a message, and Not useful.
- **Evening (from 18:00) and day one.**
  - The evening card (your day plus tomorrow) replaces the brief.
  - The day-one card (getting to know you, what each connection unlocks, and the brief time) replaces Get started while fewer than two of calendar, WhatsApp and People are connected.
- **Chats.**
  - Filters are For you (Waiting on you, With Jarvis with reminders bundled, Quiet with busy groups folded), All (by day, pinned headers), Unread, Jarvis, WhatsApp and Groups.
  - Rows are two lines plus an optional context line. Relationship tags come from People, and drift ("Quiet lately · you usually talk weekly") shows only on read chats.
  - Hold a chat to preview it: a blurred backdrop, the last message, the catch-up, quick replies and four actions.
  - Press ⌘K / Ctrl+K to search.
- **Questions from Jarvis (docked card).** When Jarvis asks a question with a card (choice or form), it docks above the keyboard in the composer's place, ChatGPT-style:
  - the keyboard closes when a new question arrives, and the transcript stays scrolled to the latest message;
  - options are equal rows in one rounded `surfaceMuted` group: number badge (hidden from 1.5× text), title, optional subtitle, ↗. One tap answers; there is no filled "first" option;
  - the pencil in the header ("Type an answer instead") folds the options into a `DockedSurfaceBar` above the composer and focuses it; Options brings them back;
  - while a turn is still running, the composer stays under the card so Stop is in reach;
  - an answered card collapses to a one-line "Answered" receipt in the transcript;
  - tapping the transcript closes the keyboard.
- **People everywhere.** A chat linked to a person shows the relationship in Chats rows, Needs you, the hold preview, the chat header (instead of your own number) and the About panel.
- **Navigation (approved design, not built yet).** See [mockups/home-header-nav.html](mockups/home-header-nav.html):
  - two floating glass pieces, a capsule for Home / Chats / Everything / You and a separate orb button;
  - the open tab is a solid pill with its name;
  - one contextual strip above the dock, priority: Jarvis writing, then Jarvis replied, then the top Needs you item;
  - the capsule folds to the current tab while scrolling;
  - hold the orb to talk;
  - Chats shows the waiting count, Home an amber dot for approvals or due reminders.
  Today's bar is `features/shell/jarvis_tab_bar.dart`.

## 8. Copy

- Write from the owner's side: "Rosa is waiting · 14 h", not "unanswered inbound".
- Buttons say what happens: Reply, Set it up, Done, +10 min, Not now. Toasts confirm it: "Saved as a draft routine. Turn it on in Routines."
- Reason lines are plain facts: "From your calendar", "Seen 4 of your last 5 Fridays".
- No apologies and no exclamation marks in UI text. Errors say what failed and what to do: "Could not save the brief time".
- Short hints that fit beside buttons: "You reply in ~18 min".

## 9. Accessibility

- Every custom tappable has a `Semantics` label (and `button: true`), and decorative children use `excludeSemantics`.
- Give buttons with a long visible text and subtitle a short label too. The web recorder (`tool/record_web.mjs`) taps by accessible name, so use unique names.
- Check contrast in both themes. Never put information in colour alone; pair it with text or an icon.
- 44pt targets, and the text-scale rules in §4.

## 10. Workflow

1. **Mockup first** for a new screen or a visible redesign. Write an HTML board in `docs/design/mockups/`, publish it as an Artifact for review, and record the decision in `mockups/README.md`.
2. **Build** with tokens and components from this playbook. Add new tokens to `theme.dart`, not inline.
3. **Verify** (see `AGENTS.md`):
   - `flutter analyze` and `flutter test`.
   - The screenshot suites: `test/screenshots/app_screenshots.dart` (add a case for every new state), `ui_polish.dart` (320–430pt, 1× and 2× text, light and dark) and `layout_audit.dart`. Look at the PNGs in `apps/mobile/build/screenshots/`.
   - A web recording: `flutter build web -t tool/ui_preview.dart --profile`, then `node tool/record_web.mjs --steps "A;B;C"` (separate steps with `;`).
   - Send the screenshots and video.
4. **Format only the files you touched** (`dart format <files>`), never all of `lib/`.
5. **Update this playbook** when you add a token, component or pattern.

## 11. Open items

- The navigation redesign and the compact Home header are approved as mockups and not built yet.
- A "Leave now" travel estimate needs locations on calendar events and reminders.
- The iOS widget's Swift (`apps/mobile/ios/JarvisWidget/`) has not been compiled yet. See its README.
