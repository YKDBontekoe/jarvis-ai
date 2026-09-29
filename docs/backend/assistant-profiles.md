# Assistant profiles

Owner-defined **assistant profiles** let one Jarvis installation keep distinct working contexts: persona, enabled skills, document collections, model class, and background-learning policy. A conversation or task binds to a **snapshot** of the profile so later edits have documented behavior.

## Data model

Each owner has at least one profile. The first access creates a default named **Jarvis** that uses global persona, every active skill, every file, owner model settings, and full learning — the previous single-assistant behavior.

| Field | Role |
|-------|------|
| Name / description | Shown in the app and injected as untrusted working context |
| Persona override | Optional instructions, preferred name, reply language; may include or hide learned owner traits |
| Enabled skill IDs | Optional allowlist; disabled or deleted skills simply disappear |
| Allowed collection IDs | Optional document-collection allowlist for file search |
| Model class / IDs | `chat`, `fast`, or `reasoning`, plus optional model id and reasoning effort overlays |
| Memory scope | `all`, `profile` (this profile’s memories), or `pinned` |
| Include pinned memories | Keep globally pinned safety-relevant facts in context when intended |
| Learning policy | Contribute to dreaming, allow persona learning, allow Remember |

Profiles cannot change the owner’s model **provider** (Codex vs OpenRouter), cannot unwrap `ApprovalRequiredAIFunction`, and cannot alter MCP operator allowlists.

## Snapshots

`POST /conversations` and `POST /tasks` capture `ProfileId`, `ProfileVersion`, and `ProfileSnapshotJson` on the conversation. Agent turns use that snapshot. Editing the live profile increments `Version` and does not change already bound threads. Deleting a non-default profile leaves snapshots in place; the chat UI marks the profile as deleted.

`PUT /conversations/{id}/profile` switches the bound snapshot. If enabled skills, collections, or memory scope change, the API returns **409** with a confirmation payload until `confirm: true`.

## Agent assembly

`JarvisAgent` resolves the snapshot into `AgentBuildContext.Profile`. `ProfileContextContributor` injects the profile as a **user-role** context block (not privileged system instructions) and states that it cannot override safety, approvals, or MCP allowlists. Skills, files, memory recall, persona learning, Remember, and the dreamed portrait honor the snapshot. `JarvisAgentFactory.BuildInstructions` stays the shared safety preamble.

## HTTP

| Method | Path |
|--------|------|
| GET/POST | `/api/v1/profiles` |
| GET/PUT/DELETE | `/api/v1/profiles/{id}` |
| GET/POST | `/api/v1/collections` |
| GET/PUT/DELETE | `/api/v1/collections/{id}` |
| POST | `/api/v1/conversations` (`profileId` optional) |
| PUT | `/api/v1/conversations/{id}/profile` |

Conversation list/detail DTOs include `profileId`, `profileName`, `profileVersion`, and `profileDeleted`.

## Client

Settings → **Profiles** creates and edits profiles. New chats reuse the last selected profile; the chat app bar shows the active name. Switching prompts when tools or knowledge scope would change. Task creation can pick a profile.

## Related

- [chat-agent-runtime.md](../architecture/chat-agent-runtime.md)
- [agent-tools.md](agent-tools.md)
- [memory-knowledge-learning.md](memory-knowledge-learning.md)
- [security-and-ownership.md](../architecture/security-and-ownership.md)
