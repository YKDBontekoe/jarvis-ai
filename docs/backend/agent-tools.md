# Agent tools

Tools are exposed to the model through **Microsoft Agent Framework** `AITool` instances, assembled per turn from **`IAgentToolContributor`** implementations registered in `AddJarvisAgent`.

## Contributor modules

| Contributor | Tools / behavior |
|-------------|------------------|
| **CoreAgentTools** | Clock, reminders, files, tasks, watches, memory, MCP admin/invoke, optional coding |
| **SkillToolContributor** | Load/list/save skills |
| **PersonaToolContributor** | Record persona traits |
| **KnowledgeGraphToolContributor** | Graph search and proposed writes (approval-gated) |
| **SurfaceToolContributor** | `RenderUi` generative cards |
| **RemoteAgentToolContributor** | Delegate to registered HTTPS agents (approval-gated) |
| **DeviceToolContributor** | Location, battery, clipboard, open URL, notifications on connected nodes |
| **BrowserToolContributor** | `BrowseTheWeb` session + Playwright tools |

Registration: `src/Jarvis.Agents/DependencyInjection.cs`.

## Core tool catalog

From `BuiltInAgentContributors` / dedicated tool classes:

| Tool area | Class | Approval notes |
|-----------|-------|----------------|
| Time | `ClockAgentTools.GetCurrentTime` | — |
| Reminders | `ReminderAgentTools` create/list/cancel | — |
| Files | `FileAgentTools` search/list | — |
| Tasks | `TaskAgentTools` list/cancel/create | Create hidden during background task turns |
| Watches | `ConditionWatchAgentTools` | — |
| Memory | `MemoryAgentTools` list/search/remember | **Forget** requires approval |
| MCP | `McpServerAgentTools` | Discover/add/update/invoke/read prompt/resource/remove mostly **approval** |
| Coding | `CodexCodingTools.RunCodingTaskAsync` | **Approval**; only if `Coding:Repositories` configured |

Browser, surface, device, skill, persona, graph, and remote-agent tools are defined in their respective folders under `src/Jarvis.Agents/`.

## Context providers (not tools)

`IAgentContextContributor` supplies system-side context without function calling:

- Clock + briefing timezone (`ClockContextProvider`)
- Pinned/relevant memory (`PersonalMemoryContextProvider`)
- Active tasks and watches
- Persona, skills, user dream portrait, knowledge graph summaries, device/browser/session state
- Bound assistant profile (`ProfileContextContributor`) as untrusted working notes

Order is controlled by `Order` on each contributor (`CoreAgentContext` uses `Order => 0`).

## MCP tool host

`Jarvis.Mcp.McpToolHost` opens **per-run** connections to configured stdio or Streamable HTTP servers, applies allowlists, and scrubs secrets from results. Host-level servers (GitHub, Home Assistant overlays) are configured via environment/`appsettings` and owner credentials.

## Adding a new tool

1. Implement methods on a small class in `Jarvis.Agents` with `[Description]` on parameters (model-facing docs).
2. Register via `AIFunctionFactory.Create(...)` in an `IAgentToolContributor`.
3. Wrap with `ApprovalRequiredAIFunction` when the action is sensitive.
4. Use Application services; never query `JarvisDbContext` from Agents.
5. Add unit tests in `tests/unit/Jarvis.UnitTests` when behavior is non-trivial.

## Voice

The voice MCP child reuses the same tool surface through internal HTTP callbacks (`VoiceEndpoints` internal routes).

See [chat-agent-runtime.md](../architecture/chat-agent-runtime.md) for turn lifecycle.
