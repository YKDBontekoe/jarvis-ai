# jarvis_mobile

Flutter client for Jarvis.

## Search

- Desktop and web: press `Ctrl`/`⌘`+`K` or use the sidebar/app-bar search control to open the command palette.
- Phones: the app bar and sidebar search buttons open the full-screen federated search UI.
- Results navigate through typed `route` targets returned by `GET /api/v1/search` (conversations, memories, files, tasks, reminders, skills, graph entities, channel threads, coding runs).
- Recent queries are stored locally with `shared_preferences` and cleared when you sign out.
- Push payloads that include `routeKind` (and route parameters) deep-link through the same navigation helper.
