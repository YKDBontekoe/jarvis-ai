# Changing Temporal workflow or activity identifiers

Jarvis workers and the API rely on **stable** Temporal workflow type names, activity type names, and task queue names. Existing workflow histories deserialize inputs and route activities by these strings. Renaming an identifier without a coordinated migration leaves in-flight runs unable to schedule or execute activities.

## Safe procedure

1. **Inventory callers** — Search the repository for the workflow class, activity attribute, and any string literals referencing the old name (API schedulers, reconcilers, tests, and ops runbooks).
2. **Add, do not replace** — Register the new activity or workflow alongside the old one. Implement the new handler by delegating to shared code so behavior stays identical.
3. **Deploy workers first** — Ship a worker build that registers both identifiers before any client starts workflows that depend on the new name.
4. **Migrate starters** — Update API schedulers and reconcilers to start new work under the new workflow ID pattern or workflow type only after workers are live.
5. **Drain old executions** — Allow existing workflow histories to complete (or explicitly cancel them after owner communication). Do not remove the old registration until Temporal reports no open runs for that type.
6. **Remove the legacy name** — Delete the old registration and attribute only after drain completes and compatibility tests are updated.

## Serialization

Workflow and activity payloads use the default .NET Temporal JSON payload converter. Changing property names on input records is a separate breaking change from renaming activity types; treat record shape changes with the same add-version/drain process or explicit version fields on inputs.

## This refactor

No Temporal workflow or activity **type names** were renamed. Logic was moved into dedicated classes under `Activities/`, `Files/`, `Learning/`, and `Hosting/` only.
