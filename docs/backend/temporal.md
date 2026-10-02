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
| `ConditionWatchWorkflow` | Poll metrics / device / calendar thresholds |
| `JarvisTaskWorkflow` | Background agent tasks with approval linkage |
| `FileProcessingWorkflow` | Extract/index uploaded documents |
| `DailyBriefingWorkflow` | Scheduled morning briefing notification |
| `WeeklyReviewWorkflow` | Sunday-evening weekly review per owner (`jarvis-weekly-review-{owner}`); re-reads settings at least every 12 hours or on the `SettingsChanged` signal, catches up a missed Sunday within 36 hours, and stops when the owner turns it off |
| `PeopleCheckInWorkflow` | Daily at 09:00 local: birthday notifications (`people.birthday`, once a year per person) and one keep-in-touch nudge (`people.checkin`, repeats weekly until contact is logged). Ends when nobody has a birthday or cadence; the reconciler restarts it |
| `AssistantHeartbeatWorkflow` | Periodic reflection → memories/persona/skills |
| `AssistantDreamingWorkflow` | Nightly dream phases → memory/persona promotion |

Workflow IDs are **stable** per owner/resource so schedules are idempotent. Activities heartbeat and honor cancellation.

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

Reminders also have a DB-backed dispatcher recovery path in addition to Temporal (see product README) so missed fires can be reconciled.

## Testing

- Unit tests mock Temporal boundaries where present in `Jarvis.UnitTests`.
- Integration tests may use Testcontainers for PostgreSQL; Temporal is often exercised via e2e `local_fixture_flow.mjs` with a dev server.

See [operations/testing.md](../operations/testing.md).
