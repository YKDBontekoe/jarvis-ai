# Layers and dependencies

Jarvis follows a **modular monolith** layout: clear project boundaries without microservice network hops for the core assistant loop.

## Solution projects

| Project | Path | Depends on | Contains |
|---------|------|------------|----------|
| **Jarvis.Domain** | `src/Jarvis.Domain` | — | Entities, enums, domain types (no I/O) |
| **Jarvis.Application** | `src/Jarvis.Application` | Domain | Use-case interfaces, DTOs, orchestration contracts |
| **Jarvis.Infrastructure** | `src/Jarvis.Infrastructure` | Application, Domain | EF Core, Identity, S3, external HTTP adapters |
| **Jarvis.Memory** | `src/Jarvis.Memory` | Application | Embedding/search helpers used by API and worker |
| **Jarvis.Mcp** | `src/Jarvis.Mcp` | Application | MCP client host, tool invocation, redaction |
| **Jarvis.Agents** | `src/Jarvis.Agents` | Application, Mcp, Memory | Microsoft Agent Framework wiring, Codex `IChatClient`, tool contributors |
| **Jarvis.Workflows** | `src/Jarvis.Workflows` | Application | Temporal workflow definitions |
| **Jarvis.Api** | `src/Jarvis.Api` | Agents, Infrastructure, Workflows (indirect) | HTTP, SignalR, DI composition, endpoints |
| **Jarvis.Worker** | `workers/Jarvis.Worker` | Agents, Infrastructure, Workflows, Memory | Temporal worker host |
| **Jarvis.AppHost** | `src/Jarvis.AppHost` | Api, Worker | Aspire distributed app |
| **Jarvis.ServiceDefaults** | `src/Jarvis.ServiceDefaults` | — | Shared OTEL, health checks |

Tests: `tests/unit/Jarvis.UnitTests`, `tests/integration/Jarvis.IntegrationTests`, `tests/e2e/` (Node).

## Dependency rules (enforced)

1. **Domain** has no references to ASP.NET, EF, Temporal, or Flutter.
2. **Application** defines ports (`IConversationStore`, `IMemoryService`, …); implementations live in Infrastructure.
3. **Agents** register tools and context providers; they call Application services, not EF directly.
4. **Api** maps HTTP to Application services and hosts real-time/voice edge code under `Jarvis.Api/Realtime`.
5. **Worker** reuses the same Agent + Infrastructure stack with `WorkerCurrentUser` standing in for `ICurrentUser`.
6. **Api** does not use `JarvisDbContext` outside `Jarvis.Api.Hosting` (startup migrations). Background services such as push and real-time notification fan-out go through Application ports (`IPushDeliveryQueue`, `INotificationFeed`).

`tests/unit/Jarvis.UnitTests/ArchitectureTests.cs` checks these rules on every unit test run: allowed project references per layer, no EF Core / ASP.NET Core / Npgsql / Temporal references in inner layers, and no `JarvisDbContext` in the API outside hosting. If a test fails, add an Application port rather than widening the allowlist.

## DI entry points

| Host | Registration |
|------|----------------|
| API | `builder.Services.AddJarvisApi(...)` in `Jarvis.Api/Hosting` (calls Infrastructure, Agents, workflows schedulers) |
| Worker | `AddJarvisInfrastructure`, `AddJarvisMemory`, `AddJarvisAgent` in `workers/Jarvis.Worker/Program.cs` |
| Agents | `AddJarvisAgent` in `src/Jarvis.Agents/DependencyInjection.cs` — registers `CodexCliChatClient`, contributors, `IJarvisAgent` |

## API endpoint modules

Endpoints are static classes in `src/Jarvis.Api/Endpoints/` mapped from `Program.cs`:

- `ConversationEndpoints` — conversations, messages, approvals
- `AutomationEndpoints` — reminders, watches, tasks, briefings
- `IntegrationEndpoints` — MCP registry, credentials
- `MemoryEndpoints`, `KnowledgeGraphEndpoints` (partial), `FileEndpoints`, `SkillEndpoints`, `PersonaEndpoints`, `LearningEndpoints`, `UsageEndpoints`
- `ChannelEndpoints`, `VoiceEndpoints`, `BrowserEndpoints`, `DeviceEndpoints`, `SurfaceEndpoints`
- `A2AEndpoints` — agent card, `/a2a`, remote agent CRUD
- `PersonalAssistantEndpoints` — home, coding runs, graph mutations, OAuth packs

Auth: `IdentityAuthEndpoints` under `/api/v1/auth`.

## Flutter client

`apps/mobile/lib/` mirrors product areas under `features/` (chat, memory, settings, …) with `api/jarvis_http.dart` as the HTTP client. See [mobile/client-architecture.md](../mobile/client-architecture.md).

## When adding a feature

1. Extend **Domain** only if you need new persisted shapes.
2. Add Application **service interface + implementation** (Infrastructure).
3. Expose **HTTP** in a dedicated `*Endpoints.cs` (or extend an existing one).
4. If the agent should act on it, add an **`IAgentToolContributor` or `IAgentContextContributor`** in `Jarvis.Agents`.
5. If it must be durable/async, add a **workflow/activity** in `Jarvis.Workflows` and register activities in the worker.
