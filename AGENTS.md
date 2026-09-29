# Agent instructions (Jarvis repository)

This file is the **fast entry point** for Cursor Cloud Agents and other coding agents. Detailed references live under [`docs/`](docs/README.md).

## What this repo is

Self-hosted personal assistant: **.NET monolith** (`src/`, `workers/`) + **Flutter** client (`apps/mobile/`), **PostgreSQL**, **Temporal**, **Codex CLI** (primary inference), optional **OpenRouter**. Owner-scoped data; tool approvals for sensitive actions.

## Read order

1. **[docs/for-agents/task-routing.md](docs/for-agents/task-routing.md)** — pick the right doc and code path for the task.
2. **[docs/for-agents/code-navigation.md](docs/for-agents/code-navigation.md)** — directory map and grep anchors.
3. **[docs/architecture/overview.md](docs/architecture/overview.md)** — system diagram and domain table.

Full index: **[docs/README.md](docs/README.md)**.

## Common tasks → documentation

| Task | Document |
|------|----------|
| HTTP API or auth | [docs/backend/api-reference.md](docs/backend/api-reference.md) |
| Agent tools / Codex turn | [docs/backend/agent-tools.md](docs/backend/agent-tools.md), [docs/architecture/chat-agent-runtime.md](docs/architecture/chat-agent-runtime.md) |
| Reminders, tasks, watches, dreams | [docs/backend/temporal.md](docs/backend/temporal.md), [docs/backend/memory-knowledge-learning.md](docs/backend/memory-knowledge-learning.md) |
| MCP / integrations / channels | [docs/backend/integrations-and-mcp.md](docs/backend/integrations-and-mcp.md) |
| Assistant profiles | [docs/backend/assistant-profiles.md](docs/backend/assistant-profiles.md) |
| Flutter UI | [docs/mobile/client-architecture.md](docs/mobile/client-architecture.md) |
| Run locally / test | [docs/operations/local-development.md](docs/operations/local-development.md), [docs/operations/testing.md](docs/operations/testing.md) |
| Config keys | [docs/operations/configuration.md](docs/operations/configuration.md) |
| Deploy / CI | [docs/operations/deployment-and-ci.md](docs/operations/deployment-and-ci.md) |
| Security / owner scope | [docs/architecture/security-and-ownership.md](docs/architecture/security-and-ownership.md) |

Operator runbook (Compose commands, feature list): **[README.md](README.md)** — prefer `docs/` for architecture and navigation.

## Engineering rules

- Respect **owner scope** (`ICurrentUser.OwnerId`) on every persistence and API change.
- Sensitive agent actions → **approvals** (`ApprovalRequiredAIFunction`); never bypass without explicit product intent.
- **No secrets** in logs, tests, or docs; follow MCP credential redaction in `Jarvis.Mcp`.
- **Layering**: Domain → Application → Infrastructure; Agents call Application services, not EF directly.
- New HTTP routes → `src/Jarvis.Api/Endpoints/` + register in `Program.cs`.
- New agent capabilities → `IAgentToolContributor` / `IAgentContextContributor` in `Jarvis.Agents`.

## Build and test (minimal)

Cloud agent bootstrap:

```sh
bash .cursor/install.sh
```

```sh
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
```

Flutter: `cd apps/mobile && flutter test`.

Full-stack fixture flow: [docs/operations/testing.md](docs/operations/testing.md).

## Local dependencies

- **Aspire**: `dotnet run --project src/Jarvis.AppHost` (after `infra/compose/.env` and Codex login).
- **Docker** required for integration tests and Compose; see [docs/operations/local-development.md](docs/operations/local-development.md).

Do not run Aspire and development Compose on the same ports at once.

## Cursor Cloud environment

- Install: `.cursor/install.sh` (pinned .NET SDK, Docker, `dotnet build`).
- Optional terminal: `dockerd` via `.cursor/dockerd.sh` per `.cursor/environment.json`.

When adding cloud-specific setup steps that agents should always follow, extend this section or `.cursor/environment.json` in the same PR as the code that depends on them.
