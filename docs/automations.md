# Owner automations

Automations combine a **typed trigger**, optional **conditions**, and one or more **typed actions**. Definitions are stored as JSON schema version `1`; Jarvis does not store executable scripts.

## API

- `GET /api/v1/automations` — list rules
- `POST /api/v1/automations` — create draft (`name`, `definition`)
- `PUT /api/v1/automations/{id}` — update draft/disabled rule
- `POST /api/v1/automations/validate` — dry-run validation
- `POST /api/v1/automations/{id}/enable` / `disable`
- `POST /api/v1/automations/{id}/test-run` / `run`
- `GET /api/v1/automations/{id}/runs` — run history
- `POST /api/v1/automations/runs/{runId}/approve` / `decline`

## Temporal workflows

| Workflow | Workflow ID | Purpose |
|----------|-------------|---------|
| `AutomationScheduleWorkflow` | `jarvis-automation-{ruleId}` | IANA schedule triggers |
| `AutomationPollWorkflow` | same ID for polling triggers | JSON/battery/location/calendar |
| `AutomationRunWorkflow` | `jarvis-automation-run-{runId}` | Execute actions once per fire |

Schedule and poll workflows call activities that start run workflows with deterministic idempotency keys (`schedule:…`, `poll:…`, `reminder_due:…`).

## Safety

- Public JSON URLs use the same SSRF rules as condition watches.
- Channel and agent-run actions require owner approval before execution (workflow signal after in-app approval).
- Run limits: default 5 actions per run, 15 minute activity timeout, 5 minute cooldown, 3 concurrent runs per owner.
- Audit events record lifecycle without secret values or full untrusted payloads.

## Example definition

```json
{
  "schemaVersion": 1,
  "trigger": { "kind": "schedule", "localTime": "09:00:00", "timeZoneId": "Europe/Amsterdam" },
  "conditions": [
    { "kind": "time_window", "startLocalTime": "08:00:00", "endLocalTime": "18:00:00", "timeZoneId": "Europe/Amsterdam" }
  ],
  "actions": [
    { "kind": "notification", "title": "Morning", "body": "Your automation ran." }
  ],
  "limits": { "cooldownMinutes": 10, "maxActionsPerRun": 3 }
}
```
