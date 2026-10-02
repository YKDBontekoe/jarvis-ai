# Configuration reference

Primary file: `src/Jarvis.Api/appsettings.json`. Override with environment variables using `__` nesting (ASP.NET Core convention).

## Database and storage

| Key | Purpose |
|-----|---------|
| `ConnectionStrings__jarvis` | PostgreSQL |
| `ObjectStorage__*` | S3 endpoint, keys, bucket, max upload |
| `Database:ApplyMigrationsAtStartup` | Migrate on boot (non-dev optional) |

## Codex and models

| Key | Purpose |
|-----|---------|
| `Codex__ExecutablePath` | CLI binary (default `codex`) |
| `Codex__ManagedInstallDirectory` | Settings-driven CLI updates |
| `Codex__EnableWebSearch` | Codex hosted live web search for chat turns |
| `Codex__Model`, `Jarvis__ModelClass` | Default model selection |
| `Codex__ModelClasses__*` | fast/standard/reasoning/coding/vision/realtime |
| `Codex__TurnTimeoutSeconds` | 30–1800 |

Owner overrides via API `/settings/models` (OpenRouter key encrypted).

### Local embedding model

Semantic memory search needs an embedding model. The Compose files include an `embeddings` service
(Hugging Face Text Embeddings Inference, CPU) that downloads the model on first start into the `embeddings-data`
volume and serves an OpenAI-compatible `/v1/embeddings`. API and worker use it through these keys, so no key or account is needed:

| Key / env var | Purpose |
|---------------|---------|
| `Embeddings__BaseUrl` (`EMBEDDINGS_BASE_URL`) | Endpoint; defaults to `http://embeddings:80/v1`. Set it empty to switch the local model off, or point it at any OpenAI-compatible server |
| `Embeddings__Model` (`EMBEDDINGS_MODEL`) | Model id; defaults to `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` (multilingual, 384 dimensions, zero-padded to the 1536-wide column) |
| `Embeddings__ApiKey` | Optional bearer key for an external server |
| `EMBEDDINGS_IMAGE` | Compose image, default `ghcr.io/huggingface/text-embeddings-inference:cpu-1.8` |

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
| `Cors:AllowedOrigins` | Flutter web origins |

## Temporal

`Temporal__Address` — default `localhost:7233`.

## MCP (host-level)

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

Sentry is off until `SENTRY_DSN` is set. The SDK also reads `SENTRY_ENVIRONMENT` and `SENTRY_RELEASE`. Compose maps the extra keys below.

| Key / env var | Purpose |
|---------------|---------|
| `SENTRY_DSN` | Backend project DSN, shared by the API, worker, migration command, and WhatsApp bridge. Empty disables Sentry |
| `SENTRY_ENVIRONMENT` | Defaults to `production` in Compose |
| `SENTRY_RELEASE` | Git SHA of the running image. The deploy workflow sets this to the same SHA whose symbols and source files were uploaded |
| `Sentry__TracesSampleRate` (`SENTRY_TRACES_SAMPLE_RATE`) | Trace sample rate. Default `1` in Development and `0.2` otherwise |
| `Sentry__ProfilesSampleRate` (`SENTRY_PROFILES_SAMPLE_RATE`) | Profiling sample rate. Default `0`. The profiler starts only when this is above zero |
| `Sentry__RecordAiContent` (`SENTRY_RECORD_AI_CONTENT`) | When `true`, agent spans include prompt and response text. Default `false` |

Issues are HTTP 5xx and unhandled exceptions. Warning and error logs are sent as Sentry logs. `/health` and `/alive` are not sampled. Authorization headers, cookies, and connection strings are removed before an event is sent.

The mobile app reads `JARVIS_SENTRY_DSN` from `--dart-define`. Its release name is the SemVer version and build number.

## Data protection

`DataProtection__KeysDirectory` — persistent key ring for credential encryption.

## Public URL

`Jarvis__PublicBaseUrl` or `Channels__PublicBaseUrl` — webhooks and agent card.

For narrative and Compose-specific variables, see root [README.md](../../README.md#configuration).
