# Improvements ledger

A numbered list of what was changed in the QA-and-improvement effort, so each item can be checked. An item is listed only when it shipped in a commit with a test or an end-to-end check. The earlier QA audit fixes live in the commit messages of `b08af3e`, `1f03b72`, `a92a902`, `a7d387d` and `8f990be`; they are not repeated here.

**Count so far: 37 improvements in this ledger. The goal of 100 has not been reached.**

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

## Gaps found but not built

Found by asking the real model 20 everyday requests. Still open:

- biweekly ("every other week") reminders need a recurrence anchored to a start date;
- the day planner covers only today;
- renaming or pinning a conversation from chat;
- deleting a project from chat (would need an approval);
- a shopping list;
- calendar events are readable only when an ICS URL is configured, and the assistant does not say so.
