# Memory, knowledge graph, and learning

Jarvis combines **structured memory**, **semantic search**, a **temporal knowledge graph**, and **offline learning** (heartbeat + dreaming).

## Memory store

- PostgreSQL tables with **full-text** and **trigram** indexes; optional **pgvector** embeddings for semantic search.
- Owner-scoped categories (preference, fact, project, …).
- **Pin** and **expiry**; **supersede** chain for explicit corrections (unpinned targets only).
- Chat extraction (`IConversationMemoryExtractor`, behind `ConversationMemoryGate`) reads the user message together with
  the four turns before it (long turns keep their last 1,000 characters), so a short reply such as "no, Thursdays now"
  is understood; a reply of eight words or fewer is also searched together with the end of the turn it answers. Memories
  still come only from what the user said. For each candidate the model picks an action against the related memories:
  `add`, `duplicate` (skipped), `enrich` (replaced by the combined statement, importance never lowered, audited as
  `memory.enriched`), `supersede` (an explicit correction, `memory.superseded`) or `expire` (the user says it no longer
  holds; the memory ends without a replacement and its graph facts close, confidence ≥ 0.9, `memory.expired`). Enrich,
  supersede and expire never touch pinned memories, and replacements keep the recall counts.

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

Owner-scoped daily journal (`journal_entries`): free text, highlights, gratitude, tags, and optional ratings (day 1–10; mood, energy, stress 1–5). Entries are **written** in Journal (Everything) or **told to Jarvis** in chat or voice, where `SaveJournalEntry` appends to the day's entry instead of creating duplicates.

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
| **Heartbeat** | Temporal `AssistantHeartbeatWorkflow` (on by default; opt out in Settings → Learning) | Memories, persona tweaks, skills. Learned skills start as `proposed` and wait for review unless the owner turns on *Activate new skills right away* (`AutoActivateSkills`). Reflection `insights` (up to 2 follow-ups) are offered once as check-ins and are never stored or audited. The heartbeat also keeps the inbox triaged and can start meeting-prep tasks; see [the autonomy envelope](life-features.md#autonomy-envelope) |
| **Dreaming** | Nightly `AssistantDreamingWorkflow` | Memory promotion/merge, persona, graph facts, **portrait** string for system prompt |
| **Manual** | `POST /learning/run`, `/learning/dream` | On-demand for testing |

Dream diary and portrait are visible under Settings → Learning; diary text is **not** used as a promotion source.

Dreaming stages **every** active memory (up to 500) for near-duplicate detection, and counts persisted recalls
(`access_count`, `last_accessed_at`; a recall on a later day counts as a second source). The REM model call reviews a
bounded set: memories written or changed since the last dream first, then the strongest staged candidates.

Settings: `/api/v1/settings/learning`.

## Signals, traces and improvement proposals

Jarvis learns from how replies land, not only from what the owner says. All of it is owner-scoped, switchable (`captureSignals`), and stores **ids, tool names and counts only**, never message text, notes, excerpts or tool arguments.

- **Signals** (`learning_signals`, kept 90 days): `regenerate`, `approval_denied`, `thumbs_up`, `thumbs_down`, `correction` (a deterministic detector on a short user message that pushes back on the reply before it; no model call, no text kept) and `tool_failure` (tool name and exception type). They have no foreign key to messages, because regenerate deletes the reply and must not erase its own signal. Recording is fire-and-forget (`ILearningRecorder`) and never fails a turn.
- **Run traces** (`turn_traces`, kept `traceRetentionDays`, default 30): per interactive or task run, which memories were injected, which skills were loaded, and each tool call's name, outcome and duration. A plain reply that used nothing is not kept.
- Nothing is written for a conversation whose assistant profile does not contribute to learning (`ProfileScope.ContributesToLearning`). Memory extraction, reflection, dreaming and feedback processing all respect the profile's `AllowsRemember` and `AllowsPersonaLearning`, and memories they create carry the source profile.
- Both tables are pruned in the nightly dreaming chain.

**Proposals** (`improvement_proposals`, modelled on `routine_suggestions`: unique per owner and `fingerprint`, sticky refusals, pending rows follow the data, at most one `improvement.suggested` notification per week with a generic body). Kinds:

| Kind | Source | Accept does |
|------|--------|-------------|
| `memory` | Reflection and dreaming candidates | Saves the memory under its source profile. Confidence >= 0.8 with `autoApplyLowRiskMemory` on (and no secret match) is saved at once as `applied` with an `improvement.applied` audit event and can be undone; 0.6 to 0.8 wait as `pending`; lower is dropped |
| `skill` | **Skill miner**: a tool sequence of 2 to 5 tools that worked at least 3 times across at least 2 conversations in 30 days (fingerprint `seq:{tool>tool>tool}`). The background model drafts the instructions from tool names only; the draft is validated and screened for secrets; at most 2 drafts a night | Saves it as a learned, active skill. Undo disables it |
| `review` | **Review miner**: a skill or memory present in at least 3 thumbs-down replies and at most 1 thumbs-up, or a tool failing at least 5 times with a failure rate above 50% over 7 days | `disable_skill` turns the skill off (undo turns it back on); `note` only records that the owner has seen it. Evidence only: retrieval ranking is never changed |

Dismissed and undone fingerprints never come back (tool reports reopen after a month, since a tool can break again). Skill and review proposals never apply on their own. The miners run after the routine refresh in the dreaming chain, each step in its own try/catch, gated by `proposeImprovements`.

Settings (`learning`): `captureSignals`, `traceRetentionDays`, `proposeImprovements`, `autoApplyLowRiskMemory`.

## Usage tracking

`IMemoryRecallTracker` and usage endpoints feed the personalization level on the usage dashboard. `UsageDashboard.Improvement` (pure `ImprovementMetrics.Compute`) adds the "Jarvis is improving" card: thumbs this period against the previous one with a trend, regenerate rate, declined approvals, the three tools that fail most, learned skills, and proposal counts with the acceptance rate. It is optional so older clients ignore it.

## Code map

| Concern | Path |
|---------|------|
| Application contracts | `src/Jarvis.Application/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Agents | `src/Jarvis.Agents/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Infrastructure persistence | `src/Jarvis.Infrastructure/Persistence/` |
| Dedicated memory package | `src/Jarvis.Memory/` |
