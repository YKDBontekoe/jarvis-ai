# Configuration reference

Primary file: `src/Jarvis.Api/appsettings.json`. Override with environment variables using `__` nesting (ASP.NET Core convention).

## Database and storage

| Key | Purpose |
|-----|---------|
| `ConnectionStrings__jarvis` | PostgreSQL |
| `ObjectStorage__*` | S3 endpoint, keys, bucket, max upload |
| `Database:ApplyMigrationsAtStartup` | Migrate on boot (non-dev optional) |

File storage uses path-style S3 requests with fixed-length, SigV4-signed upload payloads.
The client calculates optional AWS checksums only when required, avoiding checksum trailers
that self-hosted S3 implementations may reject as `Invalid payload signature`. Payload signing,
the upload SHA-256 hash, file validation and ClamAV scanning remain enabled. This compatibility
setting is built into the client; no additional storage secrets or environment variables are needed.

## Codex and models

| Key | Purpose |
|-----|---------|
| `Codex__ExecutablePath` | CLI binary (default `codex`) |
| `Codex__ManagedInstallDirectory` | Settings-driven CLI updates |
| `Codex__EnableWebSearch` | Codex hosted live web search for chat turns |
| `Codex__Access__Sandbox` | `workspace-write` (default), `read-only` or `danger-full-access`. Applies to Codex's own shell and file tools, never to Jarvis functions |
| `Codex__Access__AllowShell` | Offer Codex's shell tool (default `true`). Commands run in the sandbox without an approval card |
| `Codex__Access__AllowNetwork` | Network for commands Codex runs, and proxy/CA variables passed to the child (default `true`) |
| `Codex__Access__WritableRoots__0` | Extra absolute directories writable in `workspace-write` mode, besides the per-turn scratch directory |
| `Codex__Access__DisabledFeatures__0` | Extra Codex features to turn off (for example `image_generation`, `apps`, `plugins`, `skill_search`) |
| `Computer__ControlUrl`, `Computer__ViewUrl`, `Computer__Token` | Computer-use sandbox (set by the AppHost `computer` feature from `COMPUTER_SANDBOX_TOKEN`). Empty `ControlUrl` turns computer use off |
| `Computer__IdleTimeoutMinutes` | 5–1440, default 30. After this long unused, another conversation may claim the sandbox |
| `Finance__Quotes__ApiKey` | Optional [Finnhub](https://finnhub.io) API key. With it the stock portfolio fetches live prices (reused for 15 minutes; sent in a header, never logged). Without it prices are the ones the owner types in |

For production, set the GitHub Actions repository secret `FINANCE_QUOTES_API_KEY`. Both approved deployment
workflows pass it to `prepare-host.sh`, which saves it in the host's mode-`0600` production env file before
Compose starts the API and worker. A non-empty secret replaces the saved key; an absent secret preserves an
existing host key. It is a runtime setting and is never included in the mobile/web build or image build arguments.
Changing the secret takes effect at the next approved deployment.

To restore the old locked-down behavior set `Codex__Access__Sandbox=read-only`, `Codex__Access__AllowShell=false`, `Codex__Access__AllowNetwork=false` and disable `skill_search`, `image_generation`, `apps`, `plugins`. Codex's browser, computer-use and multi-agent features always stay off; Jarvis's approval-gated browser and its sandbox computer (`computer` feature) are the only browser and desktop.
| `Codex__Model`, `Jarvis__ModelClass` | Default model selection |
| `Codex__ModelClasses__*` | fast/standard/reasoning/coding/vision/realtime |
| `Codex__TurnTimeoutSeconds` | 30–1800 |
| `Codex__MaxConcurrentProcesses` | 1–16, default 2. Cap on concurrent Codex processes. Background work (memory extraction, reranking, triage) can hold all but one slot, so an interactive turn never queues behind it. |

Owner overrides via API `/settings/models` (OpenRouter key encrypted).

### Local embedding model

Semantic memory search needs an embedding model. The production deployment includes an `embeddings` service
(Hugging Face Text Embeddings Inference, CPU) that downloads the model on first start into the `embeddings-data`
volume and serves an OpenAI-compatible `/v1/embeddings`. API and worker use it through these keys, so no key or account is needed:

| Key / env var | Purpose |
|---------------|---------|
| `Embeddings__BaseUrl` (`EMBEDDINGS_BASE_URL`) | Endpoint; defaults to `http://embeddings:80/v1`. Set it empty to switch the local model off, or point it at any OpenAI-compatible server |
| `Embeddings__Model` (`EMBEDDINGS_MODEL`) | Model id; defaults to `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` (multilingual, 384 dimensions, zero-padded to the 1536-wide column) |
| `Embeddings__ApiKey` | Optional bearer key for an external server |
| `EMBEDDINGS_IMAGE` | Production image, default `ghcr.io/huggingface/text-embeddings-inference:cpu-1.8` |

An owner's OpenRouter embedding model (Settings → Models) overrides the local one. Changing the model re-embeds all
memories in the background. The app shows the active model and how many memories are indexed
(`GET /settings/models/embedding`). The server needs about 2 GB of RAM for the container (measured on 4 CPU threads:
about 14 ms per query and 8 ms per memory for the default model, in PyTorch; the Text Embeddings Inference speed was not measured).

## Auth

| Key | Purpose |
|-----|---------|
| `Authentication__Issuer`, `__Audience`, `__SigningKey` | Required outside Development |
| `Authentication__AllowRegistration` | Default true |
| `Authentication__AccessTokenMinutes`, `__RefreshTokenDays` | Token lifetimes |
| `RateLimiting__AuthPermitsPerMinute` | Requests per client IP per minute to `/api/v1/auth/*` and the MCP OAuth callback (default 10) |
| `RateLimiting__PublicPermitsPerMinute` | Requests per client IP per minute to `/a2a`, the agent card, and WhatsApp webhooks (default 120) |
| `ReverseProxy__TrustForwardedHeaders` | Use the last `X-Forwarded-For` hop as the client IP (default false). The production deployment sets it because the API is only reachable through Caddy; leave it off when the API is exposed directly |
| `Cors:AllowedOrigins` | Flutter web origins |

## Temporal

`Temporal__Address` — default `localhost:7233`.

## MCP (host-level)

`McpRunner__Url` / `McpRunner__Token` — where the API and worker start owner-installed npm/PyPI connectors (`ws://mcp-runner:8090/run` in production; token at least 32 characters, `MCP_RUNNER_TOKEN`). Unset runs them in-process.

Runner side: `McpRunner__ListenUrl`, `McpRunner__MaxProcesses` (default 16), `McpRunner__MaxSessionMinutes` (default 120), `McpRunner__WorkRoot`, `McpRunner__CacheRoot`, and `McpRunner__PassEnvironment` (comma-separated variables copied from the runner's own environment into connectors, for an egress proxy or CA bundle).

`Mcp__Registry__BaseUrl` — MCP registry for the in-app catalog (default `https://registry.modelcontextprotocol.io`; empty turns the catalog off).

`Mcp__Servers__0__*` — Name, Transport (`stdio` | `streamableHttp`), Command/Endpoint, AllowedTools, AutoApprovedTools, CredentialProvider, CredentialEnvironmentVariables, CredentialHeaders.

## Coding

`Coding__Repositories__*`, `Coding__WorktreeRoot`, `Coding__TimeoutSeconds`. Per repository, optional `SelfFix`, `GitHubRepository`, `BaseBranch`, and `RemoteUrl` enable pull requests (see [agent-tools.md](../backend/agent-tools.md#self-fix-jarvis-proposes-changes-to-its-own-code)); `Coding__GitHubApiBaseUrl` overrides the API host (GitHub Enterprise or tests).

## Voice / LiveKit

`LiveKit__*`, `Voice__WorkerSecret`, env `LIVEKIT_PUBLIC_URL`, `LIVEKIT_BIND_ADDRESS`.

## Channels

`Channels__PublicBaseUrl`, `Channels__Signal__BaseUrl`, `Channels__WhatsApp__GraphBaseUrl`, `Channels__WhatsAppBridge__BaseUrl`, `Channels__WhatsAppBridge__Token`.

## Push

`Push__FirebaseProjectId`, `Push__GoogleServiceAccountFile`.

## Antivirus

`Antivirus__Host`, `__Port`, `__TimeoutSeconds` — fail closed if unreachable.

## Telemetry

`OTEL_EXPORTER_OTLP_ENDPOINT` — Aspire dashboard or collector.

Development stays off. Production uses the backend project from `appsettings.Production.json` and the deployment default. The SDK also reads `SENTRY_ENVIRONMENT` and `SENTRY_RELEASE`.

| Key / env var | Purpose |
|---------------|---------|
| `SENTRY_DSN` | Backend project DSN, shared by the API, worker, migration command, and WhatsApp bridge. Set it to replace the production default |
| `SENTRY_ENVIRONMENT` | Defaults to `production` in the production deployment |
| `SENTRY_RELEASE` | Git SHA of the running image. The deploy workflow sets this to the same SHA whose symbols and source files were uploaded |
| `Sentry__TracesSampleRate` (`SENTRY_TRACES_SAMPLE_RATE`) | Trace sample rate. Default `1` in Development and `0.2` otherwise |
| `Sentry__ProfilesSampleRate` (`SENTRY_PROFILES_SAMPLE_RATE`) | Profiling sample rate. Default `0`. The profiler starts only when this is above zero |
| `Sentry__RecordAiContent` (`SENTRY_RECORD_AI_CONTENT`) | When `true`, agent spans include prompt and response text. Default `false` |

Issues are HTTP 5xx and unhandled exceptions. Warning and error logs are sent as Sentry logs. `/health` and `/alive` are not sampled. Authorization headers, cookies, and connection strings are removed before an event is sent.

Release builds of the mobile app report to the mobile Sentry project. Override that DSN with `--dart-define=JARVIS_SENTRY_DSN=...`. Debug builds stay off unless `JARVIS_SENTRY_ENABLE=true` is also set. The release name is the SemVer version and build number.

## Data protection

`DataProtection__KeysDirectory` — persistent key ring for credential encryption.

## Public URL

`Jarvis__PublicBaseUrl` or `Channels__PublicBaseUrl` — webhooks and agent card.

For narrative and deployment-specific variables, see root [README.md](../../README.md#configuration).
