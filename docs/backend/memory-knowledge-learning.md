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
- Extraction sees today's date in the owner's time zone (the daily briefing zone, UTC otherwise) and writes relative
  dates as real ones ("next month" becomes the month and year). A temporary situation ("in Lisbon until Wednesday") is
  stored with `validUntil` at the end of its last local day, at most a year out, and never replaces a lasting memory; a
  temporary memory without a usable end date is not stored. Extraction quality is measured with
  `tests/eval/Jarvis.ExtractionEval` (see its README).

### Search pipeline

1. **Query analysis** (`MemoryQuery`): chat turns are full sentences, so stopwords (Dutch and English) are dropped, the
   remaining terms are OR-ed instead of all being required, terms of four or more letters become prefix queries after
   light suffix stripping (`training` → `train:*`, `woon` also finds `woont`), and a small Dutch–English lexicon adds
   the other language's word at half weight.
2. **Lexical retrieval** (`MemoryRepository.SearchLexicalAsync`, one round trip): every term is weighted by its inverse
   document frequency within the owner's active memories and by memory length (BM25-style), normalised so 1 means all
   original terms matched. Terms the keyword match missed get a pg_trgm word-similarity score, which catches typos and
   Dutch compounds (`gesprek` in `salarisgesprek`). Each term is looked up through the GIN indexes.
3. **Semantic retrieval** (when an embedding model is configured): the embedding model is resolved first
   (`IMemoryEmbedder.ResolveAsync`, the only step that reads the scoped database context), then the query is embedded
   while the keyword query runs, and pgvector returns cosine similarities. Usage recording uses its own scope, so the
   provider call never touches the shared context. Query embeddings are cached in-process for 15 minutes
   (`QueryEmbeddingCache`, keyed by owner, model and text, at most 512 entries), so a regenerated reply or a
   `SearchMemory` call repeating the chat context's query costs no second call. Only vectors from the same model are
   compared. If the embedding provider is unavailable, keyword results still answer.
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

Retrieval quality and speed are measured offline with `tests/eval/Jarvis.MemoryEval`, extraction decisions with
`tests/eval/Jarvis.ExtractionEval` (see their READMEs).

Agent tools: `SearchMemory`, `ListMemories`, `Remember`, `Forget` (approval). See [agent-tools.md](agent-tools.md).

HTTP CRUD: `/api/v1/memory/*` (`MemoryEndpoints`).

## Uploaded files

Files (PDF, text, JSON, images) are indexed by the worker (`FileProcessingWorkflow`, `StoredFileProcessor`) and searched
with the same building blocks as memory.

**Indexing**

- Text is extracted with PdfPig (pages joined by a blank line, with each page's start offset kept) or, for images, by the
  vision model. At most 300,000 characters are kept.
- `FileTextChunker` cuts windows of 1,600 characters with 200 overlap, ending at a paragraph break, sentence end, line
  break or space in the window's second half. Each chunk stores its offsets, the page its first character is on, and
  the nearest Markdown heading above it (`file_content_chunks.heading`, part of the keyword index).
- When extraction or the chunk limit cut text off, the file is marked `partiallyIndexed`; the API returns it, the
  Files screen shows "Partly indexed", and `ListFiles` tells the model.
- `FileChunkIndexer` (run by `MemoryIndexingWorker` every 15 seconds) embeds chunks with the owner's embedding model,
  file name, page and heading in front of the passage (`FileChunkText.ForIndex`). It backfills older files, re-embeds
  chunks after a model change (`embedding_model`), and does nothing without an embedding model. Files indexed before
  page numbers and headings existed get them when they are indexed again (Retry indexing).

**Search** (`FileSearchService`)

1. Keyword retrieval (`FileContentRepository.SearchTextAsync`): the query is analysed by `MemoryQuery` (stopwords
   dropped, OR-ed terms, prefixes, Dutch–English lexicon) and scored BM25-style over the owner's ready chunks in scope.
   Only when the keywords find fewer than three chunks do original terms that no chunk contains get a pg_trgm
   word-similarity pass (typos, compounds), capped at 100 candidates per term, because long chunks make it costly.
2. Semantic retrieval when the owner has an embedding model and embedded chunks in scope; the query embedding runs
   alongside the keyword query. pgvector iterative HNSW scans keep owner and scope filters from shortening the list.
3. `FileRanking` fuses both like `MemoryRanking` (probabilistic OR, relative 35% cut, at most eight) without the memory
   priors. Scores are 0–1, so file hits now rank fairly against other results in `SearchEverything`.
4. `SearchFiles` pulls 15 candidates and `FileReranker` (background model, 8 s timeout) keeps up to eight, best first;
   an unusable answer falls back to the hybrid order.
5. When a conversation has attached files or collections, `FileContextContributor` searches the latest user message
   in those sources before every turn and adds up to three passages scoring at least 0.25 (1,500 characters each) with
   their chunk ids and citations. It never reranks, and a failed search only drops the excerpts.

Quality and speed are measured with `tests/eval/Jarvis.FileEval` (see its README).

## Journal

Owner-scoped daily journal (`journal_entries`): free text, highlights, gratitude, tags, and optional ratings (day 1–10; mood, energy, stress 1–5). Entries are **written** in Journal (Everything) or **told to Jarvis** in chat or voice, where `SaveJournalEntry` appends to the day's entry instead of creating duplicates.

Each entry is mirrored into memory (`kind: journal`, `sourceType: journal`, `sourceId` = entry id, `journal_entries.memory_id` = link) by `JournalService` (`Jarvis.Memory`), so `SearchMemory`, agent context, and dreaming can reference it. Edits rewrite the linked memory (recreating it if the user deleted it); deleting an entry deletes its memory. If the mirror fails the entry is still saved and the next edit retries. Dreaming reads journal memories as signals but never merges, supersedes, or dedupes them.

Code: `Jarvis.Application/Journal/`, `Jarvis.Memory/JournalService.cs`, `Jarvis.Infrastructure/Persistence/JournalRepository.cs`, `Jarvis.Agents/Journal/`, `JournalEndpoints`, `apps/mobile/lib/features/journal/`.

## Knowledge graph

Entities (people, places, projects, …) with facts, relations, and timelines.

- Extracted from conversations (`KnowledgeGraphExtractor`).
- Chat tools propose writes behind approval; owners edit via API/graph UI.
- Mobile map: `apps/mobile/lib/features/memory/knowledge_graph_*.dart`. Nodes are sized by connections and coloured
  per type; circles of related entities (found without the "You" hub, `graphCommunities` in `graph_model.dart`) get a
  shared halo and pull together in the layout. Selecting a node highlights its two-step neighbourhood and rings its
  literal details around it. "Time travel" replays the graph by fact `validFrom`; the sheet's Highlights list hubs,
  circles, and recently learned facts.
- Scale: `GET /api/v1/graph/overview?limit=` returns up to `KnowledgeGraphLimits.MaxOverviewEntities` (1000) entities;
  edge and literal caps grow with the limit. The map lays out graphs above 250 nodes on a background isolate with a
  grid-accelerated force layout, paints minor nodes on a canvas once more than 160 are on screen (selection,
  neighbours, labelled hubs, and search hits stay widgets), shrinks dots with density, and pages the entity list.
- Layout: the map fills the screen under a floating search and filter bar; memory status sits behind the info button.
  The bottom sheet starts with "Ask about your graph" (`POST /graph/ask`, see
  [api-reference.md](api-reference.md)): suggestions use real names, answers light up the entities and links they
  cite, and follow-ups carry the earlier turns.
- 3D: the cube button switches to `_Graph3DView` (`knowledge_graph_3d.dart`), a perspective view of `layoutGraph3d`
  that turns slowly when idle (not when animations are disabled), rotates on drag, zooms on pinch, pans with two
  fingers, and orbits the selected node. The choice is remembered in shared preferences.

Embeddings and graph persistence live in Infrastructure + `Jarvis.Memory` helpers.

## Skills

Owner-scoped `SKILL.md` documents import/export via `/api/v1/skills`. Agent loads skills with `LoadSkill` before following procedures. Continuous learning can auto-create skills when enabled.

**Review.** A learned change to an *active* skill (reflection, `SaveSkill`) returns it to `proposed` unless the owner turned on *Activate new skills right away*; the earlier text stays in the revision history. Edits by the owner and accepted improvements keep it active.

**How a skill is doing.** Each night `SkillOutcomeEngine` counts the turns older than 6 hours that loaded a skill, against its current version: a run goes well when it finished, none of its tools failed, and it was not rated down, asked for again or corrected (`TurnFeedback`). The counts (`outcome_runs`, `outcome_successes`) are kept on the skill, reset when its text changes, and returned by `GET /api/v1/skills` as `outcomeRuns` and `outcomeSuccesses`. The skill list shown to the model is ordered by the lower end of the Wilson interval of the share that went well once a skill has 3 runs; one with fewer sits at a neutral score, so proven skills rise and poor ones fall below untested ones.

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
(`access_count`, `last_accessed_at`) as usefulness signals, not independent confirmation of truth. The REM model call reviews a
bounded set: memories written or changed since the last dream first, then the strongest staged candidates.

Settings: `/api/v1/settings/learning`.

## Signals, traces and improvement proposals

Jarvis learns from how replies land, not only from what the owner says. All of it is owner-scoped, switchable (`captureSignals`), and stores **ids, tool names and counts only**, never message text, notes, excerpts or tool arguments.

- **Signals** (`learning_signals`, kept 90 days): `regenerate`, `approval_denied`, `thumbs_up`, `thumbs_down`, `correction` (a deterministic detector on a short user message that pushes back on the reply before it; no model call, no text kept) and `tool_failure` (tool name and exception type). They have no foreign key to messages, because regenerate deletes the reply and must not erase its own signal. Recording is fire-and-forget (`ILearningRecorder`) and never fails a turn.
- **Run traces** (`turn_traces`, kept `traceRetentionDays`, default 30): per interactive or task run, which memories were injected, which skills were loaded, and each tool call's name, outcome and duration. A plain reply that used nothing is not kept. Each trace also keeps the run's model-call count and input, cached-input and output tokens for the conversation's own calls (`TurnUsage`; null on older rows), so a turn's cost and prompt-cache hit ratio can be read per run.
- Nothing is written for a conversation whose assistant profile does not contribute to learning (`ProfileScope.ContributesToLearning`). Memory extraction, reflection, dreaming and feedback processing all respect the profile's `AllowsRemember` and `AllowsPersonaLearning`, and memories they create carry the source profile.
- Both tables are pruned in the nightly dreaming chain.

**Proposals** (`improvement_proposals`, modelled on `routine_suggestions`: unique per owner and `fingerprint`, sticky refusals, pending rows follow the data, at most one `improvement.suggested` notification per week with a generic body). Kinds:

| Kind | Source | Accept does |
|------|--------|-------------|
| `memory` | Reflection and dreaming candidates | Saves the memory under its source profile. Confidence >= 0.8 with `autoApplyLowRiskMemory` on (and no secret match) is saved at once as `applied` with an `improvement.applied` audit event and can be undone; 0.6 to 0.8 wait as `pending`; lower is dropped |
| `skill` | **Skill miner**: a tool sequence of 2 to 5 tools that worked at least 3 times across at least 2 conversations in 30 days (fingerprint `seq:{tool>tool>tool}`). The background model drafts the instructions from tool names only; the draft is validated and screened for secrets; at most 2 drafts a night | Saves it as a learned, active skill. Undo disables it |
| `review` | **Review miner**: a skill or memory present in at least 3 thumbs-down replies and at most 1 thumbs-up, or a tool failing at least 5 times with a failure rate above 50% over 7 days | `disable_skill` turns the skill off (undo turns it back on); `note` only records that the owner has seen it. Evidence only: retrieval ranking is never changed |
| `lesson` | **Lesson miner**: a tool that was part of at least 2 turns in 7 days that went wrong (the tool failed, the owner corrected the reply, asked for it again, rated it down, or declined its approval). A correction is matched to the reply just before it in that conversation. The background model drafts one imperative sentence (240 characters at most) from the tool name, counts, reasons and error kinds only; at most 3 drafts a night, none for a tool that already has a lesson in force | Puts the lesson in force. At 3 or more incidents (confidence 0.8) and with `autoApplyLowRiskMemory` on it is `applied` at once and audited; otherwise it waits as `pending`. Undo takes it out. At most 20 are in force |
| `skill_refine` | **Skill refiner**: an active, unlocked skill with at least 3 counted runs of which at most 60% went well. The background model redrafts its description and instructions from its own text plus, from the runs, tool names, counts and error kinds only; at most 2 redrafts a night; one proposal per skill version | Never applied on its own. Accepting rewrites the skill only while it is still at the version the redraft was written for; undo restores the earlier text as a new revision. Locked (owner-written) skills are never offered |
| `capability` | **Gap miner**: a `capability_gap` label reported in at least 2 conversations within 30 days. With `autonomousEvolution`, the nightly builder may attach a tested self-built app (see [Self-evolution](#self-evolution)) | With a tested app: activates exactly that draft; undo pauses it. Without one: records that the owner wants it, and the builder takes it first |

Dismissed and undone fingerprints never come back (tool reports reopen after a month, since a tool can break again). Skill and review proposals never apply on their own.

**Lessons in turns.** `LessonContextContributor` (order 22) shows the lessons in force whose tools the turn can use (a lesson about a feature tool only while its tool group is offered, one about an always-on tool always), best rated first, at most 5 and 1,200 characters, as untrusted working notes. The ids shown are kept on the run trace (`lesson_ids_json`). Each night, before mining, `LessonCounterEngine` counts the traces older than 6 hours that showed a lesson: a thumbs-up is *helpful*; a thumbs-down, a regenerate or a correction is *harmful*; unrated turns and tool failures count for neither. The counts live in the lesson's payload with a `countedThrough` mark so a turn is never counted twice. A lesson with at least 3 harmful turns and more harmful than helpful is switched off (`improvement.lesson_disabled`); one not shown for 60 days is retired (`improvement.lesson_retired`). Its fingerprint carries the month, so a closed lesson for the same tool can return the next month. The miners run after the routine refresh in the dreaming chain, each step in its own try/catch, gated by `proposeImprovements`.

Reflection also receives counts of corrections, regenerations and declined approvals since the last review (`reply_signals`, never text) as a hint for tone and format learning.

Settings (`learning`): `captureSignals`, `traceRetentionDays`, `proposeImprovements`, `autoApplyLowRiskMemory`,
`autonomousEvolution` (default off, see below).

## Self-evolution

Besides the nightly miners, Jarvis improves itself during turns and, when allowed, overnight:

- **Skills know their tools.** A skill can list up to 10 `requiredTools` (exact tool names, `skills.required_tools`).
  `AgentToolCatalog` holds the tools the current agent can call; the skills block marks a skill whose tools are missing
  as unavailable, `LoadSkill` refuses it with the missing names, and `SaveSkill` refuses tool names the agent cannot
  call. Changing only the tool list updates it without a new revision. After `ActivateCustomMcpServer`, Jarvis is told
  to save a skill naming the new app's tools, so later turns find it.
- **Self-insight.** `ListSkills` shows version, uses and "went well x of y"; `RetireSkill` turns off a learned, unlocked
  skill (audited `skill.retired`, the owner is notified and can switch it back on). `ListLessons` shows the lessons in
  force with their helped/hurt counts; `RecordLesson` lets Jarvis write one itself after a tool went wrong. It goes
  through `ProposeLessonAsync` with the miner's fingerprint (`lesson:tool:{tool}:{month}`) at confidence 0.8, so it is
  in force at once only with `autoApplyLowRiskMemory`, one per tool, and the nightly counter switches it off if it hurts.
- **Capability gaps.** When nothing can do what was asked, Jarvis calls `ReportCapabilityGap`. The signal
  (`capability_gap`) keeps only a short label in `category` (`CapabilityGapRules.NormalizeSlug`: 3 to 48 characters, at
  most six words, no runs of four or more digits, no `@` or URLs). The miner offers a gap reported in at least 2
  conversations within 30 days as a `capability` proposal (fingerprint `gap:{label}`). Accepting one without an app
  records that the owner wants it; accepting one with a tested app activates exactly that draft's hash
  (`CustomMcpServerManager.ActivateAsync`), and undo pauses the app.
- **Autonomous evolution** (`autonomousEvolution`, off by default, needs `proposeImprovements`). After each dream the
  `RunCapabilityEvolution` activity (its own 20-minute budget, never retried, behind the `capability-evolution` workflow
  patch) picks one open gap: accepted before pending, then most conversations; at most 3 attempts, a week apart.
  `ModelCapabilityBuilder` asks the reasoning model for one self-built MCP server (it sees only the label and the
  template), saves it through the normal `WriteCustomMcpServer` path under a name no other app uses, tests it in the
  isolated MCP runner (calling a tool only when it needs no keys) and feeds errors back, at most 3 rounds. A passing
  draft is filed on the proposal (`buildStatus: built`, app id, hash, tools, keys) and the owner is notified
  (`improvement.capability_built`); the model may also answer `not_feasible`. Nothing is ever switched on without the
  owner accepting. Audited as `improvement.capability_attempted` with ids and outcome only.

## Usage tracking

`IMemoryRecallTracker` and usage endpoints feed the personalization level on the usage dashboard. `UsageDashboard.Improvement` (pure `ImprovementMetrics.Compute`) adds the "Jarvis is improving" card: thumbs this period against the previous one with a trend, regenerate rate, declined approvals, the three tools that fail most, learned skills, and proposal counts with the acceptance rate. It is optional so older clients ignore it.

## Code map

| Concern | Path |
|---------|------|
| Application contracts | `src/Jarvis.Application/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Agents | `src/Jarvis.Agents/Memory/`, `Learning/`, `Persona/`, `Skills/` |
| Infrastructure persistence | `src/Jarvis.Infrastructure/Persistence/` |
| Dedicated memory package | `src/Jarvis.Memory/` |

### Nightly memory quality review

Before consolidation, a separate tool-free Codex CLI judge reviews the active memory bank and graph, including old
records. It defaults to `gpt-6.1-sol` with `high` reasoning, independent of the interactive provider/model. Its
transport uses `CodexAccess.Strict`: no shell, web search, native MCP, file writes or tool network access. It shares
the process limiter at background priority and records token usage. A failed/unavailable model records `failed`;
there is no silent model substitution or false “clean dataset” result.

Configure `Memory:Review:Enabled` (default true), `Model`, `ReasoningEffort`, and `TimeoutSeconds` (default 180,
clamped 10–300). No extra API key is needed; it uses the installed CLI's signed-in session. Review findings are
owner-scoped under `memory-quality-review`, exposed as `memoryReview` on `GET /api/v1/learning/status`, and counted
in the dream diary. They cite memory/entity IDs and propose corrections; they are not executable actions or sources
of facts. The review is bounded to 500 complete memories / 140,000 memory characters and the union of 200 connected and 200 recent graph entities (including orphans); partial
coverage is explicit. Invalid responses are failures. IDs outside the supplied owner-scoped set are rejected.

The consolidation prompt prioritizes cited issues but must verify them against source text. A semantic merge can
name up to eight `absorbedMemoryIds` plus `evidenceMemoryIds`; the replacement must preserve all supported details.
Absorption requires confidence ≥0.95, valid evidence IDs, matching kind/profile, and unpinned, non-journal, timeless
records. An explicit supersede requires a separate newer evidence memory. Old records expire and their derived graph
facts close; they are not hard-deleted. `AutoApplyLowRiskMemory=false` blocks automatic merges/supersedes and duplicate
expiry. Findings remain available for review. The default portrait omits transient setup blockers and unresolved
contradictions. Graph extraction uses aliases and unambiguous parent/child/sibling directions; persistence reuses
known aliases so merged identities do not reappear on the next indexing pass.

Dreaming graph facts must cite a `sourceMemoryId` from the supplied active memories. Consolidation carries those
references to the replacement memory and rejects unsourced/unknown IDs, instead of creating provenance-free facts
with a fresh nightly timestamp. Ordinary file/graph indexing continues to attach its own memory source.
