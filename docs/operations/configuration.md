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
| `Codex__EnableWebSearch` | Native standalone web search |
| `Codex__Model`, `Jarvis__ModelClass` | Default model selection |
| `Codex__ModelClasses__*` | fast/standard/reasoning/coding/vision/realtime |
| `Codex__TurnTimeoutSeconds` | 30–1800 |

Owner overrides via API `/settings/models` (OpenRouter key encrypted).

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

`Coding__Repositories__*`, `Coding__WorktreeRoot`, `Coding__TimeoutSeconds`.

## Voice / LiveKit

`LiveKit__*`, `Voice__WorkerSecret`, env `LIVEKIT_PUBLIC_URL`, `LIVEKIT_BIND_ADDRESS`.

## Channels

`Channels__PublicBaseUrl`, `Channels__Signal__BaseUrl`, `Channels__WhatsApp__GraphBaseUrl`.

## Push

`Push__FirebaseProjectId`, `Push__GoogleServiceAccountFile`.

## Antivirus

`Antivirus__Host`, `__Port`, `__TimeoutSeconds` — fail closed if unreachable.

## Telemetry

`OTEL_EXPORTER_OTLP_ENDPOINT` — Aspire dashboard or collector.

## Data protection

`DataProtection__KeysDirectory` — persistent key ring for credential encryption.

## Public URL

`Jarvis__PublicBaseUrl` or `Channels__PublicBaseUrl` — webhooks and agent card.

For narrative and Compose-specific variables, see root [README.md](../../README.md#configuration).
