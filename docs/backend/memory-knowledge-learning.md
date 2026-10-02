# Memory, knowledge graph, and learning

Jarvis combines **structured memory**, **semantic search**, a **temporal knowledge graph**, and **offline learning** (heartbeat + dreaming).

## Memory store

- PostgreSQL tables with **full-text** and **trigram** indexes; optional **pgvector** embeddings for semantic search.
- Owner-scoped categories (preference, fact, project, …).
- **Pin** and **expiry**; **supersede** chain for explicit corrections (unpinned targets only).
- Chat extraction (`IConversationMemoryExtractor`) deduplicates and transactionally supersedes on user corrections.

### Search pipeline

1. **Query analysis** (`MemoryQuery`): chat turns are full sentences, so stopwords (Dutch and English) are dropped, the
   remaining terms are OR-ed instead of all being required, terms of four or more letters become prefix queries after
   light suffix stripping (`training` → `train:*`, `woon` also finds `woont`), and a small Dutch–English lexicon adds
   the other language's word at half weight.
2. **Lexical retrieval** (`MemoryRepository.SearchLexicalAsync`, one round trip): every term is weighted by its inverse
   document frequency within the owner's active memories and by memory length (BM25-style), normalised so 1 means all
   original terms matched. Terms the keyword match missed get a pg_trgm word-similarity score, which catches typos and
   Dutch compounds (`gesprek` in `salarisgesprek`). Each term is looked up through the GIN indexes.
3. **Semantic retrieval** (when an embedding model is configured): after lexical retrieval completes, the embedding
   model is resolved and the query is embedded, then pgvector returns cosine similarities. These operations are awaited
   sequentially because model settings, embedding usage recording, and memory repositories share a scoped database
   context. If the embedding provider is unavailable, keyword results still answer.
4. **Hybrid ranking** (`MemoryRanking`): keyword and semantic relevance are fused as a probabilistic OR, then scaled by
   importance, a recency curve (90-day half-life) that recall refreshes, and a log-scaled recall count. A
   score-adaptive cut drops hits below 35% of the best one, near duplicates are skipped, and at most eight remain.
5. **Recall tracking**: memories that reach the model get `access_count` and `last_accessed_at` updated, which feeds
   ranking and dreaming (also in the worker process).
6. **Reranking**: chat context (`PersonalMemoryContextProvider`) never waits on a model rerank. The `SearchMemory` tool
   pulls a wider pool (15 candidates), and the background model (8s timeout, `MemoryReranker`) keeps at most eight that
   help and orders them best first. An unusable answer falls back to the hybrid order.
7. **Search hints**: the background indexer (`MemoryIndexer`, `SearchHintGenerator`) asks the background model for three to
   five questions plus synonyms (Dutch and English) per memory and stores them in `memories.search_hints`. The hints
   are part of the generated `search_vector` and of the text that gets embedded (`MemoryText.ForIndex`), so a question
   that shares no words with the memory still finds it. Hints are never shown to the model or the user, memories that
   look like secrets get none, and editing a memory clears them so they are regenerated. Existing memories are
   backfilled in the background, newest first; until then they are searched as before.
8. **Follow-ups**: when the latest user message has three or fewer content words, `PersonalMemoryContextProvider` also searches
   with the previous user message in front and merges both result lists (`MemoryRanking.MergeWithContext`, context hits at
   weight 1.2), so "and when is that?" finds the topic while an unrelated previous message cannot push out the message's
   own hits.
9. Pinned unexpired memories are always included in the bounded agent context.

Retrieval quality and speed are measured offline with `tests/eval/Jarvis.MemoryEval` (see its README).

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

Dreaming stages **every** active memory (up to 500) for near-duplicate detection, and counts persisted recalls
(`access_count`, `last_accessed_at`; a recall on a later day counts as a second source). The REM model call reviews a
bounded set: memories written or changed since the last dream first, then the strongest staged candidates.

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
