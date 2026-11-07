# Improvements ledger

A numbered list of what was changed in the QA-and-improvement effort, so each item can be checked. An item is listed only when it shipped in a commit with a test or an end-to-end check. The earlier QA audit fixes live in the commit messages of `b08af3e`, `1f03b72`, `a92a902`, `a7d387d` and `8f990be`; they are not repeated here.

**Count so far: 56 improvements in this ledger. The goal of 100 has not been reached.**

## Images go to the model as images

1. A photo sent with a message reaches the model as pixels (already true) and now stays visible on follow-ups: the two newest photos of the last six messages are shown again, so "what was the total?" works without re-sending. (`ImageSelection`)
2. Image files attached to a chat as a source reach the model as images, not only as extracted text.
3. A new photo wins over older ones, so an old photo never crowds out the one being asked about.
4. Each image is captioned (sent now / attached source / earlier photo) so the model can say which one it means.
5. File names in captions are flattened to one line and capped, so a name cannot pose as an instruction.
6. Image-only uploads are indexed with a description plus the text in the image, so a photo without any text is searchable ("orange sky"). (`ImageIndexText`)
7. A receipt question stays read-only: the expense guidance says to answer and offer to log it, never to log unasked. Found on the real model.
8. A photo shown again for reference is never logged a second time.
9. Real-model vision flow with fixture images (`real_vision_flow.mjs`): receipt reading, two-receipt comparison, injection image reported as text and not obeyed, whiteboard to reminders.
10. Fake-model flow for the image plan (`image_input_flow.mjs`) so CI can check it without a model.

## Photos from WhatsApp and Signal

11. A photo sent to Jarvis on WhatsApp is downloaded and given to the assistant as an image.
12. The same on Signal.
13. Media downloads are capped at 8 MB, need an https URL that is not an IP address or loopback, and use the access token only for the configured host.
14. Media that cannot be read gets an honest reply instead of silence.
15. Photo-only messages get default text so the turn is not empty.
16. Additive migration `AddChannelMessageMedia` (nullable `media_json`).

## Documents

17. Word (.docx) files are read, indexed and searchable.
18. PowerPoint (.pptx) files, slides in numeric order, speaker notes left out.
19. Zip bomb and entry-count limits and DTD/XXE protection on those files.
20. A corrupt Office file is marked failed at once instead of retrying forever as "processing".
21. TSV, ICS, YAML, SRT and VTT files can be uploaded and indexed.
22. The mobile file picker accepts the same types the server does.

## Assistant tools

23. Create a goal from chat (`CreateGoal`), with a date check that refuses dates it cannot read.
24. Pause, resume, achieve, archive or reword a goal from chat (`UpdateGoal`), refusing unknown states and other owners' goals.
25. Set the home time zone from chat (`SetHomeTimeZone`), refusing zones that do not exist.

## Security and transport

26. `X-Content-Type-Options: nosniff` on API responses.
27. `Referrer-Policy: same-origin`.
28. `Permissions-Policy` that turns off camera, microphone and geolocation for the API origin.
29. `Cache-Control: no-store` on `/api`, `/hubs` and `/a2a` unless a handler set its own caching.
30. The Kestrel `Server` header is removed.
31. `Strict-Transport-Security` added in the Caddyfile.

## Test infrastructure

32. Fake WhatsApp Graph media and Signal attachments in `fake_channels.mjs`.
33. `channels_flow.mjs` covers photos on both channels (16 checks).
34. `document_upload_flow.mjs` with fixture documents.
35. The fake Codex answers image-count questions for the latest user message only.
36. Fixture images with a README under `tests/e2e/fixtures/vision/`.
37. Unit tests for all of the above (image plan, index text, security headers, channel media, Office text, goals, preferences).

## Second batch: more things the assistant can do

38. Rename the current chat on request (`RenameThisChat`): control characters stripped, 80-character cap, only this chat.
39. Pin or unpin the current chat on request (`PinThisChat`). Neither tool exists in background tasks.
40. Read the calendar on request (`GetCalendarEvents`): ordered, in the owner's home zone, titles flattened to one line, look-ahead kept between 1 and 31 days. The assistant could not answer "what is on my calendar?" at all before.
41. With no calendar link it says so and how to connect one, and is told not to guess.
42. Archive or restore a habit by name (`ArchiveHabit`): history kept, ambiguous names ask which one, audit log records ids not names.

## Third batch: deleting, always with approval

43. Delete a goal and its check-ins from chat (`DeleteGoal`), after approval. Pausing or archiving stays the gentler `UpdateGoal`.
44. Delete a habit and its check-ins from chat (`DeleteHabit`), after approval, by exact name only; the audit log records the id, not the name.
45. Delete a project from chat (`DeleteProject`), after approval; its chats, files and tasks stay.
46. Delete a file from chat (`DeleteFile`), after approval; only offered when the message talks about deleting a file, and never in background tasks.
47. Each of the four is owner-scoped and the approval wrapper is checked in tests.

## Fourth batch: found by running the old flows against a busy database

48. A story outline takes turns between kinds, newest first, so a busy week of tasks and decisions no longer pushes every journal moment out of a recap. Found by `story_flow` against a database with many earlier runs; unit test added.
49. `meeting_flow` and `story_flow` no longer break on leftovers from earlier runs (they matched the wrong, older record, and assumed the outline is never capped).

## Fifth batch: bigger features

50. Biweekly ("every other week") reminders, on one or several weekdays. The first occurrence sets which weeks count, so no schema change or migration was needed; the assistant's reminder tool accepts `biweekly`. Verified through the API and with unit tests for single-day, multi-day, end-date and first-occurrence cases.
51. The app's reminder editor offers "Every 2 weeks" with the weekday chips, and repeat labels read "Every other Mon, Fri".
52. Plain lists (shopping, packing): service stored in owner settings (no migration), owner-scoped, with duplicate skipping, length and count limits, and exact-then-single-partial matching. Unit tests (9).
53. `/api/v1/lists` endpoints (list, add items, tick, clear ticked, delete) and `tests/e2e/lists_flow.mjs`, which passes against the running stack.
54. Assistant tools `GetLists`, `AddToList`, `TickListItems`, `ClearTickedListItems`, offered when a message talks about a shopping, grocery or packing list (English and Dutch).
55. A Lists screen in the app (tick, add, clear ticked, delete with confirm, empty state), with widget tests and a screenshot case.
56. A Lists tile for Home, a quick command and feature-search keywords so the screen is easy to reach.

Seen but left alone: Home lists the five oldest-due promises, so with five or more overdue promises one due today is not on Home. That may be intended (overdue first), so it is a question for the owner, not a fix.

## Gaps found but not built

Found by asking the real model 20 everyday requests. Still open:

- the day planner covers only today;
- pausing (not archiving) a habit.

The new tools in items 38 to 47 have unit tests only; they were not yet run against the real model because the Codex plan was out of quota.
