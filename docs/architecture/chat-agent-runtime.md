# Chat and agent runtime

Interactive chat is the primary path: Flutter sends a message, the API runs an agent turn, streams tokens and tool events over SignalR, and persists the final transcript.

## Request path

1. **Client** — `POST /api/v1/conversations/{id}/messages` with message body (`ConversationEndpoints`).
2. **Remote query host** — `RemoteQueryExecutor` runs the turn without tying lifetime to the HTTP connection (mobile disconnect does not cancel the run by default).
3. **Agent** — `IJarvisAgent` (implementation in `Jarvis.Agents`) builds an Microsoft Agent Framework agent with:
   - **Chat client** — `CodexCliChatClient` (Codex app-server) or OpenRouter when owner settings select it (`ChatClientResolver`).
   - **Tools** — aggregated from `IAgentToolContributor` registrations.
   - **Context** — `IAgentContextContributor` providers (clock, pinned memory, active tasks, watches, persona, skills, graph, surfaces, devices, browser session, remote agents).
4. **Codex adapter** — Spawns Codex CLI in a restricted sandbox; streams deltas; Jarvis executes tool calls locally with approval gates. Web search uses Codex `standalone_web_search` when enabled.
5. **Persistence** — Messages and session state via `IConversationStore` and Agent Framework session store; compaction trims old groups above token budget while keeping recent tool groups intact.
6. **Realtime** — `JarvisEventsHub` at `/hubs/events` publishes assistant text deltas and `tool.started` / `tool.completed` / `tool.failed` (names only, no args).

## Tool approval flow

Tools wrapped in `ApprovalRequiredAIFunction` (see `BuiltInAgentContributors.cs` and MCP/browser/coding wrappers) create a pending approval row. The client lists `GET /api/v1/approvals` and posts a decision to `/approvals/{id}/decision`. The agent run resumes with the user's choice.

Idempotency: approval rows are guarded by a unique key on owner + request + tool call id.

## Background tasks

`CreateTaskAsync` is **not** offered during an executing background task turn (`context.IsBackgroundTask`). Task conversations use separate session IDs; deleting a normal conversation does not remove task-backed threads.

Temporal runs task workflows (`JarvisTaskWorkflow`); the worker hosts activities that invoke the same agent stack with `WorkerCurrentUser`.

## Voice parallel path

Voice uses LiveKit rooms and an in-process C# runtime (`Jarvis.Api/Realtime`). Codex realtime bridges PCM over WebRTC; tools call back into the API via internal routes secured with `Voice__WorkerSecret`. A separate stdio MCP child can be started with `dotnet Jarvis.Api.dll voice-mcp`.

## Key source files

| Concern | Location |
|---------|----------|
| Agent factory / turn | `src/Jarvis.Agents/JarvisAgent.cs`, `JarvisAgentFactory.cs` |
| Tool registration | `src/Jarvis.Agents/DependencyInjection.cs`, `BuiltInAgentContributors.cs` |
| Codex client | `src/Jarvis.Agents/ModelProviders/CodexCliChatClient.cs` |
| HTTP send/cancel | `src/Jarvis.Api/Endpoints/ConversationEndpoints.cs` |
| SignalR hub | `src/Jarvis.Api/Conversations/JarvisEventsHub.cs` (or adjacent) |
| Approvals store | `Jarvis.Application/Approvals`, `Jarvis.Infrastructure` |

## Model routing

Per-owner settings (`/api/v1/settings/models`) choose Codex vs OpenRouter, model IDs per class (fast, reasoning, coding, vision, embedding), and optional Codex CLI self-update under `$CODEX_HOME/cli`.

See [backend/agent-tools.md](../backend/agent-tools.md) and [operations/configuration.md](../operations/configuration.md).
