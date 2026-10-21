# Plan: a more self-evolving Jarvis

Status: **proposal**, nothing below is built yet. Each phase is meant to ship as its own PR.

## Where Jarvis already is

Jarvis already improves itself in many narrow loops. This plan builds on them instead of adding a parallel system.

| Area | Today | Code |
|------|-------|------|
| Memory | Chat extraction (add / enrich / supersede / expire), dreaming consolidation, nightly quality review, recall-aware ranking | `Jarvis.Memory`, `Agents/Learning/DreamingService.cs` |
| Skills | `SaveSkill` from chat, heartbeat-learned skills, skill miner (tool sequences), skill refiner (poor outcome rate), Wilson-ranked skill list | `Agents/Skills/`, `Application/Improvements/` |
| Lessons | One-sentence lessons per failing tool, counted helpful/harmful, auto-retired | `LessonMiner.cs`, `LessonContextContributor` |
| Self-built MCP | `WriteCustomMcpServer` → `TestCustomMcpServer` (approval) → `ActivateCustomMcpServer` (approval, hash-pinned), runs only in the MCP runner, saved state | `CustomMcpAgentTools.cs`, `Jarvis.Mcp` |
| Self-fix | `ProposeJarvisFix` opens a PR against Jarvis's own repo; merging only from the app | `SelfFixAgentTools.cs` |
| Signals | `learning_signals`, `turn_traces` (tools, skills, lessons, memories, tokens) — ids and counts only | `Application/Learning/` |

### The gaps

1. **Jarvis only builds tools when asked.** Nothing notices "I could not do X" across conversations and turns that into a
   self-built MCP server proposal.
2. **Self-built servers do not maintain themselves.** A custom tool that starts failing is flagged by the review miner,
   but nothing drafts a fix, and there is no rollback to the last good revision or regression check on a new one.
3. **Skills are plain text with no link to capabilities.** A skill cannot say which tools, tool groups or self-built
   server it needs, so loading it does not bring its tools along, and a skill + server cannot be shipped as one unit.
4. **Skills are mined from tool names only**, never from a whole successful run, and a refined skill replaces the old one
   instead of being compared against it.
5. **No episodic memory of Jarvis's own work.** Memories are facts about the owner; there is no record of "last time I
   did this task, this is what worked / failed", and no self-model of what Jarvis can and cannot do.
6. **No self-optimisation of the runtime.** Tool-group keywords, context blocks and token spend are fixed by code even
   though traces already hold the data to tune them.
7. **Self-changes are scattered.** Skills, lessons, proposals, custom servers and memory merges each have their own
   screen; there is no single ledger, budget or off switch for "Jarvis changing itself".

## Guardrails (apply to every phase)

- **Owner scope** on every new table and query (`ICurrentUser.OwnerId`).
- **Running or activating code always needs approval** (`ApprovalRequiredAIFunction`, risk class Outbound). Jarvis may
  *draft* and *test-plan* on its own; it never activates a server, merges a PR or widens its own permissions.
- **Signals stay text-free** where they are today. New text (gap labels, episode summaries) is written by the background
  model, capped, screened with `MemoryAgentTools.LooksLikeSecret`, and never contains raw user messages.
- **Everything is reversible**: every autonomous change lands as an `improvement_proposals` row (new kinds below) with
  undo, an audit event, and a sticky dismissal fingerprint.
- **Budgets**: per-night caps per miner (as today) plus a weekly cap on autonomous builds.
- **Profiles**: nothing runs for conversations whose profile has `contributeToLearning: false`.

## Phase 1 — Capability gaps → self-built tools

Goal: Jarvis notices what it repeatedly cannot do and offers to build it.

1. **Record gaps.** New always-on tool `NoteCapabilityGap(capability, reason)` (no approval, only writes a signal) that
   the model calls when it has to answer "I can't do that" or falls back to a workaround. `capability` is a short
   model-written label (≤ 80 chars, e.g. "check train delays NS"), validated and secret-screened. Stored as
   `learning_signals.kind = capability_gap` with the label in a new nullable `label` column (the only text a signal may
   carry). Guidance goes in `McpContextContributor`.
2. **Gap miner** (`CapabilityGapMiner`, dreaming chain after the lesson miner): clusters labels (background model, labels
   only) and raises a proposal when a cluster has ≥ 2 gaps in ≥ 2 conversations within 30 days and nothing in
   `SearchMcpCatalog` or the owner's servers already covers it. New kind **`capability`**, fingerprint `gap:{slug}`.
3. **Accept = start a build task.** Accepting creates a `JarvisTaskWorkflow` run with a fixed brief: search the catalog
   first, else `GetCustomMcpTemplate` → `WriteCustomMcpServer` → ask for keys via `AskForMcpCredential` → stop. Test and
   activate still go through their approval cards in the app. Writing is allowed in background tasks only for this
   task type; test/activate stay hidden there.
4. Settings: `learning.buildCapabilities` = `off | propose` (default `propose`).

Tests: miner unit tests (thresholds, dedupe vs catalog, sticky dismissals); task-brief test; e2e flow in
`tests/e2e` with the fake Codex calling `NoteCapabilityGap` twice and the proposal appearing.

## Phase 2 — Self-maintaining self-built servers

1. **Golden calls.** When `TestCustomMcpServer` succeeds with a tool call, store `{toolName, argumentsJson, resultShape}`
   (shape = top-level JSON keys / type, not values) on the revision. Up to 5 per server.
2. **Regression gate.** `ActivateCustomMcpServer` replays the golden calls against the new draft in the runner (inside the
   same approval) and reports which ones changed shape. A failing gate blocks activation unless `overrideReason` is given
   (same rule as today's untested activation).
3. **Rollback.** Keep the previous `active_source` + hash. New tool/endpoint `RollbackCustomMcpServer` (approval) and a
   button on the server page. Auto-suggest rollback when a server's tools go from passing to failing right after an
   activation.
4. **Repair proposals.** New kind **`mcp_repair`** from the review miner when a `custom:<id>` tool fails ≥ 5 times at
   > 50% (existing thresholds). Accept starts a task that reads `GetCustomMcpServerSource` + the last runner
   diagnostics (already secret-scrubbed) and writes a new draft. Activation remains the owner's.
5. **Dependency freshness.** Nightly check of each server's pinned versions against the template's known-good pins
   (`CustomMcpTemplate`); a stale `mcp==` pin raises an `mcp_repair` proposal of subtype `upgrade`.

## Phase 3 — Skills as capabilities

1. **Skill front-matter.** Extend `SkillMarkdown` with optional `tools:` (tool names), `groups:` (tool group ids) and
   `servers:` (`custom:<id>` or registry names). Validated against `ToolGroups` and the owner's servers on save.
2. **Loading a skill brings its tools.** `ToolGroupChatClient` treats a `LoadSkill` call like `LoadTools` for the skill's
   groups (same mechanism as today: worked out from the messages, nothing stored).
3. **Skill packages.** Export/import (`/api/v1/skills`) bundles a skill with the source of its self-built servers.
   Importing creates the servers as **drafts** (never active) and the skill as `proposed`.
4. **Skills from whole runs.** Heartbeat reflection gets a new candidate source: a recent run with ≥ 4 tool calls that
   finished, had no failure/correction/regenerate and (if rated) a thumbs-up. The background model drafts the skill from
   that conversation (it already reads conversations for reflection), with the tool sequence from the trace as the
   skeleton. Goes through the existing `skill` proposal kind.
5. **Champion / challenger.** The skill refiner stops overwriting: an accepted redraft becomes a *challenger* revision.
   `LoadSkill` serves champion or challenger 50/50 (recorded on the trace). `SkillOutcomeEngine` promotes the challenger
   when its Wilson lower bound beats the champion's after ≥ 6 runs each, or drops it after 12 runs without winning.
   Owner-locked skills are never challenged.
6. **Skill consolidation.** Dreaming flags skills whose descriptions overlap strongly (embedding similarity) and proposes
   a merge (new kind **`skill_merge`**), never auto-applied.

## Phase 4 — Episodic memory and a self-model

1. **Episodes.** At the end of a background task, mission, or an interactive run with ≥ 4 tool calls, write one memory of
   new kind `episode`: goal, approach, outcome, what to do differently (≤ 600 chars, background model, from the run's
   own messages + trace). Linked to the skill(s) used. Episodes are searched like other memories but only injected when
   the current request is similar (score ≥ 0.5), at most 2 per turn, as untrusted working notes. Dreaming merges
   repeated episodes into a skill proposal or a lesson instead of keeping dozens.
2. **Self-model block.** A small context block (`SelfModelContextContributor`, gated like lessons) listing what Jarvis
   built or learned recently and known limits: active self-built servers with their success rate, open capability gaps,
   tools that are currently failing. Lets Jarvis say "I built a tool for that last week" or "that tool is broken, I've
   proposed a fix" instead of retrying blindly.
3. **Memory usefulness as a ranking prior (behind a flag).** Today ranking deliberately ignores feedback. Add an optional
   prior from traces (memory injected → thumbs-up vs. thumbs-down/correction), gated by `MemoryEval` showing no
   regression before it is turned on by default.

## Phase 5 — Runtime self-optimisation

1. **Learned tool-group keywords.** From traces: messages where the model had to call `LoadTools` (or called a deferred
   tool) for a group whose keywords did not match. A nightly job keeps per-owner extra keywords (top terms, stopwords
   removed, max 20 per group, decayed after 60 days). Metric: `LoadTools` calls per 100 turns should fall; tool schema
   tokens per turn should not rise more than 10%.
2. **Context cost report.** Record per-contributor injected characters on the trace (counts only). Weekly improvement
   card: contributors that are large and rarely followed by a related tool call, and the prompt-cache hit ratio trend.
   Proposals only (kind `note`), no automatic trimming.
3. **Evolution eval.** New `tests/eval/Jarvis.EvolutionEval`: replays fixture conversations through the fake Codex server
   and checks that gaps, skills, episodes and repairs are proposed (and not proposed) as expected. Run in CI on changes
   under `Improvements/`.

## Phase 6 — One place to see and control it

1. **Evolution ledger** (Settings → Learning → *How Jarvis changed*): one timeline over audit events `skill.*`,
   `improvement.*`, `memory.enriched/superseded/expired`, custom server revisions and self-fix PRs, each with undo where
   it exists. API: `GET /api/v1/learning/evolution?cursor=` reading the audit store (owner-scoped).
2. **Budgets and switch.** `learning.selfEvolution` master switch; weekly cap on autonomous build tasks (default 2) and a
   background token budget shown next to it (usage already tracked).
3. UI follows `docs/design/playbook.md`: mockup first, screenshot case in `app_screenshots.dart`, recording of
   accept → build task → approval card.

## Suggested order and size

| PR | Phase | Size | Depends on |
|----|-------|------|------------|
| 1 | 1.1–1.2 gap signals + miner + `capability` proposals | M | — |
| 2 | 1.3–1.4 accept → build task, settings | M | 1 |
| 3 | 2.1–2.3 golden calls, regression gate, rollback | M | — |
| 4 | 2.4–2.5 repair + upgrade proposals | S | 3 |
| 5 | 3.1–3.3 skill front-matter, tool loading, packages | M | — |
| 6 | 3.4–3.6 run-based skills, champion/challenger, merges | L | 5 |
| 7 | 4.1–4.2 episodes, self-model | M | — |
| 8 | 5.1–5.3 learned keywords, context report, eval | M | — |
| 9 | 6 ledger, budgets, UI | M | 1–4 |
| 10 | 4.3 memory prior (flagged) | S | 8 (eval) |

Every PR: EF migration + integration tests when tables change, unit tests for each miner/engine, a `tests/e2e` flow via
`scripts/dev/smoke.sh` with `fake_codex_app_server.mjs` updated for new tools/context blocks, docs updated in
`docs/backend/memory-knowledge-learning.md`, `agent-tools.md` and `integrations-and-mcp.md`.

## Open questions for the owner

1. Should Jarvis ever **draft** a self-built server without being asked (accept → task), or only propose and wait for you
   to say "build it" in chat?
2. Is a model-written capability label (e.g. "check NS train delays") acceptable in `learning_signals`, which today
   holds no text at all?
3. Champion/challenger means half your skill loads use an unproven version for a while. OK, or only when
   *Activate new skills right away* is on?
4. Should self-fix (`ProposeJarvisFix`) be fed by the gap miner too, for gaps that are Jarvis features rather than
   external APIs?
