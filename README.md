# Jarvis

Jarvis is a self-hosted personal assistant built as a modular .NET monolith with a Flutter client. This repository is being implemented in vertical slices from the architecture plan.

**Documentation:** Coding agents should start at [`AGENTS.md`](AGENTS.md). Architecture, API maps, and operations guides live under [`docs/`](docs/README.md).

## Current implementation

- Flutter chat shell with Markdown replies (tables, code blocks, links) and copy, a typing indicator, live tool-activity chips per reply, inline approve/decline cards for approval-gated tool calls, retry for messages that failed to send, a new-chat action, suggested prompts, Enter-to-send, and a navigation rail on wide screens
- Native generative UI cards (`RenderUi`) for choices, forms, status, and lists. Only the latest card stays interactive and sits above the composer; earlier cards collapse to a one-line receipt. Tapping an action continues the conversation
- Isolated Playwright browser/computer-use sessions (`BrowseTheWeb`) with an in-chat step timeline; navigation stays approval-gated and private/local hosts are blocked
- Agent2Agent: a public agent card at `/.well-known/agent-card.json`, JSON-RPC `POST /a2a` with hashed inbound bearer tokens, and a Settings → Agents registry that can delegate (after approval) to HTTPS peers
- Connected device nodes over SignalR: the signed-in app can honor location, battery, clipboard, open-URL, and local notification requests, with per-capability toggles under Settings → This device. After the app registers, it posts a battery and (when already permitted) location snapshot so device watches and the home briefing can use this phone without a live invoke
- WhatsApp and Signal messaging channels linked by scanning a QR code in the app (WhatsApp through the bundled Baileys bridge, Signal through signal-cli REST), with your own number allowed automatically, an editable allowlist, test send, per-peer thread history, and approval-gated replies; the WhatsApp Cloud API remains available as an advanced option
- Per-owner model routing in Settings → Models: ChatGPT Codex by default, or OpenRouter with an encrypted API key, catalog picker, and connection test
- Usage dashboard at Settings → Usage, a Usage tile for Home, and a home summary: Codex CLI and OpenRouter calls with input, output, cached, and reasoning tokens, web searches, estimated OpenRouter cost, messages sent, dreams, memories, and a personalization level based on active memory, pins, variety, and persona
- Owner-scoped skills (`SKILL.md` import/export) that the agent can load, save, and auto-create when continuous learning is on; Skills screen lists them
- A learned persona (traits plus custom instructions) from what the user says and how they rate replies, shown under Settings → Persona
- Owner-defined assistant profiles that snapshot persona, skills, document collections, model class, and learning policy onto each conversation or task, so one install can keep distinct contexts
- Durable heartbeat/reflection Temporal workflow that writes memories, persona updates, and skills on an interval, with proactive check-in notifications
- OpenClaw-style dreaming: a nightly three-phase sweep (light → REM → deep) that stages short-term signals, reflects on recurring themes, then scores and promotes, merges, or supersedes memories, tone/persona, and knowledge-graph facts. Each sweep also rewrites a short portrait of the user from the current memories and appends it to the chat system prompt; the next dream revises that same portrait. A reviewable dream diary and portrait are shown under Settings → Learning; diary text is never a promotion source
- Semantic memory search (OpenAI-compatible embeddings + reciprocal rank fusion) plus a temporal knowledge graph of people, places, and projects. Memory → Knowledge graph opens a pan-and-zoom map with type filters, search, current links, and literal facts (titles, dates, descriptions), plus each entity's timeline. Owners can edit names and summaries, add or close facts, and merge duplicate entities; chat tools propose those writes behind approval
- Owner-scoped file collections, per-conversation source attachments, scoped file search, structured citations on assistant replies (file id, chunk id, sanitized excerpt, optional page), and PDF page-aware chunking; chat composer source chips and tappable citation chips open owner-verified downloads
- Life timeline: one chronological view of journal, spending, habits, people, finished tasks, reminders, learned memories, chats and decisions, with "on this day" and arithmetic-based patterns such as how habits and mood move together
- Unified inbox: read-along WhatsApp chats and tracked mail threads triaged into needs reply / waiting / for info, with optional model summaries and draft replies, snooze, and a commitments ledger (what you promised and what is promised to you) that reminds you on the due morning
- Finance autopilot: monthly budgets with 80% / 100% warnings, recurring-charge detection with price-change and cancellation tracking, a month-end forecast, outlier alerts, English and Dutch bank CSV import with preview, and CSV export
- Automation studio: event-triggered automations (webhook, WhatsApp message, file upload, finished task, journal entry, expense, conversation needing a reply) with `{{event.*}}` templating, per-action branching, a no-side-effect simulator, templates, and hashed-token webhooks
- Library and deep research: clip public web pages (SSRF-safe), keep notes and reports, SM-2 flashcards, a digest, and durable research tasks that save a cited report
- Context modes: focus, commuting, meeting, sleep, travel and weekend, set by hand or inferred from sleeping hours, calendar and weekend; they hold back pushes, tone chat replies, can be set by automations, and drive an always-on ambient display
- Mission control: split a big goal into a crew of specialist steps that run in parallel as background tasks with a shared blackboard, supervised by the worker, with pause, cancel, edit, skip and retry
- Routine miner: finds things you do at the same time on the same days, or that reliably follow another event, and offers each as a simulated, draft-only automation under Automations → Suggested for you; a dismissed pattern never returns
- Decision journal: log a prediction with how sure you are, get a reminder on the review date to say whether it came true, and see your Brier score, a confidence-versus-reality chart and a trend; settled decisions also appear in the weekly review and on the life timeline
- Relationship radar: link a person to their WhatsApp chat and Jarvis notices when you drift apart from message times alone (a conversation that went quiet, you replying slower, one side doing the reaching out, a message left unanswered), shows it under People, nudges at most weekly, and keeps the keep-in-touch reminder honest when you write to them; an optional, off-by-default tone check uses the model
- Cancel or negotiate a subscription: from a detected subscription, have a background task draft the cancel or lower-price message (nothing is sent), or open a chat where Jarvis works through the merchant's site in the isolated browser with your approval for every navigation, click and typed input, never entering passwords or payment details and only marking it cancelled once the merchant confirms
- Personal home screen with a time-based greeting, the latest dream portrait, voice action, upcoming reminders, pending approvals, calendar events from a connected ICS pack, device battery, active task previews, and a shortcut to continue the current conversation; task details open from the preview, and the list refreshes on resume, task notifications, or pull to refresh
- Approval review is available from Tasks and contextually from a task waiting for approval; task-specific review shows only that task's conversation approvals
- Federated search across conversations, memories, files, tasks, reminders, skills, knowledge-graph entities, channel threads, and coding runs via `GET /api/v1/search`, with owner-scoped concurrent providers, per-provider timeouts, capped mixed ranking, and OpenTelemetry provider latency/result metrics (never recording search text)
- Command palette on desktop/web (`Ctrl`/`⌘`+`K`) and a dedicated mobile search screen with on-device recent queries (cleared on sign-out), result-type filters, and typed route navigation instead of interpolated URLs
- Persistent primary navigation for Chat, Tasks, Voice, Memory, and Settings; appearance (light, dark, or match this device), reminders, approvals, files, watches, automations, briefings, models, skills, persona, profiles, learning, channels, agents, this device, voice options, integrations, coding runs, usage, and audit log are grouped under Settings
- ASP.NET Core API with persistent conversations and Agent Framework sessions
- SignalR events for streamed assistant text and run status through Codex CLI app-server delta notifications
- User-visible tool activity through SignalR (`tool.started`, `tool.completed`, and `tool.failed`); only tool names and run status are sent, never arguments or results
- PostgreSQL via EF Core/Npgsql, with full-text indexes for memory and documents
- Owner-scoped memory and document search using PostgreSQL full-text and trigram search merged with reciprocal rank fusion; pinned, unexpired memories are included in bounded agent context, and up to eight memory candidates are reranked through the same Codex CLI + ChatGPT OAuth path, with an eight-second fallback to PostgreSQL order
- Optional MCP stdio tools, constrained to an explicit tool allowlist
- Per-run MCP connections that inject each owner's encrypted integration credentials only into the configured transport headers or child-process environment, then redact those values from tool results and errors before returning them to the agent; an unavailable optional server is isolated and skipped so it does not interrupt chat
- ASP.NET Core Identity accounts stored with Entity Framework, with bearer authentication and per-user ownership outside Development
- Flutter email and password sign-in and registration; access and refresh tokens use OS secure storage on mobile and WebCrypto-backed `flutter_secure_storage` on the web
- Journal (Everything → Journal): write about your day or talk it through with Jarvis in chat or voice, with a 1–10 day rating, 1–5 mood/energy/stress ratings, highlights, gratitude, and tags. Every entry is mirrored into memory (kind `journal`) so Jarvis can recall it; deleting an entry deletes that memory
- User-managed memory with category filters, search, edit, pin, delete, and visible superseded history; Codex-based extraction deduplicates repeats and transactionally supersedes only explicit corrections to unpinned memories
- Long conversations keep their early context: above 80,000 tokens the oldest turns are replaced by a rolling summary (facts, decisions, open items and exact details such as references and amounts), written in the background in 16,000-token blocks and reused until the next block fills, so no turn waits for it; the newest 32,000 tokens stay verbatim. Until a summary is ready, old tool results are collapsed and then the oldest groups are trimmed to below 64,000 tokens as before. Full history remains in the session store
- Agent Framework provides owner-scoped queued, running, and approval-waiting task context to interactive conversations, with task content labeled as untrusted reference data
- Conversation history with selection and deletion in the mobile client; deleting a conversation also removes its session, messages, approvals, and memories learned from those messages, while task sessions remain task-managed
- Human review for MCP tool calls unless a tool is explicitly configured for auto-approval; interrupted approval resumes remain visible and can be retried
- Idempotent approval persistence guarded by a unique owner/request/tool-call key
- Durable reminders scheduled through Temporal, including optional daily, weekday, and weekly recurrence in the owner's IANA time zone, with worker-delivered in-app notifications and a database-backed dispatcher recovery path
- Durable Temporal condition watches for public JSON HTTPS endpoints, authenticated JSON with a stored integration token, this device’s battery or location snapshot, or the next event on a connected ICS calendar, with bounded polling, owner-scoped cancellation, deterministic threshold checks, and notification/push delivery
- Owner-defined automations (schema version 1) with typed triggers (schedule, manual, reminder due, calendar window, device battery/location, public JSON threshold), optional conditions, and actions (notification, task, preconfigured channel message, agent run) executed through Temporal with stable workflow IDs, idempotent run keys, cooldowns, owner concurrency limits, approval pause/resume for external actions, sanitized run history, and a form-driven Flutter editor under Settings → Automations
- A weekly review every Sunday evening in the owner's time zone: journal mood, energy, stress and day ratings, finished tasks, handled reminders, and new memories become a push notification and a screen with a multi-week mood trend and a short persona-aware story (plain summary when the model is unavailable)
- Projects that group chats, files and tasks under one goal with owner instructions Jarvis follows in every chat and task of the project
- User-configurable daily morning briefings scheduled by Temporal at an IANA local time zone, with idempotent summaries of that day's reminders (including recurring next fires) and active tasks, plus an optional short persona-aware intro that falls back to the deterministic list if the model is unavailable
- Reminder and task notifications open their owner-scoped source item from the mobile client
- Approval-needed notifications are published over the owner-scoped SignalR group and open the approval screen from a foreground snackbar; tool arguments are not included in the event
- Durable APNs/FCM push delivery for reminders, completed tasks, and approvals, backed by owner-scoped device registrations, transactional delivery rows, leased retries, and expired-token cleanup
- Owner-scoped integration credentials protected with ASP.NET Core Data Protection; API responses expose configured names only, never credential values
- Flutter integration settings for guided calendar/mail/contacts packs, in-app MCP OAuth (PKCE + dynamic client registration, with token paste when the server has no authorize/token endpoints), adding, rotating, and deleting hidden owner-scoped MCP credentials, with per-owner MCP connection status from `GET /api/v1/integrations/connections` (tool counts only; endpoints and error details stay private)
- Private file upload, download, and recoverable deletion using S3-compatible object storage with PostgreSQL metadata; uploads enforce an allowlisted MIME/extension/signature, verify the full bounded stream, pass ClamAV malware scanning before storage, and dispatch indexable content to Temporal for extraction
- Durable PDF and text file extraction through Temporal, owner-scoped PostgreSQL chunks, and full-text search available in chat
- Durable tasks can be started or cancelled from chat, run by Temporal workers, and shown in the mobile app; pending tool approvals are linked to their exact task and survive activity retries. Task sessions stay separate from interactive chat history.
- Task details expose the owner-scoped task conversation and full assistant result, with completed-task notifications deep-linking to that detail view
- Temporal workflow starts use stable workflow IDs; long-running activities heartbeat and honor cancellation
- Model calls through the Codex CLI app-server, using the signed-in ChatGPT OAuth session and the account's default available Codex model; the account model catalog is cached for five minutes and refreshed through the app-server
- Current web search through Codex CLI's hosted `web_search` tool in live mode (voice uses the CLI's `standalone_web_search`) and the same signed-in ChatGPT OAuth session; no separate search-provider API key is used, search results are treated as untrusted, and completed search actions are counted without recording queries or result content
- Logical `fast`, `standard`, `reasoning`, `coding`, `vision`, and `realtime` model classes can map to models available to the signed-in Codex account. Each owner can switch Settings → Models to OpenRouter, store an encrypted API key, and pick chat/fast/reasoning/coding/vision/embedding models from the OpenRouter catalog. Codex remains the default for chat when OpenRouter is not selected; you can keep Codex for chat and add only an OpenRouter embedding model for semantic memory. Settings → Models also lists the models reported by the installed Codex CLI and can install a newer `@openai/codex` release into `$CODEX_HOME/cli`. The API and Temporal worker use that updated CLI when they share `CODEX_HOME` and are still pointed at the stock `codex` executable. An owner-selected Codex chat model is used for conversations; leave it empty to keep the server model class or the account default.
- Approval-gated Codex coding tasks in fresh detached worktrees from an explicit repository allowlist, with a Coding runs screen that lists worktree paths, changed files, and diffs
- Owner-scoped append-only audit log for approvals, coding runs, task/reminder lifecycles, files, and memory changes
- .NET Aspire AppHost for local API, PostgreSQL, and Temporal development server orchestration
- LiveKit development server plus Flutter `livekit_client` room publishing and an in-process C# voice runtime in the API that bridges PCM to Codex realtime through the app-server WebRTC transport
- OpenTelemetry traces and metrics via OTLP, including correlated agent/tool/model spans, agent duration, time-to-first-token, model and tool latency/outcomes, Codex-reported input/output/cached/reasoning token counts, native Codex web-search action counts, memory-search latency/hit counts, and Temporal worker operation spans; telemetry excludes prompts, memory queries, search queries, tool arguments, and results. When `SENTRY_DSN` is set, the same process also reports 5xx errors, warning logs, and Sentry agent spans (model, tokens, tool name, and cost) for that release. Prompt text stays out unless `Sentry__RecordAiContent` is true

Development uses a fixed local owner ID when a request has no access token, so local scripts can run without signing in. A signed-in request uses that account's Identity user ID in every environment. Outside Development, the API requires a Jarvis-issued bearer token. Production startup requires `Authentication:Issuer`, `Authentication:Audience`, and `Authentication:SigningKey`. Configure those values and your web origins before exposing the API. Jarvis sends model prompts and relevant conversation context to the Codex CLI app-server, which must be signed in with ChatGPT OAuth on the host or container. The adapter opens an ephemeral thread in an empty temporary directory with a read-only sandbox, disables Codex shell, browser, computer, app, plugin, skill, image-generation, and multi-agent tools plus MCP servers, and enables only Codex's hosted live web search when configured (`web_search="disabled"` otherwise). The child process receives only OAuth/runtime environment variables; shell network access stays disabled. Codex app-server message-delta events stream chat, while structured Jarvis tool requests are executed separately by Agent Framework through Jarvis's permission and approval path. The app-server and voice's standalone web-search feature are experimental and should be versioned alongside the Codex CLI installation.

## Run locally

No configuration is needed. From the repository root:

```sh
dotnet run --project src/Jarvis.AppHost
```

Aspire starts PostgreSQL, Temporal, object storage, ClamAV, LiveKit, signal-cli, the WhatsApp bridge, the MCP runner, the API (`http://localhost:5082`) and the worker, with development secrets built in; database migrations apply automatically. If the Codex CLI on this machine is not signed in yet, the app's Home screen shows **Sign Jarvis in to ChatGPT** with a one-time code. To override a default (a LAN listen URL, a feature), copy `.env.example` to `infra/compose/.env`, edit it, and source it before running.

## Tests

Pull requests targeting `main` run [`.github/workflows/ci.yml`](.github/workflows/ci.yml): .NET build and unit tests, PostgreSQL integration tests (Testcontainers), Python release/AltStore checks, the AppHost-generated production Compose checks, Flutter analyze and widget tests, and API/worker container image builds. Require the **All checks passed** status in branch protection before merging.

Run deterministic unit tests with:

```sh
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
scripts/ci/run-python-unit-tests.sh
```

The model-agnostic behavioral evaluation cases live in [`evals/jarvis-core-v1.jsonl`](evals/jarvis-core-v1.jsonl), with isolated-run requirements documented in [`evals/README.md`](evals/README.md). Run them against a disposable Jarvis deployment through the normal API; inference still goes through the Codex CLI app-server.

Flutter widget tests run with `flutter test` from `apps/mobile`.

To exercise the whole flow locally without a ChatGPT OAuth session, point the API and worker at the deterministic Codex app-server fixture in [`tests/e2e/fake_codex_app_server.mjs`](tests/e2e/fake_codex_app_server.mjs). It speaks the same app-server JSON-RPC subset, streams its output, and plans scripted multi-step tool calls (reminders, memory, approvals, clock, and background tasks). With PostgreSQL and a Temporal dev server running:

```sh
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ConnectionStrings__jarvis="Host=localhost;Database=jarvis;Username=jarvis;Password=jarvis"
export Codex__ExecutablePath="$PWD/tests/e2e/fake_codex_app_server.mjs" Codex__EnableWebSearch=false
dotnet run --project src/Jarvis.Api --no-launch-profile -- --urls http://localhost:5082 &
dotnet run --project workers/Jarvis.Worker --no-launch-profile &
(cd tests/e2e && npm ci && node local_fixture_flow.mjs)
```

`local_fixture_flow.mjs` checks streaming, tool events, approvals (approve and decline), memory, reminders, and a Temporal background task through the public HTTP and SignalR API. WhatsApp and Signal setup is covered by [`tests/e2e/channels_flow.mjs`](tests/e2e/channels_flow.mjs) against [`tests/e2e/fake_channels.mjs`](tests/e2e/fake_channels.mjs). Agent card, A2A tokens, remote agents, device settings, and voice options are covered by [`tests/e2e/platform_flow.mjs`](tests/e2e/platform_flow.mjs). Set `JARVIS_PLATFORM_CHAT=1` to also wait for a `RenderUi` choice card from the fake Codex planner. For the web app, run `flutter build web --profile` and serve `build/web` on `http://localhost:5137`, which is an allowed development CORS origin. The app signs in against the API; a Development API still accepts unauthenticated script calls.

PostgreSQL integration tests use Testcontainers and the same pgvector PostgreSQL image as the app. Run them on a machine with Docker available:

```sh
dotnet test tests/integration/Jarvis.IntegrationTests/Jarvis.IntegrationTests.csproj
```

Aspire exposes the Temporal development UI at `http://localhost:8233`. The AppHost (`src/Jarvis.AppHost`) is the only orchestration: there are no hand-written Compose files. Optional parts are switched on by name with `JARVIS_FEATURES` (or `--Jarvis:Features=`), the same names locally and in production:

```sh
JARVIS_FEATURES="github home-assistant browser" dotnet run --project src/Jarvis.AppHost
```

| Feature | Adds |
|---------|------|
| `github` | The official GitHub MCP server (locally it needs `github-mcp-server` on `PATH`; the API image bundles it) |
| `home-assistant` | Home Assistant's MCP endpoint; set `HOME_ASSISTANT_MCP_URL` |
| `browser` | Isolated Playwright MCP browser (production adds its filtering egress proxy) |
| `coding` | Production only: the coding tool on a mounted checkout (`CODING_REPO_PATH`); Aspire already registers the current checkout |
| `tunnel` | Production only: Caddy behind an existing host proxy or Cloudflare Tunnel |
| `verification` | Local only: the fake Codex app server and fake MCP server used by the e2e scripts |

The API is available at `http://localhost:5082`; OpenAPI is at `/openapi/v1.json` in Development. Local development uses the Development identity bypass and binds to loopback by default; never expose it publicly.

Aspire starts `bbernhard/signal-cli-rest-api` and sets `Channels__Signal__BaseUrl` automatically. Link a Signal device from the app (Settings → WhatsApp & Signal → Connect → Signal, then scan the QR code); the REST port is bound to loopback as `http://127.0.0.1:8080` in development and stays unpublished in production. Set `SIGNAL_CLI_REST_URL` only when you already run signal-cli somewhere else.

File uploads are scanned by a private ClamAV daemon before object storage. Aspire persists its signature database and waits for ClamAV readiness; first startup may take several minutes while signatures download. Production must configure `Antivirus:Host` and keep the daemon private to the application network. Jarvis fails closed when the scanner is unavailable and rejects detected files before storage.

Voice uses Flutter LiveKit rooms and an in-process C# runtime in the API. The API checks conversation ownership, asks the installed Codex CLI for its voice-mode voices (`thread/realtime/listVoices`), joins the LiveKit room itself, and starts Codex realtime with the owner's saved voice or that CLI's own default. It returns a ten-minute room JWT plus the owner's hands-free and caption preferences. The runtime bridges room PCM audio to Codex CLI's realtime voice session over WebRTC and publishes the returned audio directly back into the LiveKit room. Codex answers in that realtime session and calls the same Jarvis tools and memory as chat through a C# MCP host (`SearchMemory`, reminders, MCP servers, and the rest), including live Codex web search for current facts. Approval-gated tools still ask in the Jarvis app. The runtime posts live caption deltas, persists final utterances on the conversation, and keeps the mounted Codex CLI OAuth session; it does not use a model-provider API key. Set `VOICE_WORKER_SECRET` in `.env` to a unique random value outside local development. Hands-free listening, captions, and the CLI voice list are under Settings → Voice. The realtime app-server protocol is experimental and must stay version-aligned with the pinned Codex CLI; audio behavior still needs validation with a signed-in account and a physical device.

The audit log is available from the mobile app's Audit log action and `GET /api/v1/audit`. It stores action names, risk classes, outcomes, owner IDs, and limited resource metadata; it does not store prompts, file contents, or memory contents. The application rejects update and delete operations on audit events.

Integration credentials can be managed from the Flutter Integrations screen or through the API. `PUT /api/v1/integrations/{provider}/credentials/{secretName}` adds or replaces one secret without returning its value; `DELETE` on that path removes only that secret. `GET /api/v1/integrations/credentials` lists configured provider names and secret field names, while `DELETE /api/v1/integrations/{provider}/credentials` removes every secret for that provider. The full-set `PUT /api/v1/integrations/{provider}/credentials` also remains available for automation. Secret values are protected with ASP.NET Core Data Protection before PostgreSQL persistence and never enter agent context. Set `DataProtection__KeysDirectory` to a persistent, private writable key-ring directory outside Development. For production Compose, create the configured host directory with mode `0700` and ownership matching `JARVIS_UID`/`JARVIS_GID`; back it up securely because losing the key ring makes stored credentials unreadable. MCP adapters can retrieve these values through the server-side `IIntegrationCredentialStore` contract. Development Compose persists its key ring in a named volume, and Aspire uses the current user's default ASP.NET Core key store.

The browser container has no published host port, runs without the Docker socket, has resource limits, and lives on an internal Docker network. Chromium uses a dedicated Squid egress proxy; the proxy blocks loopback, private, link-local, multicast, and reserved IP ranges plus local-only hostnames, including on redirects. Browser navigation and interaction tools require approval; page snapshots, searches, screenshots, and console reads are allowlisted as read-only. Chat can start an isolated session with `BrowseTheWeb`; the app shows each Playwright step on a timeline. This opt-in development configuration is for the single local Development identity. Browser profiles and sessions still need per-user isolation before enabling the browser in a multi-user deployment.

Aspire registers the current Git checkout as the `jarvis` coding repository. When Jarvis proposes a coding task, the user must approve it; Codex then edits a fresh detached worktree outside the primary checkout using its workspace-write sandbox. For repositories without a commit, or with tracked credential-like files, it creates a separate Git snapshot from nonignored files while filtering common credential paths. Changes stay uncommitted and the tool returns the workspace path and diff summary for review. Settings → Coding runs lists recent runs. Production exposes a coding repository only with the `coding` feature, which mounts `CODING_REPO_PATH` and a writable worktree volume.

PDF, text, Markdown, CSV, JSON, JPEG, PNG, and WebP uploads are indexed asynchronously by the Temporal worker. Image text is extracted with the same signed-in Codex CLI app-server and ChatGPT OAuth model path used for chat (up to 8 MiB per image). Jarvis can search extracted text when answering chat requests; document contents remain untrusted input and are not promoted into system instructions. Images without legible text finish indexing as `ready` with no text chunks. The files screen shows `queued`, `processing`, `ready`, or `failed` for indexable files.

Condition watches can be created in chat or from Settings → Condition watches. JSON watches poll a public, credential-free HTTPS JSON endpoint for one numeric dot-separated object property, or an authenticated JSON URL using a stored integration token as `Authorization`. Device battery and location watches read the latest snapshot posted by the Jarvis app. Calendar watches read the connected ICS pack and alert when the next event is within a chosen number of minutes. Intervals are 5 minutes to 24 hours. HTTPS JSON requests reject redirects, private/reserved IPs, local hostnames, non-JSON responses, and bodies over 1 MiB. Checks are deterministic Temporal activities and do not invoke a model.

The daily morning briefing can be enabled and configured from the Jarvis menu or through `PUT /api/v1/briefings/daily` with `enabled`, `localTime` (`HH:mm:ss`), and `timeZoneId` (for example, `Europe/Amsterdam`). Temporal keeps one durable schedule per owner and applies time-zone daylight-saving changes. Each day's notification is idempotent and includes reminders due that local day plus queued, running, or approval-waiting tasks. Jarvis then optionally writes a 1–3 sentence persona-aware intro through the same background model path as heartbeat reflection; if that rewrite times out or fails, the notification keeps the deterministic list. Reminder and task titles are treated as untrusted data.

The bundled Temporal server uses its development mode and SQLite persistence. A production deployment needs a supported Temporal server deployment with durable production storage, TLS/authentication, and operational monitoring. The API and worker use `Temporal__Address` (default `localhost:7233`).

## Production deployment

The production stack is defined in the AppHost (`src/Jarvis.AppHost/ProductionDeployment.cs`) and published as a Docker Compose file; Docker Compose only runs what Aspire generated, and the file is never edited by hand. It runs Jarvis behind Caddy with persistent PostgreSQL, a PostgreSQL-backed Temporal server, private Garage S3-compatible storage, ClamAV, LiveKit, a private signal-cli REST API, the MCP runner, nightly backups, and the Jarvis API/workers. Only Caddy's HTTP/HTTPS ports and LiveKit's required media ports are published; Postgres, Temporal, Garage, ClamAV, and signal-cli remain on private Docker networks.

There is nothing to configure by hand. Every deploy runs `scripts/deploy/prepare-host.sh` first, which creates `infra/compose/.env.production` (mode `0600`) and fills in everything Jarvis manages: database, storage, account, LiveKit, voice and MCP runner secrets; the service account ids; the Codex home, key ring and backup directories under `JARVIS_DATA_DIR` (default `~/jarvis-data`, mode `0700`); and a single-node Garage config. The only input is the public hostname, `JARVIS_DOMAIN`. Values already in the file are never changed, so an existing server keeps its passwords and paths. Voice uses the same hostname: LiveKit signaling is served under `/rtc`, so one DNS record is enough (a separate `LIVEKIT_DOMAIN` still works).

After the first deploy, open Jarvis and use **Sign Jarvis in to ChatGPT** on the Home screen (also in Settings → Models). It shows a one-time code for ChatGPT's device sign-in, so nobody needs a shell on the server. It is only offered while the server is signed out.

To deploy by hand on the server:

```sh
export JARVIS_DOMAIN=jarvis.example.org
export JARVIS_API_IMAGE=ghcr.io/<owner>/jarvis-ai/api:<sha>
export JARVIS_WORKER_IMAGE=ghcr.io/<owner>/jarvis-ai/worker:<sha>
JARVIS_FEATURES="github" scripts/deploy/remote-up.sh
```

`remote-up.sh` prepares the host, publishes the AppHost into `artifacts/compose/docker-compose.yaml` (with a local .NET 10 SDK, or the pinned SDK container so the server needs only Docker), then pulls, migrates, and replaces containers. Features are listed under [Run locally](#run-locally) and in [docs/operations/deployment-and-ci.md](docs/operations/deployment-and-ci.md); `DEPLOY_COMPOSE_FILES` from older setups is mapped to them. Backups are on by default; see [docs/operations/backup-and-restore.md](docs/operations/backup-and-restore.md) for encryption and off-host copies.

Allow inbound TCP 80 and 443 plus UDP 443; nothing else is published. Caddy owns TCP 80/443, LiveKit voice media uses UDP 443 directly and falls back to TCP 443 through Caddy (with the `tunnel` feature, where another proxy owns 443, LiveKit media needs TCP 7881 and UDP 50000-50100 instead). Also allow outbound HTTPS for Codex OAuth/model access, Firebase FCM/OAuth, user-configured public condition-watch endpoints, Caddy certificate issuance, ClamAV signature updates, NuGet (for publishing the AppHost), the MCP registry and npm/PyPI (for connectors), and GHCR pulls. The deployment script applies EF migrations after Postgres is healthy and before updating application containers. Keep the environment file private, and pin every third-party container image to an audited release or digest before deploying.

## GitHub Actions pipelines

Releases use [Semantic Versioning 2.0.0](https://semver.org/): git tags are `vMAJOR.MINOR.PATCH` (for example `v1.2.0`). When a pull request merges to `main`, [`.github/workflows/create-release-tag.yml`](.github/workflows/create-release-tag.yml) reads the **SemVer bump** checkboxes in [`.github/pull_request_template.md`](.github/pull_request_template.md), creates the next `v*` tag on the merge commit, and pushes it. That tag starts [`.github/workflows/release.yml`](.github/workflows/release.yml). Release builds the backend/web images and the iOS IPA, then **Approve release** is the one production review. Approving it deploys the server and publishes the AltStore source. You can still run the backend or iOS workflow manually from **Actions**; those direct runs keep their own production approval. Push a `v*` tag yourself in an emergency and approve the Release workflow it starts.

| Change | SemVer bump | Example tag |
|--------|-------------|---------------|
| Breaking API, auth, DB, or mobile contract | Major | `v2.0.0` |
| Backward-compatible feature | Minor | `v1.3.0` |
| Backward-compatible fix or docs-only release | Patch | `v1.2.1` |

Before tagging, align `apps/mobile/pubspec.yaml` with the marketing version (`version: X.Y.Z+N`). The iOS workflow rewrites the pubspec to `X.Y.Z` plus a monotonic iOS build number derived from the SemVer and run id. Validate a version locally with `python3 scripts/release/semver.py --validate 1.2.3` or compute the next tag with `python3 scripts/release/semver.py --bump patch --from-version 1.2.0`.

### iOS IPA (unsigned, LiveContainer)

[`.github/workflows/release-ios.yml`](.github/workflows/release-ios.yml) builds an **unsigned** release IPA on `macos-latest` and attaches `Jarvis.ipa` to the GitHub Release for the SemVer tag. No Apple signing certificates or provisioning profiles are required.

1. Add production Flutter `--dart-define` repository secrets when you need a non-local API at build time: `JARVIS_API_URL` and optional Firebase iOS keys (`JARVIS_FIREBASE_*`).
2. Tag `vX.Y.Z` or run the workflow manually. Download `Jarvis.ipa` from the release.
3. Import the IPA in [LiveContainer](https://github.com/LiveContainer/LiveContainer) on your device (or copy the file via AirDrop, Files, or another transfer you already use with LiveContainer).

Local packaging uses the same layout as CI: `flutter build ios --release --no-codesign` then [`scripts/ios/package_unsigned_ipa.sh`](scripts/ios/package_unsigned_ipa.sh).

Push notifications and some entitlements may be limited without a normal signed distribution profile; in-app chat, account sign-in, and SignalR still depend on your configured `JARVIS_API_URL`.

The Release workflow publishes both the IPA and generated `source.json` to the Jarvis server after the production approval. A manual run of the iOS workflow on a `v*` tag also publishes that source and creates the GitHub Release; a manual run from another branch builds the IPA only. Add `https://jarvis.ykdbonte.dev/altstore/source.json` to AltStore. Each run keeps earlier IPA versions available for existing source entries. The feed and download are public; keep private data out of the IPA and source metadata.

The self-hosted runner serves `/home/ykdbonte/.jarvis/altstore` through the host Caddy route at `/altstore/*`. The public endpoint is independent of GitHub repository visibility, and the workflow updates the source only after the IPA artifact is ready.

### Backend GHCR images and deployment

[`.github/workflows/deploy-backend.yml`](.github/workflows/deploy-backend.yml) builds `api` and `worker`, pushes them to `ghcr.io/<owner>/jarvis-ai/<name>:<git-sha>` (plus the version tag and `latest` on `v*` tags). A `v*` tag builds those images from the Release workflow and deploys them only after **Approve release**. A manual run of this workflow can still deploy on its own production approval. Garage uses its pinned upstream image. The workflow does not run on every commit to `main`.

Server setup:

1. Point DNS for your hostname at the server and install Docker with the Compose plugin.
2. Register a persistent Linux x64 self-hosted runner on the server with the `jarvis-deploy` label, running as the account that should own Jarvis's data, with Docker access. The deploy workflow transfers the source as a short-lived Git bundle and pulls images with the workflow's `GITHUB_TOKEN`, so the server needs no GitHub credentials.
3. Set repository variable `JARVIS_DOMAIN` to the hostname. Optionally set `JARVIS_FEATURES` (for example `github` or `tunnel` behind a host-level reverse proxy or Cloudflare Tunnel) and secret `DEPLOY_PATH` (default `~/jarvis`).
4. Run the Release workflow (or merge a release PR) and approve the deploy. Then sign in to ChatGPT from the Home screen.

In GitHub → Packages, the two application container packages must be linked to this repository so `GITHUB_TOKEN` can push and the deploy job can pull; keep them private if the repo is private.

`workflow_dispatch` accepts `skip_deploy` to build/push images without deploying, and an optional extra `image_tag`. Production secrets stay in `.env.production` on the server and are never passed through GitHub Actions. `python3 -m unittest tests/unit/compose/test_production_compose.py` publishes the AppHost and checks the generated stack (images, isolation, features); CI runs it in the backend job.

Production deployment runs `dotnet Jarvis.Api.dll migrate` as a one-shot task before
replacing the API or worker containers. The task logs the target EF migration, uses a
PostgreSQL advisory lock, and aborts the deployment on failure. API startup migration
is intentionally rejected outside Development; local Development startup continues
to migrate by default and can opt out with `Database__ApplyMigrationsAtStartup=false`.

Migrations shipped in a rolling release must be backward compatible with both the old
and new API/worker versions. Use expand-and-contract changes: add nullable columns,
tables, and indexes first; deploy readers/writers that tolerate both schemas; backfill
separately; and remove or tighten schema only in a later release after old replicas can
no longer run. Never combine a destructive schema change with the first code release
that stops using the old shape.

MCP tools are disabled unless configured. Set `Mcp__Servers__0__Name`, `Mcp__Servers__0__Transport`, and `Mcp__Servers__0__AllowedTools__0`; stdio servers also need `Mcp__Servers__0__Command` and optional arguments/environment, while Streamable HTTP servers need `Mcp__Servers__0__Endpoint` and optional headers. Tools are approval-required by default; add an exact tool name to `Mcp__Servers__0__AutoApprovedTools__0` only when unattended execution is intended. Stdio children receive a minimal environment by default. To inject an owner's encrypted integration credentials into a stdio child, set `Mcp__Servers__0__CredentialProvider`, map a child variable to a stored secret name under `CredentialEnvironmentVariables`, and store those values under the same provider slug. For Streamable HTTP, map headers with `CredentialHeaders`; sending mapped credentials requires HTTPS. Store the complete header value (including a `Bearer` scheme when required) in the encrypted credential record. The API opens a scoped MCP connection for each agent run and disposes it when the run finishes. Stored or configured transport values are scrubbed from MCP tool results and errors before those values can reach the model context.

Jarvis can manage MCP servers in conversation. `list_host_mcp_servers` lists operator-installed host servers such as GitHub with their credential provider slug and configured allowlists before a token is stored. `list_mcp_servers` and `list_mcp_connections` show registered servers and the tools connected for this turn, including host servers. `discover_mcp_server_tools` returns tool names, descriptions, required arguments, prompts, and resources for a public HTTPS endpoint, or for a registered or host server id when credentials are available; host servers without a token still return their configured allowlist and Integrations provider slug. `add_mcp_server` registers public HTTPS servers and can omit `allowedTools` to register every tool name discovered from the endpoint (up to 80). `add_mcp_stdio_server` downloads and registers owner-scoped stdio servers through `npx -y` or `uvx`. `update_mcp_server`, `set_mcp_server_enabled`, `set_mcp_server_tools`, and `remove_mcp_server` change that registration. Add, update, enable, tool changes, and remove require approval. An allowlist is 1 to 80 exact tool names, or `*` for every tool the server exposes. The agent can also narrow or pause a host server such as GitHub, but it cannot enable tools the operator left out of that server's configuration. `invoke_mcp_tool`, `read_mcp_resource`, and `get_mcp_prompt` use an enabled server during the current turn; direct tools from a new or changed server appear on the next turn. Every one of those calls requires approval. After adding a remote server, store its optional bearer token in Integrations using the returned `jarvis-mcp-…` provider ID and the secret name `token`. Server definitions, host-server pauses, and credentials are encrypted in the owner-scoped integration credential store. The Integrations screen can pause a server, start an in-app OAuth session (`POST /api/v1/integrations/oauth/sessions` plus the anonymous `/api/v1/integrations/oauth/callback`), and install guided calendar/mail/contacts packs (`GET/POST /api/v1/integrations/packs`). Calendar packs can store a first-party ICS URL without adding an MCP server. `GET/POST/PUT/DELETE /api/v1/mcp-servers` plus `PUT /api/v1/mcp-servers/{id}/state` and `PUT /api/v1/mcp-controls/{name}` expose the same controls outside chat.

Home Assistant's first-party MCP Server integration is supported over its Streamable HTTP `/api/mcp` endpoint. Enable the integration in Home Assistant and expose only the entities Jarvis may access. Set `HOME_ASSISTANT_MCP_URL` to its HTTPS `/api/mcp` URL, then turn on the `home-assistant` feature (`JARVIS_FEATURES`) locally or in production. In Jarvis → Integrations, use **Set or rotate token** and paste a Home Assistant long-lived access token; Jarvis stores it under `home-assistant` → `token`, encrypted, and adds the `Bearer` scheme only when connecting. The feature allowlists the Home Assistant server's exposed tools; Jarvis requires approval for every call by default. Use HTTPS because the per-owner token is sent in an Authorization header. See the [Home Assistant MCP Server setup](https://www.home-assistant.io/integrations/mcp_server) for enabling the server and exposing entities.

GitHub uses the [official GitHub MCP server](https://github.com/github/github-mcp-server), pinned to v1.12.2 and built into the API container. The `github` feature launches it over stdio with only its repository, issue, and pull request toolsets enabled. Store a least-privilege GitHub personal access token in Jarvis → Integrations under `github` → `token`; it is injected into that server process for the signed-in owner only. All exposed GitHub tools require approval. Model inference stays on Codex unless the owner selects OpenRouter in Settings → Models.

The Flutter default API URL is `http://localhost:5082`. On Android emulators use `--dart-define=JARVIS_API_URL=http://10.0.2.2:5082`. On a physical device, point the API URL at the machine's reachable address and configure the Compose port bindings and firewall for the device. The app expects the API host to be reachable and joins `/hubs/events` for incremental response updates.

Mobile push uses Firebase Cloud Messaging for both Android and iOS; configure an APNs authentication key for the iOS app in the Firebase project. Pass `JARVIS_FIREBASE_API_KEY`, `JARVIS_FIREBASE_PROJECT_ID`, `JARVIS_FIREBASE_SENDER_ID`, and the platform's `JARVIS_FIREBASE_ANDROID_APP_ID` or `JARVIS_FIREBASE_IOS_APP_ID` as Flutter `--dart-define` values. Set `JARVIS_FIREBASE_IOS_BUNDLE_ID` if the iOS bundle ID differs from `com.example.jarvis_mobile`. The iOS target declares Push Notifications and Background Modes/Remote notifications; use a real signing profile with APNs enabled. The app requests notification permission after sign-in, registers token refreshes with Jarvis, and removes the device registration on sign-out. In production, set `FIREBASE_PROJECT_ID` and `FIREBASE_SERVICE_ACCOUNT_FILE` in the protected Compose environment file; the service-account file is mounted read-only into the API and must be readable by `JARVIS_UID`. Leave the project ID empty to disable server push delivery. SignalR and the in-app notification list continue to work without Firebase configuration.

Sign in from the Flutter app with email and password. The API stores accounts with ASP.NET Core Identity and Entity Framework, returns a short-lived JWT plus a rotating refresh token, and uses the account ID as the owner ID. Tokens stay in `flutter_secure_storage` (OS secure storage on mobile, WebCrypto in the browser). Serve the web app over HTTPS (or localhost) and configure that origin in `Cors:AllowedOrigins`. Set `Authentication__AllowRegistration=false` after the accounts you need have been created.

## Configuration

Important settings are in `src/Jarvis.Api/appsettings.json` and may be overridden by environment variables:

- `ConnectionStrings__jarvis`: PostgreSQL connection string supplied automatically by Aspire.
- `Codex__ExecutablePath`: Codex CLI executable (default `codex`). Settings updates apply only when this is the stock `codex` executable; a custom path, such as the end-to-end fixture, is left unchanged.
- `Codex__ManagedInstallDirectory`: directory for Settings-driven Codex CLI updates (default `$CODEX_HOME/cli`, or `~/.codex/cli` when `CODEX_HOME` is unset). The image-pinned CLI remains the fallback until an update is installed.
- `Codex__NpmExecutablePath`: npm used to install those updates (default `npm`).
- `Codex__EnableWebSearch`: enable Codex CLI's hosted live web search for chat turns (default `true`); set to `false` to disable search while keeping the other Codex restrictions. This CLI feature is experimental and depends on the installed Codex version.
- `Codex__Model`: optional model identifier passed to each Codex CLI app-server thread; by default, Jarvis selects the default available model reported by the signed-in Codex account.
- `Jarvis__ModelClass`: optional root-agent class such as `standard` or `reasoning`; it must have a corresponding Codex model mapping.
- `Codex__ModelClasses__Fast`, `__Standard`, `__Reasoning`, `__Coding`, `__Vision`, and `__Realtime`: optional exact model identifiers from the signed-in Codex account. The coding tool, image requests, and in-process voice runtime select their respective classes when configured; every selection is checked against the account's model catalog and required input modality.
- `Codex__VisionModel`: optional Codex account model identifier for image inputs. Jarvis checks the signed-in app-server model catalog for input modality support; when unset, image requests automatically select an available image-capable model.
- `Coding__Repositories__0__Name` and `Coding__Repositories__0__Path`: explicit repository allowlist that enables the approval-gated coding tool. Aspire sets this to the current Git checkout.
- `Coding__WorktreeRoot`: optional parent directory for isolated coding workspaces. Defaults to `.jarvis-worktrees` beside the configured repository.
- `Coding__TimeoutSeconds`: coding-task time limit from 60 to 3,600 seconds (default 900).
- `Authentication__Issuer`: required outside Development; a stable token issuer such as `https://jarvis.example.com`.
- `Authentication__Audience`: required outside Development; the API audience, usually `jarvis-api`.
- `Authentication__SigningKey`: required outside Development; at least 32 random bytes. Do not reuse the Development key.
- `Authentication__AllowRegistration`: allow `POST /api/v1/auth/register` (default `true`). Set `false` after the owner account exists.
- `Authentication__AccessTokenMinutes` and `Authentication__RefreshTokenDays`: token lifetimes (defaults 15 minutes and 30 days).
- `RateLimiting__AuthPermitsPerMinute` and `RateLimiting__PublicPermitsPerMinute`: per-client-IP limits for sign-in and OAuth callbacks (default 10) and for Agent2Agent and WhatsApp webhooks (default 120). Over the limit returns 429 with `Retry-After`.
- `ReverseProxy__TrustForwardedHeaders`: take the client IP from the last `X-Forwarded-For` hop (default false). The production deployment sets it because the API is only reachable through Caddy.
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OpenTelemetry collector or Aspire Dashboard endpoint.
- `SENTRY_DSN`, `SENTRY_ENVIRONMENT`, and `SENTRY_RELEASE`: Sentry project for the API, worker, migrations, and WhatsApp bridge. Production already points at that project; set `SENTRY_DSN` only to replace it. Development does not send events. `Sentry__RecordAiContent` defaults to false. Release builds of the app report to the mobile project; override the DSN with `JARVIS_SENTRY_DSN`.
- `Mcp__Servers__0__Name`, `Mcp__Servers__0__Transport`, `Mcp__Servers__0__Command`, `Mcp__Servers__0__Endpoint`, `Mcp__Servers__0__AllowedTools__0`, and `Mcp__Servers__0__AutoApprovedTools__0`: optional MCP server settings. Supported transports are `stdio` and `streamableHttp`. Only explicitly allowlisted tools reach the agent; tools require approval unless named in `AutoApprovedTools`.
- `Mcp__Servers__0__CredentialProvider`, `Mcp__Servers__0__CredentialEnvironmentVariables__TOKEN`, and `Mcp__Servers__0__CredentialHeaders__Authorization`: map encrypted owner-scoped integration secrets to an MCP server's process environment or HTTPS headers. Credential map values name secret fields in `PUT /api/v1/integrations/{provider}/credentials`.
- `Mcp__Servers__0__CredentialHeaderPrefixes__Authorization`: optional validated prefix (for example, `Bearer`) added to an encrypted credential when constructing an MCP HTTP header. Secret values and the constructed header are both redacted from tool results.
- `Temporal__Address`: Temporal frontend address used by the API and reminder worker.
- `LiveKit__ApiKey`, `LiveKit__ApiSecret`, `LiveKit__InternalUrl`, and `LiveKit__PublicUrl`: in-process voice runtime and short-lived mobile room token settings. Local development uses the configured `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` (the secret must contain at least 32 UTF-8 bytes); set the Aspire listen URL and the public URL to the machine's LAN address when connecting from a physical device.
- `Voice__WorkerSecret`: shared secret the in-process voice MCP child uses for tool callbacks. Configure it from a secret store outside local development.
- `Channels__PublicBaseUrl` or `Jarvis__PublicBaseUrl`: public origin used in WhatsApp webhook URLs and the Agent2Agent card (`https://jarvis.example.com`). When unset, the API uses the incoming request host.
- `Channels__WhatsAppBridge__BaseUrl` / `Channels__WhatsAppBridge__Token`: the Baileys WhatsApp bridge (`workers/whatsapp-bridge`). Aspire and the production deployment build and start it automatically (`http://whatsapp-bridge:3000` inside Compose, session data in the `whatsapp-bridge-data` volume); set `WHATSAPP_BRIDGE_URL` only when it runs elsewhere and `WHATSAPP_BRIDGE_TOKEN` to require a bearer token. It is never published outside the Docker network. Link WhatsApp from the app: Settings → WhatsApp & Signal → Connect → WhatsApp, then scan the code under WhatsApp → Linked devices. This uses the unofficial WhatsApp Web protocol, so use it with your own account and expect the occasional re-link.
- `Channels__Signal__BaseUrl`: signal-cli REST endpoint. Aspire and the production deployment default this to the bundled `bbernhard/signal-cli-rest-api` service (`http://127.0.0.1:8080` on the host, `http://signal-cli:8080` inside Compose). Override with `SIGNAL_CLI_REST_URL` only when signal-cli runs elsewhere. Leave the override empty to keep the bundled service.
- `Channels__WhatsApp__GraphBaseUrl`: WhatsApp Cloud API origin (default `https://graph.facebook.com/v21.0`).
- `Push__FirebaseProjectId` and `Push__GoogleServiceAccountFile`: enable the API's durable FCM sender (which relays iOS pushes through Firebase/APNs); keep the service-account file outside the repository and mount it read-only.
- `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`, `LIVEKIT_PUBLIC_URL`, and `VOICE_WORKER_SECRET`: LiveKit and in-process voice runtime settings. Model inference is through the Codex CLI app-server only unless the owner selected OpenRouter for chat.
- `JARVIS_LISTEN_URL`, `LIVEKIT_BIND_ADDRESS` (production LiveKit media), and `LIVEKIT_PUBLIC_URL`: device reachability. They default to loopback (`JARVIS_LISTEN_URL=http://localhost:5082` for Aspire). For a physical device, set the Aspire listen URL to the machine's LAN interface, and set `LIVEKIT_PUBLIC_URL=ws://<machine-lan-address>:7880`. Local development uses the configured `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` (the secret must contain at least 32 UTF-8 bytes).
- `ObjectStorage__ServiceUrl`, `ObjectStorage__AccessKey`, `ObjectStorage__SecretKey`, `ObjectStorage__Bucket`, and `ObjectStorage__Region`: S3-compatible object storage. Local Aspire uses SeaweedFS; production should point to a supported S3-compatible service and provision a private bucket with least-privilege credentials.
- `Antivirus__Host`, `Antivirus__Port`, and `Antivirus__TimeoutSeconds`: private ClamAV daemon endpoint and upload scan timeout. The port defaults to `3310`; timeout defaults to 60 seconds.

## Repository layout

`apps/mobile` contains Flutter. `src/Jarvis.Api` is the HTTP and SignalR edge; Application and Domain hold contracts and entities; Infrastructure owns EF Core/PostgreSQL; Agents owns Microsoft Agent Framework and the Codex CLI adapter. `src/Jarvis.Workflows` defines Temporal workflows and `workers/Jarvis.Worker` hosts their activities.
