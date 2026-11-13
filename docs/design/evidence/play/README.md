# Play: evidence

Rendered from the screenshot fixtures (`gameBoard()` in `apps/mobile/test/screenshots/fixtures.dart`).

- `40-play-light.png`: the score hero, two open bets with confidence, deadline and reward, and a bet the owner won.
- `40-play-quests-dark.png`: the week's quests: the boss, a person, a habit with progress, and a finished journal quest.
- `40-play-ghost-light.png`: the race against the ghost (you in accent, the ghost in a neutral tone).
- `play-flow.mp4`: Everything → search "play" → open Play → Call it off → Keep the bet → skip a quest.

Regenerate: `cd apps/mobile && flutter test --no-pub test/screenshots/app_screenshots.dart --plain-name play`, and for the video
`flutter build web -t tool/ui_preview.dart --profile` then
`node tool/record_web.mjs --steps "Everything;type:play;Beat Jarvis;Open Play;Call it off;Keep the bet;scroll:700;Not this one;scroll:900"`.
