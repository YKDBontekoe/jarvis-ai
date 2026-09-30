# Memory, knowledge graph, and learning

Jarvis combines **structured memory**, **semantic search**, a **temporal knowledge graph**, and **offline learning** (heartbeat + dreaming).

## Memory store

- PostgreSQL tables with **full-text** and **trigram** indexes; optional **pgvector** embeddings for semantic search.
- Owner-scoped categories (preference, fact, project, …).
- **Pin** and **expiry**; **supersede** chain for explicit corrections (unpinned targets only).
- Chat extraction (`IConversationMemoryExtractor`) deduplicates and transactionally supersedes on user corrections.

### Search pipeline

1. PostgreSQL FTS + trigram merged via **reciprocal rank fusion**.
2. Up to eight candidates **reranked** via Codex CLI path (8s timeout) with fallback to DB order.
3. Pinned unexpired memories included in bounded agent context (`PersonalMemoryContextProvider`).

Agent tools: `SearchMemory`, `ListMemories`, `Remember`, `Forget` (approval). See [agent-tools.md](agent-tools.md).

HTTP CRUD: `/api/v1/memory/*` (`MemoryEndpoints`).

## Journal

Owner-scoped daily journal (`journal_entries`): free text, highlights, gratitude, tags, and optional ratings (day 1–10; mood, energy, stress 1–5). Entries are **written** in Journal (sidebar) or **told to Jarvis** in chat or voice, where `SaveJournalEntry` appends to the day's entry instead of creating duplicates.

Each entry is mirrored into memory (`kind: journal`, `sourceType: journal`, `sourceId` = entry id, `journal_entries.memory_id` = link) by `JournalService` (`Jarvis.Memory`), so `SearchMemory`, agent context, and dreaming can reference it. Edits rewrite the linked memory (recreating it if the user deleted it); deleting an entry deletes its memory. If the mirror fails the entry is still saved and the next edit retries. Dreaming reads journal memories as signals but never merges, supersedes, or dedupes them.

Code: `Jarvis.Application/Journal/`, `Jarvis.Memory/JournalService.cs`, `Jarvis.Infrastructure/Persistence/JournalRepository.cs`, `Jarvis.Agents/Journal/`, `JournalEndpoints`, `apps/mobile/lib/features/journal/`.

## Knowledge graph

Entities (people, places, projects, …) with facts, relations, and timelines.

- Extracted from conversations (`KnowledgeGraphExtractor`).
- Chat tools propose writes behind approval; owners edit via API/graph UI.
- Mobile map: `apps/mobile/lib/features/memory/knowledge_graph_*.dart`.

Embeddings and graph persistence live in Infrastructure + `Jarvis.Memory` helpers.

## Skills

Owner-scoped `SKILL.md` documents import/export via `/api/v1/skills`. Agent loads skills with `LoadSkill` before following procedures. Continuous learning can auto-create skills when enabled.

## Persona

Traits + custom instructions learned from user statements and reply ratings. Tools record traits (`PersonaAgentTools`); context injected each turn.

## Assistant profiles

Owner-defined profiles snapshot persona, skills, collections, model class, and learning policy onto each conversation. See [assistant-profiles.md](assistant-profiles.md). Dreaming skips conversations whose snapshot has `contributeToLearning: false`. Pinned memories can remain available to isolated profiles.

## Learning loops

| Mechanism | Trigger | Output |
|-----------|---------|--------|
| **Heartbeat** | Temporal `AssistantHeartbeatWorkflow` | Memories, persona tweaks, skills |
| **Dreaming** | Nightly `AssistantDreamingWorkflow` | Memory promotion/merge, persona, graph facts, **portrait** string for system prompt |
| **Manual** | `POST /learning/run`, `/learning/dream` | On-demand for testing |

Dream diary and portrait are visible under Settings → Learning; diary text is **not** used as a promotion source.

Settings: `/api/v1/settings/learning`.

## Usage tracking

`IMemoryRecallTracker` and usage endpoints feed the personalization level on the usage dashboard.

## Code map

| Concern | Path |
|---------|------|
| Application contracts | `src/Jarvis.Application/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Agents | `src/Jarvis.Agents/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Infrastructure persistence | `src/Jarvis.Infrastructure/Persistence/` |
| Dedicated memory package | `src/Jarvis.Memory/` |
