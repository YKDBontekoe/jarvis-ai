# Local development

## Prerequisites

- **.NET SDK** 10.x (see `global.json` / `.cursor/install.sh` pin `10.0.302`)
- **Codex CLI** signed in (`codex login status`) for real model calls
- **Docker** — Aspire dependencies, Testcontainers, optional Compose
- **Flutter** — for mobile/web client work

Cloud agents: run `bash .cursor/install.sh` from repo root; optional `dockerd` terminal per `.cursor/environment.json`.

## Environment file

```sh
cp .env.example infra/compose/.env
```

Edit secrets for Postgres and S3-compatible storage before Compose or sourcing for Aspire.

## Aspire (recommended)

From repo root after sourcing env:

```sh
set -a && source infra/compose/.env && set +a
dotnet run --project src/Jarvis.AppHost
```

Starts API (default `http://localhost:5082`), PostgreSQL (pgvector), Temporal (7233/8233), SeaweedFS, ClamAV, LiveKit, optional signal-cli. Migrations apply automatically in Development.

Coding repo allowlist: AppHost sets `Coding__Repositories__0__Path` to the git root.

## Docker Compose (alternative)

Development profile:

```sh
docker compose --profile development --env-file infra/compose/.env \
  -f infra/compose/docker-compose.yml up -d
```

Requires `CODEX_AUTH_FILE` pointing at host `auth.json`. Overlays: browser, GitHub, Home Assistant, coding — see root [README.md](../../README.md).

**Do not run Aspire and Compose on the same ports simultaneously.**

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

Bind overrides: `JARVIS_LISTEN_URL`, `LIVEKIT_PUBLIC_URL`, `JARVIS_BIND_ADDRESS` — see [configuration.md](configuration.md).
