# Task routing for coding agents

Use this table to jump to the right doc and code **before** broad repo search.

| User intent / symptom | Read first | Primary code |
|----------------------|------------|--------------|
| Add or change HTTP API | [api-reference.md](../backend/api-reference.md) | `src/Jarvis.Api/Endpoints/` |
| Agent tool or prompt context | [agent-tools.md](../backend/agent-tools.md) | `src/Jarvis.Agents/` |
| Assistant profiles / context isolation | [assistant-profiles.md](../backend/assistant-profiles.md) | `Jarvis.Application/Profiles/`, `Jarvis.Agents/Profiles/` |
| Chat streaming / SignalR | [chat-agent-runtime.md](../architecture/chat-agent-runtime.md) | `Jarvis.Api/Conversations/`, hub |
| Tool approvals stuck | [chat-agent-runtime.md](../architecture/chat-agent-runtime.md) | `Jarvis.Application/Approvals/` |
| Reminder/watch/task scheduling | [temporal.md](../backend/temporal.md) | `Jarvis.Workflows/`, `Jarvis.Worker` |
| Memory search / forget / pin | [memory-knowledge-learning.md](../backend/memory-knowledge-learning.md) | `Jarvis.Application/Memory/`, Infrastructure |
| Knowledge graph UI/API | same + [mobile/client-architecture.md](../mobile/client-architecture.md) | `features/memory/`, graph endpoints |
| MCP server / integration secret | [integrations-and-mcp.md](../backend/integrations-and-mcp.md) | `Jarvis.Mcp/`, `IntegrationEndpoints` |
| WhatsApp / Signal | [integrations-and-mcp.md](../backend/integrations-and-mcp.md) | `ChannelEndpoints`, `features/channels/` |
| Browser / Playwright | same | `Jarvis.Agents/Browser/`, compose browser overlay |
| Flutter UI bug | [mobile/client-architecture.md](../mobile/client-architecture.md) | `apps/mobile/lib/features/...` |
| Voice / LiveKit | [api-reference.md](../backend/api-reference.md), README voice section | `Jarvis.Api/Realtime/` |
| Auth / JWT / owner mix-up | [security-and-ownership.md](../architecture/security-and-ownership.md) | `Jarvis.Infrastructure/Identity/` |
| File upload / virus scan | [security-and-ownership.md](../architecture/security-and-ownership.md) | `Jarvis.Application/Files/` |
| Deploy / Compose / GHCR | [deployment-and-ci.md](../operations/deployment-and-ci.md) | `infra/compose/` |
| Local repro / tests | [testing.md](../operations/testing.md), [local-development.md](../operations/local-development.md) | `tests/` |
| Config key meaning | [configuration.md](../operations/configuration.md) | `appsettings.json` |

## Conventions

- **Owner scope**: every data change must respect `ICurrentUser.OwnerId`.
- **Approvals**: if a user-facing action is destructive or crosses a trust boundary, use `ApprovalRequiredAIFunction` or the approvals API.
- **Secrets**: never log or return credential values; follow MCP redaction patterns.
- **Tests**: add unit tests in `Jarvis.UnitTests` for pure logic; use e2e fixtures for full-stack regressions.

## Docs hub

Full index: [docs/README.md](../README.md). Entry point for agents: [AGENTS.md](../../AGENTS.md).
