# Event spine, entity links and reactions

Jarvis's features (reminders, tasks, watches, inbox, journal, expenses, missions, approvals and more) used to talk to
each other only through a few foreign keys and an automation-only event bus. The **event spine** gives them one shared
vocabulary for things, one stream of what happens, and one place where Jarvis decides to follow up on its own.

```
feature code ──publish──▶ IJarvisEventBus ──▶ PersistingEventHandler   → owner_events (activity feed, situation)
 (watch fired,                               ├─▶ ConversationLinkHandler → entity_links (chat ↔ what Jarvis made)
  task failed,                               ├─▶ RealtimeEventHandler    → SignalR "event.created" (open apps)
  approval, …)                               └─▶ AgentReactionHandler    → AgentReactionWorkflow → background task
automation events ──AutomationEventBridge──▶ (automations run first, then the spine hears the same event)
```

## Entity refs

`EntityRef` (`src/Jarvis.Application/Events/EntityRef.cs`) writes any owner-scoped thing as `type:id`, for example
`task:0190…` or `reminder:0190…`. Types live in `EntityTypes`. The same form is used by:

- events (`JarvisEvent.Subject`),
- links (`entity_links`),
- agent tool results and the per-turn situation, and
- the Flutter client (`apps/mobile/lib/features/entities/entity_ref.dart`; destination `entity:type:id`).

A ref never grants access. Every reader resolves it with the owner's id (`IRelatedEntityService.DescribeAsync`).

## Events

| Piece | Where |
|-------|-------|
| `JarvisEvent`, `JarvisEventKinds`, `EventOrigin`, `IJarvisEventBus`, `IJarvisEventHandler` | `src/Jarvis.Application/Events/JarvisEvents.cs` |
| Bus implementation (handlers run in order, one failing never stops the others) | `src/Jarvis.Infrastructure/Events/JarvisEventBus.cs` |
| Storage (`owner_events`, pruned after 90 days by `OwnerEventRetentionWorker`) | `src/Jarvis.Infrastructure/Persistence/OwnerEventRepository.cs` |
| Registration | `src/Jarvis.Infrastructure/Events/EventSpineRegistration.cs` (both hosts) |

Publish with `bus.TryPublishAsync(...)` **after** your own transaction commits, so a failure never reaches the caller.
Current publishers:

- Reminder due, task failed: `WorkflowRepository`
- Watch fired: `ConditionWatchRepository`
- Approval requested and decided: `ToolApprovalStore`
- Commitment created: `CommitmentService`
- Mission step done or failed: `MissionService`
- Heartbeat check-in: `HeartbeatService`
- Autonomous action (`agent.acted`): `ApprovalPolicy`
- The seven automation event kinds, through `AutomationEventBridge`:
  - task completed
  - file uploaded
  - journal saved
  - expense logged
  - inbox needs reply
  - WhatsApp message received
  - webhook

`EventOrigin` says who caused the event: `Owner`, `Agent`, `AgentReaction` or `System`. Summary and data are untrusted
text and are always fenced when they reach a prompt.

## Links and "related"

`IRelatedEntityService` (`src/Jarvis.Infrastructure/Events/RelatedEntityService.cs`) answers what is related to a thing.
It merges two sources:

1. Stored `entity_links`, drawn by the agent (`LinkEntities`), by the owner (`POST /entities/links`), or automatically
   between a chat and what Jarvis made in it.
2. Links the features already keep as columns:
   - a reminder's conversation,
   - a commitment's reminder or thread,
   - a journal entry's memory,
   - a memory's source chat,
   - a mission's tasks,
   - an expense's receipt,
   - a project's chats.

## What the agent gets

- **Situation** (`src/Jarvis.Agents/Situation/SituationContext.cs`, order 5). Every turn sees, each line with its ref:
  - reminders due in the next 24 hours,
  - pending approvals,
  - decisions due for review,
  - routine suggestions,
  - unread notifications,
  - the last 12 hours of events.
- **Cross-feature tools** (`ConnectedAgentTools.cs`):
  - `SearchEverything`, `GetRelated`, `LinkEntities`, `ListRecentEvents`
  - `ListNotifications`, `MarkNotificationRead`
  - `ListProjects`, `CreateProject`, `AssignToProject`
  - `ListAssistantProfiles`, `GetWeeklyReview`
- **Result refs.** `ToolResultRefs` reads the things a tool made from its result ("reminder ID …", `task:…`). The
  `tool.completed` SignalR event carries `toolCallId` and `refs`, and the app turns them into result cards.

## Reactions

`AgentReactionPolicy` (`src/Jarvis.Application/Events/AgentReactions.cs`) decides which events Jarvis follows up on by
itself:

- watch fired,
- task failed,
- commitment created (not mere suggestions),
- inbox needs a reply,
- mission step failed.

It refuses in these cases:

- the event was caused by a reaction, or concerns a task a reaction started (the loop guard);
- autonomy is off, reactions are off, or the level is `ask_everything`;
- it is quiet hours;
- `MaxReactionsPerDay` is used up.

`AgentReactionHandler` signals the per-owner Temporal `AgentReactionWorkflow` (`src/Jarvis.Workflows/AgentReactionWorkflow.cs`).
The workflow gathers events for two minutes, then calls the `RunAgentReaction` activity. `AgentReactionRunner`
re-checks every event and starts **one ordinary background task** whose prompt fences the events as data and asks
Jarvis to do nothing, prepare, or act. Because it is a normal task, the usual approval rules apply.

## Autonomy levels

`AutonomySettings.Level` (`src/Jarvis.Application/Settings/AutonomySettings.cs`, evaluated in `ApprovalPolicy`):

| Level | Read-only | Reversible change | Outside action (send, browse, integration) | Delete / private read / unknown |
|-------|-----------|-------------------|--------------------------------------------|----------------------------------|
| `ask_everything` | asks | asks | asks | asks |
| `standard` | auto | asks | asks | asks |
| `full` (default) | auto | auto in background tasks | asks | asks |
| `autonomous` (opt-in) | auto | auto everywhere | auto **only** in background runs, **only** in categories listed in `AutonomousOutboundCategories`, within `MaxAutonomousOutboundPerDay` | asks |

Only the categories in `AutonomousOutboundCategories.Eligible` can be allowed: WhatsApp, automation messages, the
browser, integration tools, opening links, and other agents. Changing integrations and coding Jarvis itself can never
run unattended. Every non-read-only automatic approval is audited and emits `agent.acted`, so it shows up in Activity.
`Enabled = false` remains the single kill switch.

## HTTP

- `GET /api/v1/events?since=&kinds=&origins=&subject=&limit=`: the owner's activity feed.
- `GET /api/v1/entities/{type}/{id}/related`: a thing's title and everything related to it.
- `POST /api/v1/entities/links`, `DELETE /api/v1/entities/links?from=&to=&relation=`: links the owner draws.
- `GET /api/v1/settings/autonomy/outbound-categories`: categories the autonomous level can allow.

## Adding to it

- **New feature event:** add a kind to `JarvisEventKinds`, publish it after commit with a `Subject` ref, and add it to
  `AgentReactionPolicy.ReactiveKinds` only if a background follow-up is genuinely useful.
- **New entity type:** add it to `EntityTypes`, to `RelatedEntityService.DescribeAsync` (owner-scoped), to the Flutter
  `entityTypes` map, and, if it has columns that link it, to `ImplicitAsync`.
