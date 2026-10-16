---
name: jarvis-design
description: Use for any UI, design, layout, animation, theming or screenshot work in apps/mobile (the Flutter app), such as new screens, cards, navigation, motion or mockups. Loads the Jarvis design playbook and the UI verification steps.
---

# Jarvis design

1. Read `docs/design/playbook.md` before changing UI. It holds the tokens, type, layout, motion, components, patterns, copy and accessibility rules.
2. New screen or visible redesign: make a mockup first in `docs/design/mockups/` and get it reviewed (see that folder's README) before building.
3. Reuse components from the playbook's catalog. Colours come from `JarvisColors`, motion from `JarvisMotion` / `JarvisSprings`; no inline colour literals in `lib/features/`.
4. Check every new surface at 320pt and 2× text, in light and dark, with reduced motion.
5. Verify as the playbook's Workflow section says: `flutter analyze`, `flutter test`, the three screenshot suites (add a case per new state), a web recording, and send the evidence.
6. When you add or change a token, component or pattern, update the playbook in the same change.
