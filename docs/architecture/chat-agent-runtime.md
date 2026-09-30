# Chat and agent runtime

Interactive chat is the primary path: Flutter sends a message, the API runs an agent turn, streams tokens and tool events over SignalR, and persists the final transcript.

## Request path

1. **Client** — `POST /api/v1/conversations/{id}/messages` with message body (`ConversationEndpoints`).
2. **Remote query host** — `RemoteQueryExecutor` runs the turn without tying lifetime to the HTTP connection (mobile disconnect does not cancel the run by default).
3. **Agent** — `IJarvisAgent` (implementation in `Jarvis.Agents`) resolves the conversation’s bound profile snapshot, then `JarvisAgentFactory` builds a Microsoft Agent Framework agent with:
   - **Chat client** — `CodexCliChatClient` (Codex app-server) or OpenRouter when owner settings select it (`ChatClientResolver`). Profiles may overlay model class, model ids, and reasoning effort; they cannot change the owner’s provider.
   - **Tools** — aggregated from `IAgentToolContributor` registrations, then filtered by the snapshot (skills, files). Approval wrappers and MCP operator allowlists stay in force.
   - **Context** — `IAgentContextContributor` providers (clock, pinned memory, active tasks, watches, persona, skills, bound profile, graph, surfaces, devices, browser session, remote agents). Profile text is untrusted working notes, not privileged system instructions.
4. **Codex adapter** — Spawns Codex CLI in a restricted sandbox; streams deltas; Jarvis executes tool calls locally with approval gates. Web search uses Codex `standalone_web_search` when enabled.
5. **Persistence** — Messages and session state via `IConversationStore` and Agent Framework session store; compaction trims old groups above token budget while keeping recent tool groups intact.
6. **Realtime** — `JarvisEventsHub` at `/hubs/events` publishes assistant text deltas and `tool.started` / `tool.completed` / `tool.failed` (names only, no args).

## Tool approval flow

Tools wrapped in `ApprovalRequiredAIFunction` (see `BuiltInAgentContributors.cs` and MCP/browser/coding wrappers) create a pending approval row. The client lists `GET /api/v1/approvals` and posts a decision to `/approvals/{id}/decision`. The agent run resumes with the user's choice.

Idempotency: approval rows are guarded by a unique key on owner + request + tool call id.

## Tool failures and self-correction

`ToolFailureFeedback` (in `Jarvis.Agents`) is the function invoker for every agent turn. Input validation errors thrown by tools (`ArgumentException`, `FormatException`, `JsonException`) are passed back to the model, capped at 400 characters, so it can fix its arguments and retry within the same turn. Every other exception becomes a generic "the tool failed" result; the real exception is only logged. The invoker runs after approval gating, so it never sees a call the user has not approved.

When Codex names a function that does not exist, `CodexCliChatClient` asks once for a corrected response instead of failing the turn, the same way it already retries malformed `argumentsJson`. Tool results in the Codex prompt carry the tool name, so multi-step chains stay readable.

## Background tasks

`CreateTaskAsync` is **not** offered during an executing background task turn (`context.IsBackgroundTask`). Task conversations use separate session IDs; deleting a normal conversation does not remove task-backed threads. Reminder and automation chats are ordinary interactive conversations (`conversationId` on the reminder or rule) so the owner can reply when they fire.

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

See [backend/agent-tools.md](../backend/agent-tools.md), [backend/assistant-profiles.md](../backend/assistant-profiles.md), and [operations/configuration.md](../operations/configuration.md).
