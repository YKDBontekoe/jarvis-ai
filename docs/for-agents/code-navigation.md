# Code navigation

Quick map of high-signal paths. Grep from repo root is fine; start here to reduce noise from `bin/` and `obj/`.

## Solution entry

- `Jarvis.sln` — all C# projects
- `src/Jarvis.Api/Program.cs` — HTTP pipeline and endpoint map
- `workers/Jarvis.Worker/Program.cs` — Temporal worker + activities
- `src/Jarvis.AppHost/Program.cs` — local orchestration

## Layered backend

```
src/Jarvis.Domain/           Entities only
src/Jarvis.Application/      Interfaces + use cases (by feature folder)
src/Jarvis.Infrastructure/   EF, Identity, external IO
src/Jarvis.Agents/           Agent Framework + Codex adapter + tools
src/Jarvis.Mcp/              MCP client host
src/Jarvis.Memory/           Embedding/search helpers
src/Jarvis.Workflows/        Temporal workflow definitions
src/Jarvis.Api/              Endpoints, hubs, voice realtime
```

## Application feature folders

Each folder under `Jarvis.Application/` roughly matches a product area:

`Conversations`, `Workflows`, `Memory`, `Files`, `Integrations`, `Approvals`, `Channels`, `Devices`, `Home`, `Learning`, `Persona`, `Profiles`, `Skills`, `Browser`, `Realtime`, `Usage`, `Audit`, `Agents`, `Security`, `Settings`, `Surfaces`.

Persistence models and migrations: `Jarvis.Infrastructure/Persistence/`.

## Agent contributors (grep targets)

```text
IAgentToolContributor
IAgentContextContributor
ApprovalRequiredAIFunction
CodexCliChatClient
```

## Flutter

```
apps/mobile/lib/main.dart
apps/mobile/lib/api/jarvis_http.dart
apps/mobile/lib/features/chat/     # largest surface
apps/mobile/lib/features/<area>/   # one folder per product area; every screen lives here
apps/mobile/lib/features/settings/
```

## Infrastructure and deploy

```
src/Jarvis.AppHost/              Aspire: local dev + production deployment (publishes Compose)
infra/compose/                  Dockerfile, production env template
infra/livekit/livekit.yaml
scripts/deploy/
.github/workflows/
```

## Tests

```
tests/unit/Jarvis.UnitTests/
tests/integration/Jarvis.IntegrationTests/
tests/e2e/
evals/jarvis-core-v1.jsonl
```

## Cloud agent bootstrap

```
.cursor/install.sh
.cursor/environment.json
```

## Related

- [task-routing.md](task-routing.md) — intent → doc → path
- [layers-and-dependencies.md](../architecture/layers-and-dependencies.md) — dependency rules
