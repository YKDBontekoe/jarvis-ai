# Jarvis

Jarvis is a self-hosted personal assistant built as a modular .NET monolith with a Flutter client. This repository is being implemented in vertical slices from the architecture plan.

## Current implementation

- Flutter chat shell with Markdown replies (tables, code blocks, links) and copy, a typing indicator, live tool-activity chips per reply, inline approve/decline cards for approval-gated tool calls, retry for messages that failed to send, a new-chat action, suggested prompts, Enter-to-send, and a navigation rail on wide screens
- Native generative UI cards (`RenderUi`) for choices, forms, status, and lists. Only the latest card stays interactive and sits above the composer; earlier cards collapse to a one-line receipt. Tapping an action continues the conversation
- Isolated Playwright browser/computer-use sessions (`BrowseTheWeb`) with an in-chat step timeline; navigation stays approval-gated and private/local hosts are blocked
- Agent2Agent: a public agent card at `/.well-known/agent-card.json`, JSON-RPC `POST /a2a` with hashed inbound bearer tokens, and a Settings → Agents registry that can delegate (after approval) to HTTPS peers
- Connected device nodes over SignalR: the signed-in app can honor location, battery, clipboard, open-URL, and local notification requests, with per-capability toggles under Settings → This device
- WhatsApp Cloud API and Signal (signal-cli REST) messaging channels, configured in the app with an allowlist, webhook copy, test send, and approval-gated replies
- Per-owner model routing in Settings → Models: ChatGPT Codex by default, or OpenRouter with an encrypted API key, catalog picker, and connection test
- Usage dashboard at Settings → Usage, the sidebar, and a home summary: Codex CLI and OpenRouter calls with input, output, cached, and reasoning tokens, web searches, estimated OpenRouter cost, messages sent, dreams, memories, and a personalization level based on active memory, pins, variety, and persona
- Owner-scoped skills (`SKILL.md` import/export) that the agent can load, save, and auto-create when continuous learning is on; Skills screen lists them
- A learned persona (traits plus custom instructions) from what the user says and how they rate replies, shown under Settings → Persona
- Durable heartbeat/reflection Temporal workflow that writes memories, persona updates, and skills on an interval, with proactive check-in notifications
- OpenClaw-style dreaming: a nightly three-phase sweep (light → REM → deep) that stages short-term signals, reflects on recurring themes, then scores and promotes, merges, or supersedes memories, tone/persona, and knowledge-graph facts. A reviewable dream diary is shown under Settings → Learning; diary text is never a promotion source
- Semantic memory search (OpenAI-compatible embeddings + reciprocal rank fusion) plus a temporal knowledge graph of people, places, and projects. Memory → Knowledge graph opens a pan-and-zoom map with type filters, search, current links, and literal facts (titles, dates, descriptions), plus each entity's timeline
- Agent tools for reminders (create, list, cancel, including daily/weekday/weekly recurrence), background tasks (create, list, cancel), condition watches (create, list, stop), memory (search, remember, and approval-gated forget), files (search, list), MCP server management, and an IANA time-zone clock; each turn also receives the current time in the owner's configured briefing time zone
- Personal home screen with a time-based greeting, voice action, active task previews, and a shortcut to continue the current conversation; task details open from the preview, and the list refreshes on resume, task notifications, or pull to refresh
- Approval review is available from Tasks and contextually from a task waiting for approval; task-specific review shows only that task's conversation approvals
- Persistent primary navigation for Chat, Tasks, Voice, Memory, and Settings; reminders, approvals, files, watches, briefings, models, skills, persona, learning, channels, agents, this device, voice options, integrations, usage, and audit log are grouped under Settings
- ASP.NET Core API with persistent conversations and Agent Framework sessions
- SignalR events for streamed assistant text and run status through Codex CLI app-server delta notifications
- User-visible tool activity through SignalR (`tool.started`, `tool.completed`, and `tool.failed`); only tool names and run status are sent, never arguments or results
- PostgreSQL via EF Core/Npgsql, with full-text indexes for memory and documents
- Owner-scoped memory and document search using PostgreSQL full-text and trigram search merged with reciprocal rank fusion; pinned, unexpired memories are included in bounded agent context, and up to eight memory candidates are reranked through the same Codex CLI + ChatGPT OAuth path, with an eight-second fallback to PostgreSQL order
- Optional MCP stdio tools, constrained to an explicit tool allowlist
- Per-run MCP connections that inject each owner's encrypted integration credentials only into the configured transport headers or child-process environment, then redact those values from tool results and errors before returning them to the agent; an unavailable optional server is isolated and skipped so it does not interrupt chat
- ASP.NET Core Identity accounts stored with Entity Framework, with bearer authentication and per-user ownership outside Development
- Flutter email and password sign-in and registration; access and refresh tokens use OS secure storage on mobile and WebCrypto-backed `flutter_secure_storage` on the web
- User-managed memory with category filters, search, edit, pin, delete, and visible superseded history; Codex-based extraction deduplicates repeats and transactionally supersedes only explicit corrections to unpinned memories
- Agent Framework truncation compaction trims old conversation groups above 80,000 tokens to below 64,000 while preserving the four newest groups and keeping tool-call groups intact; full history remains in the session store
- Agent Framework provides owner-scoped queued, running, and approval-waiting task context to interactive conversations, with task content labeled as untrusted reference data
- Conversation history with selection and deletion in the mobile client; deleting a conversation also removes its session, messages, approvals, and memories learned from those messages, while task sessions remain task-managed
- Human review for MCP tool calls unless a tool is explicitly configured for auto-approval; interrupted approval resumes remain visible and can be retried
- Idempotent approval persistence guarded by a unique owner/request/tool-call key
- Durable reminders scheduled through Temporal, including optional daily, weekday, and weekly recurrence in the owner's IANA time zone, with worker-delivered in-app notifications and a database-backed dispatcher recovery path
- Durable Temporal condition watches for numeric values from public JSON HTTPS endpoints, with bounded polling, owner-scoped cancellation, deterministic threshold checks, and notification/push delivery
- User-configurable daily morning briefings scheduled by Temporal at an IANA local time zone, with idempotent summaries of that day's reminders (including recurring next fires) and active tasks, plus an optional short persona-aware intro that falls back to the deterministic list if the model is unavailable
- Reminder and task notifications open their owner-scoped source item from the mobile client
- Approval-needed notifications are published over the owner-scoped SignalR group and open the approval screen from a foreground snackbar; tool arguments are not included in the event
- Durable APNs/FCM push delivery for reminders, completed tasks, and approvals, backed by owner-scoped device registrations, transactional delivery rows, leased retries, and expired-token cleanup
- Owner-scoped integration credentials protected with ASP.NET Core Data Protection; API responses expose configured names only, never credential values
- Flutter integration settings for adding, rotating, and deleting hidden owner-scoped MCP credentials, with per-owner MCP connection status from `GET /api/v1/integrations/connections` (tool counts only; endpoints and error details stay private)
- Private file upload, download, and recoverable deletion using S3-compatible object storage with PostgreSQL metadata; uploads enforce an allowlisted MIME/extension/signature, verify the full bounded stream, pass ClamAV malware scanning before storage, and dispatch indexable content to Temporal for extraction
- Durable PDF and text file extraction through Temporal, owner-scoped PostgreSQL chunks, and full-text search available in chat
- Durable tasks can be started or cancelled from chat, run by Temporal workers, and shown in the mobile app; pending tool approvals are linked to their exact task and survive activity retries. Task sessions stay separate from interactive chat history.
- Task details expose the owner-scoped task conversation and full assistant result, with completed-task notifications deep-linking to that detail view
- Temporal workflow starts use stable workflow IDs; long-running activities heartbeat and honor cancellation
- Model calls through the Codex CLI app-server, using the signed-in ChatGPT OAuth session and the account's default available Codex model; the account model catalog is cached for five minutes and refreshed through the app-server
- Current web search through Codex CLI's native `standalone_web_search` feature and the same signed-in ChatGPT OAuth session; no separate search-provider API key is used, search results are treated as untrusted, and completed search actions are counted without recording queries or result content
- Logical `fast`, `standard`, `reasoning`, `coding`, `vision`, and `realtime` model classes can map to models available to the signed-in Codex account. Each owner can switch Settings → Models to OpenRouter, store an encrypted API key, and pick chat/fast/reasoning/coding/vision/embedding models from the OpenRouter catalog. Codex remains the default for chat when OpenRouter is not selected; you can keep Codex for chat and add only an OpenRouter embedding model for semantic memory. Settings → Models also lists the models reported by the installed Codex CLI and can install a newer `@openai/codex` release into `$CODEX_HOME/cli`. The API, worker, and voice worker use that updated CLI when they share `CODEX_HOME` and are still pointed at the stock `codex` executable. An owner-selected Codex chat model is used for conversations; leave it empty to keep the server model class or the account default.
- Approval-gated Codex coding tasks in fresh detached worktrees from an explicit repository allowlist
- Owner-scoped append-only audit log for approvals, coding runs, task/reminder lifecycles, files, and memory changes
- .NET Aspire AppHost for local API, PostgreSQL, and Temporal development server orchestration
- LiveKit development server plus Flutter `livekit_client` room publishing and a LiveKit Agents voice worker bridged to Codex realtime through the app-server WebSocket transport
- OpenTelemetry traces and metrics via OTLP, including correlated agent/tool/model spans, agent duration, time-to-first-token, model and tool latency/outcomes, Codex-reported input/output/cached/reasoning token counts, native Codex web-search action counts, memory-search latency/hit counts, and Temporal worker operation spans; telemetry excludes prompts, memory queries, search queries, tool arguments, and results

Development uses a fixed local owner ID when a request has no access token, so local scripts can run without signing in. A signed-in request uses that account's Identity user ID in every environment. Outside Development, the API requires a Jarvis-issued bearer token. Production startup requires `Authentication:Issuer`, `Authentication:Audience`, and `Authentication:SigningKey`. Configure those values and your web origins before exposing the API. Jarvis sends model prompts and relevant conversation context to the Codex CLI app-server, which must be signed in with ChatGPT OAuth on the host or container. The adapter opens an ephemeral thread in an empty temporary directory with a read-only sandbox, disables Codex shell, browser, computer, app, plugin, skill, image-generation, and multi-agent tools plus MCP servers, and enables only Codex's native standalone web search when configured. The child process receives only OAuth/runtime environment variables; shell network access stays disabled. Codex app-server message-delta events stream chat, while structured Jarvis tool requests are executed separately by Agent Framework through Jarvis's permission and approval path. The app-server and native standalone web-search feature are experimental and should be versioned alongside the Codex CLI installation.

## Run locally

Sign in to the Codex CLI with your ChatGPT account, then start the local dependencies and app:

```sh
cp .env.example infra/compose/.env
```

Edit `infra/compose/.env` with strong local database and S3-compatible storage secrets. Confirm the Codex CLI session:

```sh
codex login status
```

For Aspire, run the API and local dependencies from the repository root:

```sh
set -a
source infra/compose/.env
set +a
dotnet run --project src/Jarvis.AppHost
```

Database migrations apply automatically in Development.

## Tests

Run deterministic unit tests with:

```sh
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
python3 -m unittest tests/unit/altstore/test_generate_source.py tests/unit/compose/test_production_images.py tests/unit/release/test_semver.py
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

Aspire exposes the Temporal development UI at `http://localhost:8233`. To run the API, worker, PostgreSQL, and Temporal together in Docker Compose instead, set `CODEX_AUTH_FILE` in `.env` to the absolute path of the Codex CLI `auth.json` created by `codex login`, then use:

```sh
docker compose --profile development --env-file infra/compose/.env -f infra/compose/docker-compose.yml up -d
```

To connect Home Assistant as well, set `HOME_ASSISTANT_MCP_URL` in the Compose env file and add the Home Assistant overlay:

```sh
docker compose --profile development --env-file infra/compose/.env \
  -f infra/compose/docker-compose.yml \
  -f infra/compose/docker-compose.home-assistant.yml up -d
```

To enable GitHub in Compose, add its overlay and rebuild the API image so it includes the pinned official MCP server:

```sh
docker compose --profile development --env-file infra/compose/.env \
  -f infra/compose/docker-compose.yml \
  -f infra/compose/docker-compose.github.yml up --build -d
```

The API is available at `http://localhost:5082`; OpenAPI is at `/openapi/v1.json` in Development. The API, Temporal worker, and voice worker mount the configured Codex CLI OAuth `auth.json` so their CLI subprocesses can authenticate. Treat that file as a credential; Compose writes refresh updates back to the host file. This Compose profile enables the Development identity bypass and binds the API to loopback by default. Do not use this profile as a production deployment. Aspire and Compose use the same local ports; run one orchestration option at a time.

File uploads are scanned by a private ClamAV daemon before object storage. The development orchestrators persist its signature database and wait for ClamAV readiness; first startup may take several minutes while signatures download. Production must configure `Antivirus:Host` and keep the daemon private to the application network. Jarvis fails closed when the scanner is unavailable and rejects detected files before storage.

To include the isolated Playwright MCP browser in local development, enable its opt-in profile and Compose override:

```sh
docker compose --profile development --profile browser --env-file infra/compose/.env \
  -f infra/compose/docker-compose.yml \
  -f infra/compose/docker-compose.browser.yml up -d
```

Voice uses Flutter LiveKit rooms and a LiveKit Agents worker. The API checks conversation ownership, asks the installed Codex CLI for its voice-mode voices (`thread/realtime/listVoices`), and dispatches the worker with the owner's saved voice or that CLI's own default. It returns a ten-minute room JWT plus the owner's hands-free and caption preferences. The worker bridges room PCM audio to Codex CLI's realtime voice session and publishes the returned audio directly back into the LiveKit room. Codex's realtime handoff calls a single owner-scoped MCP tool in the worker; that tool sends the transcript through Jarvis's normal conversation, memory, tools, and approval pipeline. Codex then speaks the response in the same realtime session, avoiding the prior text-response-to-speech append loop. The worker posts live caption deltas to the conversation SignalR group and keeps the mounted Codex CLI OAuth session; it does not use a model-provider API key. Set `VOICE_WORKER_SECRET` in `.env` to a unique random value before running Compose. Hands-free listening, captions, and the CLI voice list are under Settings → Voice. The realtime app-server protocol is experimental and must stay version-aligned with the pinned Codex CLI; audio behavior still needs validation with a signed-in account and a physical device.

The audit log is available from the mobile app's Audit log action and `GET /api/v1/audit`. It stores action names, risk classes, outcomes, owner IDs, and limited resource metadata; it does not store prompts, file contents, or memory contents. The application rejects update and delete operations on audit events.

Integration credentials can be managed from the Flutter Integrations screen or through the API. `PUT /api/v1/integrations/{provider}/credentials/{secretName}` adds or replaces one secret without returning its value; `DELETE` on that path removes only that secret. `GET /api/v1/integrations/credentials` lists configured provider names and secret field names, while `DELETE /api/v1/integrations/{provider}/credentials` removes every secret for that provider. The full-set `PUT /api/v1/integrations/{provider}/credentials` also remains available for automation. Secret values are protected with ASP.NET Core Data Protection before PostgreSQL persistence and never enter agent context. Set `DataProtection__KeysDirectory` to a persistent, private writable key-ring directory outside Development. For production Compose, create the configured host directory with mode `0700` and ownership matching `JARVIS_UID`/`JARVIS_GID`; back it up securely because losing the key ring makes stored credentials unreadable. MCP adapters can retrieve these values through the server-side `IIntegrationCredentialStore` contract. Development Compose persists its key ring in a named volume, and Aspire uses the current user's default ASP.NET Core key store.

The browser container has no published host port, runs without the Docker socket, has resource limits, and lives on an internal Docker network. Chromium uses a dedicated Squid egress proxy; the proxy blocks loopback, private, link-local, multicast, and reserved IP ranges plus local-only hostnames, including on redirects. Browser navigation and interaction tools require approval; page snapshots, searches, screenshots, and console reads are allowlisted as read-only. Chat can start an isolated session with `BrowseTheWeb`; the app shows each Playwright step on a timeline. This opt-in development configuration is for the single local Development identity. Browser profiles and sessions still need per-user isolation before enabling the browser in a multi-user deployment.

Aspire registers the current Git checkout as the `jarvis` coding repository. When Jarvis proposes a coding task, the user must approve it; Codex then edits a fresh detached worktree outside the primary checkout using its workspace-write sandbox. For repositories without a commit, or with tracked credential-like files, it creates a separate Git snapshot from nonignored files while filtering common credential paths. Changes stay uncommitted and the tool returns the workspace path and diff summary for review. Docker Compose does not expose a coding repository, so coding stays disabled there; enabling it requires a configured Git repository mount and a separate writable workspace volume.

PDF, text, Markdown, CSV, JSON, JPEG, PNG, and WebP uploads are indexed asynchronously by the Temporal worker. Image text is extracted with the same signed-in Codex CLI app-server and ChatGPT OAuth model path used for chat (up to 8 MiB per image). Jarvis can search extracted text when answering chat requests; document contents remain untrusted input and are not promoted into system instructions. Images without legible text finish indexing as `ready` with no text chunks. The files screen shows `queued`, `processing`, `ready`, or `failed` for indexable files.

Condition watches can be created in chat or from Tasks → Condition watches. Each watch polls a public, credential-free HTTPS JSON endpoint for one numeric dot-separated object property and alerts when it reaches a `below` or `above` threshold. Intervals are 5 minutes to 24 hours. Requests reject redirects, private/reserved IPs, local hostnames, non-JSON responses, and bodies over 1 MiB. Checks are deterministic Temporal activities and do not invoke a model. Authenticated APIs and ordinary HTML pages are not supported by this watch source.

The daily morning briefing can be enabled and configured from the Jarvis menu or through `PUT /api/v1/briefings/daily` with `enabled`, `localTime` (`HH:mm:ss`), and `timeZoneId` (for example, `Europe/Amsterdam`). Temporal keeps one durable schedule per owner and applies time-zone daylight-saving changes. Each day's notification is idempotent and includes reminders due that local day plus queued, running, or approval-waiting tasks. Jarvis then optionally writes a 1–3 sentence persona-aware intro through the same background model path as heartbeat reflection; if that rewrite times out or fails, the notification keeps the deterministic list. Reminder and task titles are treated as untrusted data.

The bundled Temporal server uses its development mode and SQLite persistence. A production deployment needs a supported Temporal server deployment with durable production storage, TLS/authentication, and operational monitoring. The API and worker use `Temporal__Address` (default `localhost:7233`).

## Production Compose

`infra/compose/docker-compose.production.yml` is a separate production topology. It runs Jarvis behind Caddy with persistent PostgreSQL, a PostgreSQL-backed Temporal server, private Garage S3-compatible storage, ClamAV, LiveKit, and the Jarvis API/workers. Only Caddy's HTTP/HTTPS ports and LiveKit's required media ports are published; Postgres, Temporal, Garage, and ClamAV remain on private Docker networks.

Copy `infra/compose/.env.production.example` to `infra/compose/.env.production`, set its mode to `0600`, and replace every placeholder. Set `JARVIS_UID`/`JARVIS_GID` to the account that owns `CODEX_HOME_DIR`, configure the account issuer, audience, signing key, and DNS names, and sign the Codex CLI in with ChatGPT OAuth using `CODEX_HOME="$CODEX_HOME_DIR" codex login`. Use a dedicated persistent directory for `CODEX_HOME_DIR`, owned by that numeric account with mode `0700`; it stores `auth.json`, writable Codex app-server state, and Settings-driven CLI updates under `cli/`. Those updates survive container recreation because the containers' root filesystems are read-only. The API and both workers mount this directory and run as that unprivileged user. Development Compose shares the same `cli/` directory through the `codex-cli` volume.

With DNS for `JARVIS_DOMAIN` and `LIVEKIT_DOMAIN` pointing at the host, start the stack with:

```sh
docker compose --env-file infra/compose/.env.production \
  -f infra/compose/docker-compose.production.yml up --build -d
```

`--build` compiles the API, Temporal worker, and voice worker on the host. After the GitHub Actions deploy pipeline is configured, prefer pulling the prebuilt GHCR images instead:

```sh
export JARVIS_API_IMAGE=ghcr.io/<owner>/jarvis-ai/api:<sha>
export JARVIS_WORKER_IMAGE=ghcr.io/<owner>/jarvis-ai/worker:<sha>
export JARVIS_VOICE_WORKER_IMAGE=ghcr.io/<owner>/jarvis-ai/voice-worker:<sha>
scripts/deploy/remote-up.sh
```

Add `-f infra/compose/docker-compose.home-assistant.yml` to that command and set `HOME_ASSISTANT_MCP_URL` in the production env file to enable Home Assistant.

Add `-f infra/compose/docker-compose.github.yml` to enable GitHub. Host builds still need `--build` so the API image includes the pinned MCP server; GHCR images already include it.

Allow inbound TCP 80/443 for Caddy, TCP 7881 and UDP 50000-50100 for LiveKit media, plus outbound HTTPS for Codex OAuth/model access, Firebase FCM/OAuth, user-configured public condition-watch endpoints, Caddy certificate issuance, ClamAV signature updates, and GHCR pulls. The API applies EF migrations at startup after Postgres is healthy. Temporal owns a separate persistent Postgres database. Garage creates the private `jarvis-files` bucket and its application access key before the API starts. Keep the Compose environment file, Garage config, and Codex OAuth file private, and pin every third-party container image to an audited release or digest before deploying. This production topology has been configuration-validated; live service startup still requires pulling its container images and setting real account signing keys, DNS, Firebase, APNs, and Codex OAuth credentials.

## GitHub Actions pipelines

Releases use [Semantic Versioning 2.0.0](https://semver.org/): git tags are `vMAJOR.MINOR.PATCH` (for example `v1.2.0`). When a pull request merges to `main`, [`.github/workflows/create-release-tag.yml`](.github/workflows/create-release-tag.yml) reads the **SemVer bump** checkboxes in [`.github/pull_request_template.md`](.github/pull_request_template.md), creates the next `v*` tag on the merge commit, and pushes it. That tag triggers the iOS IPA and backend deploy workflows. You can still run either release workflow manually from **Actions** with `workflow_dispatch`, or push a `v*` tag yourself in an emergency.

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

The iOS release workflow publishes both the IPA and generated `source.json` to the Jarvis server. It updates automatically for `v*` releases; a manual workflow run also publishes the selected version without creating a GitHub Release. Add `https://jarvis.ykdbonte.dev/altstore/source.json` to AltStore. Each run keeps earlier IPA versions available for existing source entries. The feed and download are public; keep private data out of the IPA and source metadata.

The self-hosted runner serves `/home/ykdbonte/.jarvis/altstore` through the host Caddy route at `/altstore/*`. The public endpoint is independent of GitHub repository visibility, and the workflow updates the source only after the IPA artifact is ready.

### Backend GHCR images and deployment

[`.github/workflows/deploy-backend.yml`](.github/workflows/deploy-backend.yml) builds `api`, `worker`, and `voice-worker`, pushes them to `ghcr.io/<owner>/jarvis-ai/<name>:<git-sha>` (plus the version tag and `latest` on `v*` tags), then deploys through a self-hosted runner on your server. Garage uses its pinned upstream image. The workflow runs when a `v*` release tag is created (automatically after merge to `main`, or manually), not on every commit to `main`.

Server bootstrap:

1. Clone this repository to a persistent path such as `/opt/jarvis`.
2. Copy `infra/compose/.env.production.example` to `infra/compose/.env.production`, mode `0600`, and fill in real secrets. Set `GARAGE_CONFIG_FILE` to the private Garage config file path. The deploy job supplies the API and worker image names and commit tag.
3. Install Docker with the Compose plugin. The checkout must be able to `git fetch` this repository; the workflow uses its short-lived `GITHUB_TOKEN` for the fetch and GHCR pull.
4. Add repository secret `DEPLOY_PATH` for the production checkout path on the server.
5. Register a persistent Linux x64 self-hosted runner on the production server with the `jarvis-deploy` label. Run it as the account that owns the checkout and production env file, with Docker and Compose access. The build jobs stay on GitHub-hosted runners; only the deploy job runs on the server.
6. Set repository variable `DEPLOY_COMPOSE_FILES` to `infra/compose/docker-compose.production.tunnel.yml` when using a host-level reverse proxy or Cloudflare Tunnel instead of the bundled public Caddy edge. Optional overlays can be space-separated after it, for example `infra/compose/docker-compose.github.yml infra/compose/docker-compose.home-assistant.yml`.
7. In GitHub → Packages, link the three application container packages to this repository so `GITHUB_TOKEN` can push and the deploy job can pull. Keep packages private if the repo is private; the runner logs into GHCR with a short-lived token. For later manual pulls, `docker login ghcr.io` on the host with a PAT that has `read:packages`.

`workflow_dispatch` accepts `skip_deploy` to build/push images without deploying, and an optional extra `image_tag`. Production secrets stay in `.env.production` on the server and are never passed through GitHub Actions. `python3 -m unittest tests/unit/compose/test_production_images.py` checks that Compose interpolates the GHCR image variables.

MCP tools are disabled unless configured. Set `Mcp__Servers__0__Name`, `Mcp__Servers__0__Transport`, and `Mcp__Servers__0__AllowedTools__0`; stdio servers also need `Mcp__Servers__0__Command` and optional arguments/environment, while Streamable HTTP servers need `Mcp__Servers__0__Endpoint` and optional headers. Tools are approval-required by default; add an exact tool name to `Mcp__Servers__0__AutoApprovedTools__0` only when unattended execution is intended. Stdio children receive a minimal environment by default. To inject an owner's encrypted integration credentials into a stdio child, set `Mcp__Servers__0__CredentialProvider`, map a child variable to a stored secret name under `CredentialEnvironmentVariables`, and store those values under the same provider slug. For Streamable HTTP, map headers with `CredentialHeaders`; sending mapped credentials requires HTTPS. Store the complete header value (including a `Bearer` scheme when required) in the encrypted credential record. The API opens a scoped MCP connection for each agent run and disposes it when the run finishes. Stored or configured transport values are scrubbed from MCP tool results and errors before those values can reach the model context.

Jarvis can manage MCP servers in conversation. `list_mcp_servers` and `list_mcp_connections` show registered servers and the tools connected for this turn, including host servers such as GitHub. `discover_mcp_server_tools` returns tool names, descriptions, required arguments, prompts, and resources for a public HTTPS endpoint or for a server that already has a stored token. `add_mcp_server`, `update_mcp_server`, `set_mcp_server_enabled`, `set_mcp_server_tools`, and `remove_mcp_server` change that registration. Add, update, enable, tool changes, and remove require approval. An allowlist is 1 to 80 exact tool names, or `*` for every tool the server exposes. The agent can also narrow or pause a host server such as GitHub, but it cannot enable tools the operator left out of that server's configuration, and it cannot add stdio commands or private endpoints. `invoke_mcp_tool`, `read_mcp_resource`, and `get_mcp_prompt` use an enabled server during the current turn; direct tools from a new or changed server appear on the next turn. Every one of those calls requires approval. After adding a remote server, store its optional bearer token in Integrations using the returned `jarvis-mcp-…` provider ID and the secret name `token`. Server definitions, host-server pauses, and credentials are encrypted in the owner-scoped integration credential store. The Integrations screen can pause a server, and `GET/POST/PUT/DELETE /api/v1/mcp-servers` plus `PUT /api/v1/mcp-servers/{id}/state` and `PUT /api/v1/mcp-controls/{name}` expose the same controls outside chat.

Home Assistant's first-party MCP Server integration is supported over its Streamable HTTP `/api/mcp` endpoint. Enable the integration in Home Assistant and expose only the entities Jarvis may access. Set `HOME_ASSISTANT_MCP_URL` to its HTTPS `/api/mcp` URL, then include `infra/compose/docker-compose.home-assistant.yml` when starting either development or production Compose. In Jarvis → Integrations, use **Set or rotate token** and paste a Home Assistant long-lived access token; Jarvis stores it under `home-assistant` → `token`, encrypted, and adds the `Bearer` scheme only when connecting. The Compose overlay allowlists the Home Assistant server's exposed tools; Jarvis requires approval for every call by default. Use HTTPS because the per-owner token is sent in an Authorization header. See the [Home Assistant MCP Server setup](https://www.home-assistant.io/integrations/mcp_server) for enabling the server and exposing entities.

GitHub uses the [official GitHub MCP server](https://github.com/github/github-mcp-server), pinned to v1.12.2 and built into the API container. The Compose overlay launches it over stdio with only its repository, issue, and pull request toolsets enabled. Store a least-privilege GitHub personal access token in Jarvis → Integrations under `github` → `token`; it is injected into that server process for the signed-in owner only. All exposed GitHub tools require approval. Model inference stays on Codex unless the owner selects OpenRouter in Settings → Models.

The Flutter default API URL is `http://localhost:5082`. On Android emulators use `--dart-define=JARVIS_API_URL=http://10.0.2.2:5082`. On a physical device, point the API URL at the machine's reachable address and configure the Compose port bindings and firewall for the device. The app expects the API host to be reachable and joins `/hubs/events` for incremental response updates.

Mobile push uses Firebase Cloud Messaging for both Android and iOS; configure an APNs authentication key for the iOS app in the Firebase project. Pass `JARVIS_FIREBASE_API_KEY`, `JARVIS_FIREBASE_PROJECT_ID`, `JARVIS_FIREBASE_SENDER_ID`, and the platform's `JARVIS_FIREBASE_ANDROID_APP_ID` or `JARVIS_FIREBASE_IOS_APP_ID` as Flutter `--dart-define` values. Set `JARVIS_FIREBASE_IOS_BUNDLE_ID` if the iOS bundle ID differs from `com.example.jarvis_mobile`. The iOS target declares Push Notifications and Background Modes/Remote notifications; use a real signing profile with APNs enabled. The app requests notification permission after sign-in, registers token refreshes with Jarvis, and removes the device registration on sign-out. In production, set `FIREBASE_PROJECT_ID` and `FIREBASE_SERVICE_ACCOUNT_FILE` in the protected Compose environment file; the service-account file is mounted read-only into the API and must be readable by `JARVIS_UID`. Leave the project ID empty to disable server push delivery. SignalR and the in-app notification list continue to work without Firebase configuration.

Sign in from the Flutter app with email and password. The API stores accounts with ASP.NET Core Identity and Entity Framework, returns a short-lived JWT plus a rotating refresh token, and uses the account ID as the owner ID. Tokens stay in `flutter_secure_storage` (OS secure storage on mobile, WebCrypto in the browser). Serve the web app over HTTPS (or localhost) and configure that origin in `Cors:AllowedOrigins`. Set `Authentication__AllowRegistration=false` after the accounts you need have been created.

## Configuration

Important settings are in `src/Jarvis.Api/appsettings.json` and may be overridden by environment variables:

- `ConnectionStrings__jarvis`: PostgreSQL connection string supplied automatically by Aspire.
- `Codex__ExecutablePath`: Codex CLI executable (default `codex`). Settings updates apply only when this is the stock `codex` executable; a custom path, such as the end-to-end fixture, is left unchanged.
- `Codex__ManagedInstallDirectory`: directory for Settings-driven Codex CLI updates (default `$CODEX_HOME/cli`, or `~/.codex/cli` when `CODEX_HOME` is unset). The image-pinned CLI remains the fallback until an update is installed.
- `Codex__NpmExecutablePath`: npm used to install those updates (default `npm`).
- `Codex__EnableWebSearch`: enable Codex CLI's native standalone web search (default `true`); set to `false` to disable search while keeping the other Codex restrictions. This CLI feature is experimental and depends on the installed Codex version.
- `Codex__Model`: optional model identifier passed to each Codex CLI app-server thread; by default, Jarvis selects the default available model reported by the signed-in Codex account.
- `Jarvis__ModelClass`: optional root-agent class such as `standard` or `reasoning`; it must have a corresponding Codex model mapping.
- `Codex__ModelClasses__Fast`, `__Standard`, `__Reasoning`, `__Coding`, `__Vision`, and `__Realtime`: optional exact model identifiers from the signed-in Codex account. The coding tool, image requests, and voice worker select their respective classes when configured; every selection is checked against the account's model catalog and required input modality.
- `Codex__VisionModel`: optional Codex account model identifier for image inputs. Jarvis checks the signed-in app-server model catalog for input modality support; when unset, image requests automatically select an available image-capable model.
- `Coding__Repositories__0__Name` and `Coding__Repositories__0__Path`: explicit repository allowlist that enables the approval-gated coding tool. Aspire sets this to the current Git checkout.
- `Coding__WorktreeRoot`: optional parent directory for isolated coding workspaces. Defaults to `.jarvis-worktrees` beside the configured repository.
- `Coding__TimeoutSeconds`: coding-task time limit from 60 to 3,600 seconds (default 900).
- `Authentication__Issuer`: required outside Development; a stable token issuer such as `https://jarvis.example.com`.
- `Authentication__Audience`: required outside Development; the API audience, usually `jarvis-api`.
- `Authentication__SigningKey`: required outside Development; at least 32 random bytes. Do not reuse the Development key.
- `Authentication__AllowRegistration`: allow `POST /api/v1/auth/register` (default `true`). Set `false` after the owner account exists.
- `Authentication__AccessTokenMinutes` and `Authentication__RefreshTokenDays`: token lifetimes (defaults 15 minutes and 30 days).
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OpenTelemetry collector or Aspire Dashboard endpoint.
- `Mcp__Servers__0__Name`, `Mcp__Servers__0__Transport`, `Mcp__Servers__0__Command`, `Mcp__Servers__0__Endpoint`, `Mcp__Servers__0__AllowedTools__0`, and `Mcp__Servers__0__AutoApprovedTools__0`: optional MCP server settings. Supported transports are `stdio` and `streamableHttp`. Only explicitly allowlisted tools reach the agent; tools require approval unless named in `AutoApprovedTools`.
- `Mcp__Servers__0__CredentialProvider`, `Mcp__Servers__0__CredentialEnvironmentVariables__TOKEN`, and `Mcp__Servers__0__CredentialHeaders__Authorization`: map encrypted owner-scoped integration secrets to an MCP server's process environment or HTTPS headers. Credential map values name secret fields in `PUT /api/v1/integrations/{provider}/credentials`.
- `Mcp__Servers__0__CredentialHeaderPrefixes__Authorization`: optional validated prefix (for example, `Bearer`) added to an encrypted credential when constructing an MCP HTTP header. Secret values and the constructed header are both redacted from tool results.
- `Temporal__Address`: Temporal frontend address used by the API and reminder worker.
- `LiveKit__ApiKey`, `LiveKit__ApiSecret`, `LiveKit__InternalUrl`, and `LiveKit__PublicUrl`: worker dispatch and short-lived mobile room token settings. Local development uses the configured `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` (the secret must contain at least 32 UTF-8 bytes); set Compose bind addresses and the public URL to the machine's LAN address when connecting from a physical device.
- `Voice__WorkerSecret`: shared secret between the API and LiveKit voice worker for transcript callbacks. Configure it from a secret store outside local development.
- `Channels__PublicBaseUrl` or `Jarvis__PublicBaseUrl`: public origin used in WhatsApp webhook URLs and the Agent2Agent card (`https://jarvis.example.com`). When unset, the API uses the incoming request host.
- `Channels__Signal__BaseUrl`: signal-cli REST endpoint (for example `http://127.0.0.1:8080`). Required before a Signal channel can be connected in the app. Leave unset to keep Signal disconnected.
- `Channels__WhatsApp__GraphBaseUrl`: WhatsApp Cloud API origin (default `https://graph.facebook.com/v21.0`).
- `Push__FirebaseProjectId` and `Push__GoogleServiceAccountFile`: enable the API's durable FCM sender (which relays iOS pushes through Firebase/APNs); keep the service-account file outside the repository and mount it read-only.
- `LIVEKIT_URL`, `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`, `JARVIS_INTERNAL_API_URL`, and `VOICE_WORKER_SECRET`: voice worker settings. Model inference is through the Codex CLI app-server only unless the owner selected OpenRouter for chat.
- `JARVIS_BIND_ADDRESS`, `JARVIS_LISTEN_URL`, `LIVEKIT_BIND_ADDRESS`, and `LIVEKIT_PUBLIC_URL`: local device reachability. They default to loopback (`JARVIS_LISTEN_URL=http://localhost:5082` for Aspire). For a physical device, set Compose bind addresses or the Aspire listen URL to the machine's LAN interface, and set `LIVEKIT_PUBLIC_URL=ws://<machine-lan-address>:7880`. Local development uses the configured `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` (the secret must contain at least 32 UTF-8 bytes).
- `ObjectStorage__ServiceUrl`, `ObjectStorage__AccessKey`, `ObjectStorage__SecretKey`, `ObjectStorage__Bucket`, and `ObjectStorage__Region`: S3-compatible object storage. Local Aspire uses SeaweedFS; production should point to a supported S3-compatible service and provision a private bucket with least-privilege credentials.
- `Antivirus__Host`, `Antivirus__Port`, and `Antivirus__TimeoutSeconds`: private ClamAV daemon endpoint and upload scan timeout. The port defaults to `3310`; timeout defaults to 60 seconds.

## Repository layout

`apps/mobile` contains Flutter. `src/Jarvis.Api` is the HTTP and SignalR edge; Application and Domain hold contracts and entities; Infrastructure owns EF Core/PostgreSQL; Agents owns Microsoft Agent Framework and the Codex CLI adapter. `src/Jarvis.Workflows` defines Temporal workflows and `workers/Jarvis.Worker` hosts their activities.
