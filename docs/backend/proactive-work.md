# Ongoing proactive work

Jarvis keeps unfinished work in an owner-scoped `proactive_cases` table. Home, Needs you, Everything → Ongoing work,
and a project's Ongoing work entry open the same list and detail screen. Saving tracks a responsibility; enabling
periodic checks is a separate owner choice. A chat tool can enable one when the owner explicitly requests ongoing
monitoring. Background agents can capture suggestions and prepare within an existing responsibility, but cannot
accept, enable, resolve or expand its scope.

## Lifecycle and execution

`suggested` requires acceptance, then `open` can be enabled. `paused` stops checking without losing context.
`stopped`, `dismissed` and `resolved` are terminal. Duplicate capture uses owner, profile, kind, root and normalized
objective text; open loops ignore the root so identical intentions from different chats merge their source references.
This is exact normalized matching, not semantic matching. Terminal fingerprints prevent extraction from resurrecting
finished work; a genuinely new responsibility needs a new objective.

Cases store linked sources, an optional root, a dependency graph of steps, owner completion conditions, monitored
assumptions, deadline and supplied planning capacity, notes, policy, expiry, prepared reports and action receipts.
Arbitrary written stop conditions remain owner-confirmed; only expiry and an explicitly completed linked project or
commitment stop checks automatically. Inactivity, silence, a reply and finishing all steps do not prove the outcome.

`ProactiveCaseDispatcher` repairs the database-to-Temporal handoff every 30 seconds. Stable workflow IDs and a Wake
signal reuse a responsibility's workflow. `ProactiveCaseWorkflow` re-reads state on every activity and continues as new
after 100 checks. Database revisions, a five-minute claim lease, cancellation checks and an atomic final write prevent
duplicate execution and discard preparation invalidated by an owner edit, pause, stop or linked event.

Schedules use the owner's IANA zone. Checks respect quiet hours, a per-case daily check budget, expiry and existing
autonomy settings. `ask_everything` and disabled autonomy defer background checks. `standard` prepares a proposed day
plan; `full` and `autonomous` may apply the supported internal replan. No outbound action, booking, submission, tool
permission, access scope or approval policy is changed by this subsystem.

## Bounded first-release capabilities

| Epic | Available behavior | Boundary |
|---|---|---|
| 1. Open loops | Durable source-linked intentions, conservative EN/NL chat capture, inferred suggestions, lifecycle and deduplication | Prefix capture and exact objective matching; more general extraction uses the agent tool |
| 2. Attention | Shared Needs you grouping, optional daily limit, quiet hours, freshness; overlapping opportunities, heartbeat and reactions are suppressed for active covered sources | Existing operational failures and approvals remain outside optional limits; this is not a replacement for all notification transports |
| 3. Ongoing jobs | Explicit enable, pause, resume, stop; Temporal cadence, daily budget, expiry, restart recovery and last/next checks | One source-bounded check/preparation per run; no permission expansion |
| 4. Receipts | Trigger, evidence versions, affected projection and before/after; supported internal-plan and learned-frequency Undo | Not a universal undo log for pre-existing tools |
| 5. Projects | Dependency steps, completion conditions, eligible next step and a prepared update | Plans are owner-authored; research tasks are created through existing tools |
| 6. Deadline radar | Supplied effort minus supplied available minutes, missing-estimate warnings and a specific scope-reduction proposal that preserves prerequisites | No invented availability/deadlines or external promises; calendar-derived effort forecasting is not implemented |
| 7. Day replanning | Repositions unlocked focus blocks around calendar, working hours, completed and pinned blocks; visible overflow | Preserves external calendar; unknown calendar prevents application; no connected calendar is explicitly labelled |
| 8. Follow-through | Linked inbox evidence distinguishes waiting and a newly received reply; linked commitment remains open until explicitly completed | Select the relevant inbox thread/commitment; reply drafting stays in the existing inbox workflow |
| 9. Meetings | A meeting case defines the selected series; preparation carries unresolved accepted items into the next meeting, refreshes generated agendas, and shows changes since the previous preparation | Series membership is owner-selected. Owner-written agendas and private notes are preserved; extracted commitments still require acceptance |
| 10. Administration | Return, refund and repair step templates; evidence, references, notes and response deadlines in one case | Owner records submissions/outcomes; sending and spending use existing tools/approvals |
| 11. Goal capacity | One shared week allocated in chosen priority order; optional calendar availability uses Today's weekday working hours, and accepting more work rechecks capacity and concurrent owner edits | Calendar use is explicit and owner-wide. All-day events count as busy; missing event durations and retrieval failures stay unknown. Completed work keeps consuming the week even if its goal is paused |
| 12. Trips | An owner-mapped watch shifts a verified itinerary item by observed delay minutes; preparation identifies affected items/connections, proposes revised times, and compares saved alternatives, cancellation terms and preparation checklists | The watch's numeric meaning is explicitly selected. Stale observations and separately edited schedules require review. Prices/availability need verification; bookings and cancellations retain normal approvals |
| 13. Propagation | Linked source events invalidate in-flight checks; refreshed case projections; document source-version staleness preserves owner wording | Owner source links define dependencies; no global dependency graph or automatic document rewrite |
| 14. Context packs | Current permitted evidence and recent preparation attached to a case; chat can read it before continuing | Sources are re-authorized, bounded and selected explicitly; no automatic discovery of every related file |
| 15. Assumptions | Explicit source versions, numeric watch thresholds, review dates and owner acknowledgement | Review never resolves the original decision or changes its prediction |
| 16. Living research | Up to five selected public URLs, cited text changes, owner-selected important claims, cached supported/contradicted/unclear reviews with verified quotations, unknown retrieval failures and five report versions | Claim review uses the existing tool-free Codex transport, is bounded to 45 seconds/6,000 output tokens, and cannot change a claim or resolve an outcome. Ambiguous or unverified classifications remain unknown; source discovery is outside the selected scope |
| 17. Memory conflicts | Existing nightly critic queues same-profile two-memory comparisons as suggestions; Keep first/second/Both with context uses memory correction history | Pinned targets are protected; larger multi-memory conflicts remain in the existing quality review; correction steps may partially succeed and report that explicitly |
| 18. Routine trials | Seven-day observation of draft event/schedule routines; observed skips, actions and approval counts; no action execution | Bounded queries are labelled partial; live external conditions remain unknown; activation is separate |
| 19. Outcome learning | Explicit usefulness/timing/effort feedback; three matching too-early/not-useful answers reduce check frequency with evidence and Undo | Per case and profile; no silence inference or permission learning; delivery-window/format learning is not implemented |
| 20. Recovery | Transient/missing-input/permission/unavailable classifications; bounded retries, durable claims and one case-level request | This subsystem performs no external write; uncertain external outcomes continue through existing task/approval recovery |

## Sources, privacy and concurrency

`IProactiveSourceReader` is implemented in Infrastructure and explicitly projects typed owner-scoped records. The
Application and agent layers do not query EF. Each execution and detail read uses the current profile scope, not the
scope when a case was created. Deleted, expired, superseded and profile-inaccessible records are omitted; stale
preparation and inaccessible report history are withheld. Meeting private notes never enter the pack. Broad unclassified
feature records are unavailable to profiles with restricted memory/file scope. Sources are untrusted reference data.

`IProactiveRepository.CompareExchangeAsync` guards case revisions. `ApplyAsync` supports only `planner.day` and commits
the setting mutation and case receipt in the same PostgreSQL transaction. Undo compares the affected current document
or policy to the saved after-value; a later edit produces HTTP 409 instead of being overwritten. Learned settings never
touch access or approvals. Receipts are bounded to 200, explicit feedback to 100 and report history to five versions.

`policy.useCalendarCapacity` is opt-in for an owner-wide `goal_capacity` case. It uses Monday–Friday and the working
hours saved in Today, in the case's IANA time zone. Calendar failure does not become free time. Goal acceptance rechecks
the capacity policy after calendar lookup and compares-and-exchanges the goal ledger, including completed work.

Trip monitoring uses an assumption with `effect: "delay_minutes"`, a watch `sourceRef`, `tripRef`, and `tripItemId`.
The server captures the verified original date/start/end as a baseline. Applying a previous proposal does not compound
the delay; intervening unrelated schedule edits are flagged. Observations expire after twice the watch interval (at
least an hour). All proposals remain internal; fixed transport connections are flagged rather than silently moved.

Research assumptions are explicit important claims. Reviews require an exact quotation from the selected source for
supported or contradicted classifications, cache by claim text and source version, and never treat retrieval failure
or absence of text as contradiction. Editing a claim or removing its source invalidates the current classification
and preparation; authorized earlier reports remain labelled as history.

## API

All paths below are under authorized `/api/v1/proactivity/cases` and derive the owner from `ICurrentUser`.

| Method | Suffix | Request / response |
|---|---|---|
| GET | empty | Case summaries; context/report/receipt payloads omitted |
| POST | empty | `ProactiveCaseDraft`; tracked by default; explicit enable must represent the owner's choice |
| GET | `/kinds`, `/sources` | Supported kinds and bounded source catalog |
| GET | `/{id}` | Current authorized detail |
| PUT | `/{id}?revision=` | Draft edits; kind and profile immutable |
| POST | `/{id}/commands` | `{command, revision}`: accept, enable, pause, resume, stop, dismiss, resolve, `done:<step>`, `reviewed:<assumption>` |
| POST | `/{id}/check` | Perform an eligible check; still respects schedule, autonomy, quiet hours and budgets |
| POST | `/{id}/feedback` | `{signal, revision}`; useful, not_useful, wrong, too_early, too_late, too_much_effort |
| POST | `/{id}/receipts/{receipt}/undo` | `{revision}`; only supported reversible receipt kinds |
| POST | `/{id}/memory-resolution` | `{revision, choice, firstContext?, secondContext?, sourceVersions}`; keep_first, keep_second, both |

Foreign IDs return 404; stale revisions and Undo conflicts return 409; invalid drafts return 400.

## Rollout and verification

Apply `20261103080313_AddProactiveCases` before exposing these endpoints or starting the new worker dispatcher. Deploy
compatible worker and API versions together: older workers do not register `ProactiveCaseWorkflow`. No responsibility
is enabled by migration. The mobile Home entry tolerates an older server's missing route.

Coverage lives in `ProactivityTests`, `ProactivePreparationTests`, `MemoryQualityReviewTests`, `DocumentTests`,
`ProactivePersistenceTests`, `tests/e2e/proactivity_flow.mjs`, and Flutter `proactive_screen_test.dart`. The PostgreSQL
integration suite starts a real ephemeral HTTP server and runs the Node case flow against it. Required screenshot
fixtures cover light/dark list/detail, editor/settings and large-text layouts; the web fixture records navigation and pause.
`Durable_workflow_resumes_after_worker_restart_and_honors_pause_without_duplicate_checks` also runs the production
workflow through a disposable Temporal server and two worker instances. Synthetic checks do not establish real-world
usefulness: report explicit owner feedback separately and leave silence unknown. Full-stack fixture smoke remains
necessary before deployment.
