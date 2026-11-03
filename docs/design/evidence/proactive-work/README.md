# Proactive work review evidence

Captured on 2026-11-03 after rebasing onto `4354014`. All images and the recording use fictional fixture data.

- [Light list](proactive-list-light.png), [dark list](proactive-list-dark.png), [detail](proactive-detail-light.png)
- [320pt detail](detail-320pt.png), [large-text detail](detail-large-text.png)
- [Calendar capacity controls](proactive-calendar-light.png), [trip monitor](proactive-trip-monitor-dark.png)
- [Navigation and pause recording](navigation-and-pause.mp4), [paused result](paused.png)
- [Trip-monitor dialog and cancel recording](trip-monitor-and-cancel.mp4)

The screenshots and recorded pause result were visually inspected. The recording navigates from Everything to
Ongoing work, opens a responsibility and pauses its checks.

## Verification results

| Check | Result |
|---|---|
| Backend unit suite | 2,368 passed |
| Focused `ProactivePersistenceTests` | 7 passed; disposable PostgreSQL, actual HTTP flow and Temporal restart/pause recovery |
| Flutter analysis | No issues |
| Full Flutter suite | 1,176 passed |
| Three screenshot suites | 337 passed, 26 failed |
| Synthetic proactive scenarios | 8/8; explicit owner usefulness remains unknown |
| Profile web build and interaction recording | Passed |

The screenshot failures are in `ui_polish.dart`: 24 shell cases expect no descendant text in `JarvisTabBar`,
but the current shell contains the dock/selected-tab text; two docked-question cases overflow by 26px at
320pt with 3× text. The new proactive screen cases passed. These failures remain unresolved.

The required full-stack `scripts/dev/smoke.sh` check could not validate this checkout because an existing
verification stack occupies its fixed API port 5082 and Temporal ports. That stack was left running. The focused
integration suite instead exercised this code on disposable services and an ephemeral HTTP listener.
The entire integration suite has not passed cleanly: an earlier broad run had Docker lifecycle timeouts.
Python bridge and native iOS packaging were not run; no Python/native implementation changed.

See [feature contracts and first-release boundaries](../../../backend/proactive-work.md), including rollout order
and migration requirements. These checks do not establish real-world intervention usefulness.
