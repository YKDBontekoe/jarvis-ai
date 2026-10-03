# Local development

## Prerequisites

- **.NET SDK** 10.x (see `global.json` / `.cursor/install.sh` pin `10.0.302`)
- **Codex CLI** installed; sign it in from the app (Home → Sign Jarvis in to ChatGPT) or with `codex login`
- **Docker** (or Podman) — Aspire container dependencies and Testcontainers
- **Flutter** — for mobile/web client work

Cloud agents: run `bash .cursor/install.sh` from repo root; optional `dockerd` terminal per `.cursor/environment.json`.

## Environment file (optional)

The AppHost has development defaults for everything, so `dotnet run --project src/Jarvis.AppHost` works without
one. To override a default (LAN listen URL, features), `cp .env.example infra/compose/.env`, edit it, and source it.
If the local Codex CLI is not signed in, the app's Home screen offers **Sign Jarvis in to ChatGPT**.

## Aspire (recommended)

From repo root:

```sh
dotnet run --project src/Jarvis.AppHost
```

Starts the API (default `http://localhost:5082`), worker, MCP runner, PostgreSQL (pgvector), Temporal (7233/8233), SeaweedFS, ClamAV, LiveKit, optional signal-cli and the WhatsApp bridge. Migrations apply automatically in Development.

Coding repo allowlist: AppHost sets `Coding__Repositories__0__Path` to the git root.

Optional parts use the same names as production: `JARVIS_FEATURES="github home-assistant browser verification"` (see the root [README.md](../../README.md#run-locally)). `verification` swaps in the fake Codex app server and fake MCP server for the e2e scripts.

There are no hand-written Compose files. The AppHost (`src/Jarvis.AppHost`) is the only orchestration; production Compose is generated from it (`scripts/deploy/publish-compose.sh`, see [deployment-and-ci.md](deployment-and-ci.md)). To see the production stack locally: `scripts/deploy/publish-compose.sh` and open `artifacts/compose/docker-compose.yaml`.

## API and worker only

For scripted e2e without full Aspire, see [testing.md](testing.md) (`fake_codex_app_server.mjs`).

## Flutter

```sh
cd apps/mobile
flutter run --dart-define=JARVIS_API_URL=http://localhost:5082
```

Web profile build served on `http://localhost:5137` is an allowed dev CORS origin.

## Identity bypass

Development API accepts unauthenticated calls with a fixed owner id for scripts; production requires JWT. Flutter should use register/login endpoints.

## Ports (defaults)

| Service | Port |
|---------|------|
| Jarvis API | 5082 |
| Temporal UI | 8233 |
| Temporal gRPC | 7233 |
| LiveKit WS | 7880 |
| Signal CLI REST (dev) | 8080 (loopback) |

Bind overrides: `JARVIS_LISTEN_URL`, `LIVEKIT_PUBLIC_URL` — see [configuration.md](configuration.md).
