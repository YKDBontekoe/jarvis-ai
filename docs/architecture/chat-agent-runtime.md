# Chat and agent runtime

Interactive chat is the primary path: Flutter sends a message, the API runs an agent turn, streams tokens and tool events over SignalR, and persists the final transcript.

## Request path

1. **Client** — `POST /api/v1/conversations/{id}/messages` with message body (`ConversationEndpoints`).
2. **Remote query host** — `RemoteQueryExecutor` runs the turn without tying lifetime to the HTTP connection (mobile disconnect does not cancel the run by default).
3. **Agent** — `IJarvisAgent` (implementation in `Jarvis.Agents`) resolves the conversation’s bound profile snapshot, then `JarvisAgentFactory` builds a Microsoft Agent Framework agent with:
   - **Chat client** — `CodexCliChatClient` (Codex app-server) or OpenRouter when owner settings select it (`ChatClientResolver`). Profiles may overlay model class, model ids, and reasoning effort; they cannot change the owner’s provider.
   - **Tools** — aggregated from `IAgentToolContributor` registrations, then filtered by the snapshot (skills, files). Approval wrappers and MCP operator allowlists stay in force.
   - **Context** — `IAgentContextContributor` providers (clock, pinned memory, active tasks, watches, persona, skills, bound profile, graph, surfaces, devices, browser session, remote agents). Profile text is untrusted working notes, not privileged system instructions.
4. **Codex adapter** — Spawns Codex CLI with the access profile from `Codex:Access` (default: its own shell and a workspace-write sandbox on a per-turn scratch directory, network on; see [configuration](../operations/configuration.md)); streams deltas; Jarvis executes tool calls locally with approval gates. Web search uses Codex's hosted `web_search` tool in live mode when enabled; the experimental `standalone_web_search` is turned off for chat.
5. **Persistence** — Messages and session state via `IConversationStore` and Agent Framework session store; compaction (`CurrentContextCompactionProvider`) first replaces the oldest turns with a cached rolling summary (`RollingSummaryCompaction`, written in the background, never blocking a turn), then collapses old tool results and trims old groups above the token budget while keeping recent tool groups intact. `tests/eval/Jarvis.CompactionEval` measures it with a real model.
6. **Realtime** — `JarvisEventsHub` at `/hubs/events` publishes assistant text deltas and `tool.started` / `tool.completed` / `tool.failed` (names only, no args). Codex's native web search is reported the same way as a `WebSearch` tool step (`NativeToolProgress`), carried in update metadata so it never enters the stored transcript. While a reply has no text yet, the Flutter typing indicator names the current step (thinking, working, working out the next step) and shows elapsed seconds after 8 s.

## Tool approval flow

Tools wrapped in `ApprovalRequiredAIFunction` (see `BuiltInAgentContributors.cs` and MCP/browser/coding wrappers) create a pending approval row. The client lists `GET /api/v1/approvals` and posts a decision to `/approvals/{id}/decision`. The agent run resumes with the user's choice.

The owner can choose **Always allow** on that card. `rememberCategory: true` stores a standing grant for the action's category (`ApprovalCategories`: forgetting memories, using the browser, one MCP tool on one server, and so on). Later calls in that category run in the same turn without a new card, including voice and automation actions. Grants are listed and revoked at `GET`/`DELETE /api/v1/approvals/standing`. They are owner settings; assistant profiles cannot add or remove them.

A grant can be limited: `rememberHours` (1 to 8760) makes it expire, and `rememberScope: "tasks"` makes it apply only to background task and automation runs (default `all`). An expired grant stops working at once and is dropped from the list and from storage on the next save; grants stored before this existed are permanent and global as before.

**Automatic approval policy.** Before asking, `JarvisAgent` checks standing grants and then `IApprovalPolicy` (`Application/Approvals/ApprovalPolicy.cs`) with the owner's `AutonomySettings`. `ToolRiskPolicy` gives every built-in gated tool a risk class; anything not listed is `Unknown` and asks.

| Class | Examples | Without asking |
|---|---|---|
| `ReadOnly` | `DiscoverMcpServerTools`, `ReadMcpResource`, `GetMcpPrompt` | Yes (switch `autoApproveReadOnly`) |
| `ReversibleLocal` | `RunAutomation`, `ProposeGraphFact`, `CorrectGraphFact`, `ClipUrlToLibrary` | Only inside a background task at level `full` |
| `SensitiveRead` | `GetDeviceLocation`, `ReadDeviceClipboard` | Never |
| `Outbound` | `SendWhatsAppMessage`, `InvokeMcpTool`, browser, coding, MCP server changes | Never |
| `Destructive` | `ForgetMemory`, `DeleteExpense`, `RemovePerson`, graph merge/forget | Never |

Integration tools whose server sets an explicit `readOnlyHint` without `destructiveHint` count as `ReadOnly` (switch `autoApproveMcpReadHints`). That is the server's own claim, so it is ignored for any name that is also a built-in tool. The level is `full` (default), `standard` (read-only only) or `ask_everything`; `enabled: false` turns the policy off and restores standing-grants-only behaviour. Each automatic approval is audited as `approval.policy_auto_approved` (tool, risk, reason; never arguments) and counted per UTC day against `maxAutoApprovalsPerDay` (default 200); past it, calls ask again. A background task is any durable task conversation, including ones the heartbeat started.

Idempotency: approval rows are guarded by a unique key on owner + request + tool call id.

## Retry after a failed model call

`ConversationTurnService` runs the agent once more when the first attempt fails before producing anything: no text streamed and no tool started (`RetryWhenNothingHappenedAsync`). That covers the Codex process dying, timing out or returning no thread, and broken connections (`AgentFailureMessage.IsRetryable`). Signed-out, rate-limited and missing-CLI failures are never retried, and neither is anything after the first output, so a reply is never duplicated and an action never repeats. The retry applies to chat sends and regenerate; the mission supervisor polls every 3 seconds while any mission runs (10 when idle) so step handoffs are quick.

## Tool failures and self-correction

`ToolFailureFeedback` (in `Jarvis.Agents`) is the function invoker for every agent turn. Input validation errors thrown by tools (`ArgumentException`, `FormatException`, `JsonException`) are passed back to the model, capped at 400 characters, so it can fix its arguments and retry within the same turn. Every other exception becomes a generic "the tool failed" result; the real exception is only logged. Network and timeout failures (`HttpRequestException`, `TimeoutException`, `IOException`, cancelled HTTP calls) use a different generic text that says a retry may help, so the model tries once more instead of giving up or looping on a permanent error. Only the category reaches the model, never the exception text. The invoker runs after approval gating, so it never sees a call the user has not approved.

When Codex names a function that does not exist, `CodexCliChatClient` asks once for a corrected response instead of failing the turn, the same way it already retries malformed `argumentsJson`. Tool results in the Codex prompt carry the tool name, so multi-step chains stay readable.

## Background tasks

`CreateTaskAsync` is **not** offered during an executing background task turn (`context.IsBackgroundTask`). Task conversations use separate session IDs; deleting a normal conversation does not remove task-backed threads. Reminder and automation chats are ordinary interactive conversations (`conversationId` on the reminder or rule) so the owner can reply when they fire.

Temporal runs task workflows (`JarvisTaskWorkflow`); the worker hosts activities that invoke the same agent stack with `WorkerCurrentUser`.

Jarvis also starts background tasks on its own when events happen: a watch fires, a task fails, a commitment is made, or someone needs a reply. These go through `AgentReactionWorkflow`, within `AutonomySettings`, quiet hours and a daily budget. `tool.completed` carries `toolCallId` and `refs` (the things the call made) for result cards. See [event-spine.md](event-spine.md).

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
