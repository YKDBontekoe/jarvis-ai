# Temporal workflows

Jarvis uses **Temporal** for durable timers, background agent tasks, file processing, learning loops, and operational recovery.

## Configuration

- `Temporal__Address` — frontend gRPC (default `localhost:7233`).
- API schedules workflows; **Jarvis.Worker** hosts workers and activities (`workers/Jarvis.Worker/Program.cs`).

Local dev: Aspire starts `temporalio/temporal` in dev mode with SQLite; UI on port **8233**.

## Workflow types

Defined in `src/Jarvis.Workflows/`:

| Workflow | Purpose |
|----------|---------|
| `ReminderWorkflow` | Fire reminders (incl. recurrence in owner IANA TZ) and post to the reminder’s linked chat |
| `AutomationScheduleWorkflow` / `AutomationPollWorkflow` / `AutomationRunWorkflow` | Owner automations; run results post to the rule’s linked chat. Every run ends as completed, failed or skipped; sensitive actions wait in the shared approval inbox (`automation-run:` request ids) and `TemporalWorkflowReconciler` closes abandoned runs |
| `ConditionWatchWorkflow` | Poll metrics / device / calendar thresholds. One-shot by default; a `repeat` watch stays active, alerts when the condition becomes true, and alerts again only after it cleared and `cooldownMinutes` passed |
| `JarvisTaskWorkflow` | Background agent tasks with approval linkage |
| `FileProcessingWorkflow` | Extract/index uploaded documents |
| `DailyBriefingWorkflow` | Scheduled morning briefing notification: reminders and active tasks, plus one section per `IBriefingSectionProvider` (calendar, inbox threads waiting for a reply, due or overdue commitments, open habits, birthdays within a week, budgets near their limit). Providers are resolved at delivery time and a failing one is left out |
| `WeeklyReviewWorkflow` | Sunday-evening weekly review per owner (`jarvis-weekly-review-{owner}`); re-reads settings at least every 12 hours or on the `SettingsChanged` signal, catches up a missed Sunday within 36 hours, and stops when the owner turns it off |
| `PeopleCheckInWorkflow` | Daily at 09:00 local: birthday notifications (`people.birthday`, once a year per person) and one keep-in-touch nudge (`people.checkin`, repeats weekly until contact is logged). Before nudging it marks people as contacted when the owner wrote to their linked WhatsApp chat, and afterwards sends the relationship radar's weekly `people.radar` notification. Ends when nobody has a birthday, a cadence or a linked chat; the reconciler restarts it |
| `AssistantHeartbeatWorkflow` | Periodic reflection → memories/persona/skills, inbox sync and triage, check-ins (due reminders, stale approvals, failed tasks, upcoming meetings, urgent waiting threads, overdue commitments, reflection insights) and, within the [autonomy envelope](life-features.md#autonomy-envelope), meeting-prep tasks. **On by default for every owner.** |
| `AssistantDreamingWorkflow` | Nightly dream phases → memory/persona promotion. **On by default for every owner.** |
| `PeopleCheckInWorkflow` | Daily birthday/contact nudges (09:00 local) and the weekly relationship radar |

Workflow IDs are **stable** per owner/resource so schedules are idempotent. Activities heartbeat and honor cancellation.

**Defaults reach every owner.** The reconciler walks `IOwnerDirectory` (every Identity user), not only owners with a stored settings row, so heartbeat, dreaming and the weekly review run for owners who never opened Settings. A missing `learning` or `weekly-review` row means `LearningSettings.Default` / `WeeklyReviewSettings.Default`, and the activities apply the same fallback. Only a saved row with the feature switched off stops it. The morning briefing needs the owner's time zone, so the app calls `POST /briefings/daily/default` once per account with the device zone; the server then creates an enabled 08:00 briefing only when no briefing row exists yet.

## Worker responsibilities

Besides workflow activities, the worker hosts:

- `TemporalWorkflowReconciler` — align schedules with DB state
- `MemoryIndexingWorker` — async memory embedding/index pipeline
- Shared agent stack (`AddJarvisAgent`) for task/heartbeat/dream activities that need model calls

`WorkerCurrentUser` implements `ICurrentUser` from workflow/activity context.

## API interaction

Application services (`IReminderService`, `IJarvisTaskService`, `IConditionWatchService`, schedulers in `Jarvis.Application.Workflows`) start or cancel workflows through Temporal client wrappers registered in Infrastructure/API DI.

If Temporal is unavailable, reminder creation returns **503** from the API (`AutomationEndpoints`).

## Dispatcher recovery

**Place reminders** ("when I arrive at the supermarket") have no workflow. A reminder with `place` (name, lat/lon, radius, `arrive`/`leave`, `repeats`) stays pending until a position posted to `POST /devices/telemetry` crosses its edge; `PlaceReminderService` → `IReminderRepository.ObservePositionAsync` delivers the `reminder.due` notification, posts to the linked chat and starts `reminder_due` automations. The reconciler skips them (`location_latitude IS NULL` filters). The app reports positions through `PlaceReminderTracker` only while a place reminder is pending (background on iOS with "Always" access).

Reminders also have a DB-backed dispatcher recovery path in addition to Temporal (see product README) so missed fires can be reconciled.

## Testing

- Unit tests mock Temporal boundaries where present in `Jarvis.UnitTests`.
- Integration tests may use Testcontainers for PostgreSQL; Temporal is often exercised via e2e `local_fixture_flow.mjs` with a dev server.

See [operations/testing.md](../operations/testing.md).
