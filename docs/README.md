# Jarvis documentation

This folder is the canonical reference for humans and coding agents working in the Jarvis repository. The root [`AGENTS.md`](../AGENTS.md) file is the fast entry point; use this index when you need depth.

## Start here

| If you need… | Read |
|--------------|------|
| Where to look for a task (routing) | [for-agents/task-routing.md](for-agents/task-routing.md) |
| Project map and key paths | [for-agents/code-navigation.md](for-agents/code-navigation.md) |
| System purpose and major subsystems | [architecture/overview.md](architecture/overview.md) |
| Layering and project dependencies | [architecture/layers-and-dependencies.md](architecture/layers-and-dependencies.md) |
| Chat, tools, approvals, SignalR | [architecture/chat-agent-runtime.md](architecture/chat-agent-runtime.md) |
| Auth, owner scope, secrets | [architecture/security-and-ownership.md](architecture/security-and-ownership.md) |

## Backend

| Topic | Document |
|-------|----------|
| HTTP API surface (`/api/v1`, hubs, A2A) | [backend/api-reference.md](backend/api-reference.md) |
| Agent tools and contributors | [backend/agent-tools.md](backend/agent-tools.md) |
| Assistant profiles | [backend/assistant-profiles.md](backend/assistant-profiles.md) |
| Temporal workflows and worker | [backend/temporal.md](backend/temporal.md) |
| Memory, knowledge graph, learning | [backend/memory-knowledge-learning.md](backend/memory-knowledge-learning.md) |
| MCP, integrations, channels, browser | [backend/integrations-and-mcp.md](backend/integrations-and-mcp.md) |

## Client and operations

| Topic | Document |
|-------|----------|
| Flutter mobile/web app | [mobile/client-architecture.md](mobile/client-architecture.md) |
| Local dev (Aspire, Compose, Codex) | [operations/local-development.md](operations/local-development.md) |
| Tests and evals | [operations/testing.md](operations/testing.md) |
| Production, CI, releases | [operations/deployment-and-ci.md](operations/deployment-and-ci.md) |
| Backups and restore | [operations/backup-and-restore.md](operations/backup-and-restore.md) |
| Configuration keys | [operations/configuration.md](operations/configuration.md) |

## Repository map (top level)

```
apps/mobile/          Flutter client
src/                  .NET solution (API, Application, Domain, Infrastructure, Agents, Workflows, …)
workers/Jarvis.Worker Temporal activity host
infra/compose/        Docker Compose stacks (dev + production)
tests/                unit, integration (Testcontainers), e2e Node fixtures
evals/                behavioral eval JSONL
scripts/              deploy, release, iOS packaging, AltStore
```

The product README at [`../README.md`](../README.md) remains the operator-facing runbook (Compose commands, production checklist, feature list). Prefer **this docs tree** for architecture and code navigation.
