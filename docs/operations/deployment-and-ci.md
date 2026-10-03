# Deployment and CI

## Production topology

Defined in the AppHost: `src/Jarvis.AppHost/ProductionDeployment.cs`. There is no hand-written Compose file.
`scripts/deploy/publish-compose.sh` publishes the AppHost (`dotnet run --project src/Jarvis.AppHost -- --operation
publish --step publish`) into `artifacts/compose/docker-compose.yaml`, using a local .NET 10 SDK or the pinned SDK
container. `scripts/deploy/remote-up.sh` does that first on every deploy, then runs Docker Compose against the
generated file with the repository root as the project directory and project name `jarvis` (so existing
`jarvis_*` volumes are reused). Settings Aspire's Compose model lacks (a PID limit, `platform`) are added by the
AppHost right after publishing (`ComposeOutput.cs`).

Optional parts are AppHost features, chosen with `JARVIS_FEATURES` (repository variable for deploys): `browser`,
`github`, `home-assistant`, `coding`, `tunnel`. `DEPLOY_COMPOSE_FILES` from older setups maps its overlay names to
the same features (`scripts/deploy/compose-env.sh`).

- **MCP runner** for owner-installed connectors, and the nightly **backup** service

- **Caddy** edge TLS
- **Jarvis API + Worker** (GHCR images in CI)
- **PostgreSQL** (app + Temporal DB)
- **Garage** S3-compatible storage
- **embeddings**: a CPU Text Embeddings Inference container with the local embedding model (see [configuration.md](configuration.md#local-embedding-model))
- **ClamAV**, **LiveKit**, private **signal-cli** and **whatsapp-bridge** (built on the host from `workers/whatsapp-bridge`)
- Only edge/media ports published; databases stay on private networks

Env template: `infra/compose/.env.production.example` → `.env.production` (mode `0600`).

Bootstrap and deploy scripts: `scripts/deploy/remote-up.sh` (generate, pull, migrate, replace, health-check), `scripts/deploy/publish-compose.sh`, `scripts/deploy/compose-env.sh`.

The migration command applies all missing migrations through the latest migration in
the image. Migration timestamps can arrive out of order when feature branches merge;
the last pending migration must never be used as a target because EF would revert
newer migrations that were already applied. Reapplying a migration recreates tables,
but does not restore data removed by an earlier downgrade; recover that data from a
database backup separately.

## Backups

The `backup` service takes a nightly `pg_dump` of both databases plus the data protection key ring into
`JARVIS_BACKUP_DIR`. `scripts/backup/restore.sh` restores one. See [backup-and-restore.md](backup-and-restore.md).

## SemVer releases

- Tags `vMAJOR.MINOR.PATCH` on merge to `main` via [create-release-tag.yml](../../.github/workflows/create-release-tag.yml) and PR template checkboxes.
- Validate: `python3 scripts/release/semver.py --validate 1.2.3`
- Align `apps/mobile/pubspec.yaml` marketing version before release.

## Workflows

| Workflow | Purpose |
|----------|---------|
| `ci.yml` | PRs to `main`: .NET build/tests, Python checks, Flutter analyze/tests, container image builds |
| `create-release-tag.yml` | Tag on merge, then start `release.yml` |
| `release.yml` | Wait for backend/web and iOS builds, then the single production approval deploys both |
| `release-ios.yml` | Unsigned IPA + AltStore source. Tag releases build only; publishing waits for `release.yml` |
| `deploy-backend.yml` | Build Flutter web, publish API (including web) and worker to GHCR. Tag releases build only; deploying waits for `release.yml` |

Require the **All checks passed** job from `ci.yml` in branch protection before merging to `main`.

Repository secret `DEPLOY_PATH`; repository variable `JARVIS_FEATURES` for optional parts such as `tunnel` (the older `DEPLOY_COMPOSE_FILES` still maps to features).

Sentry release secrets, all optional: `SENTRY_AUTH_TOKEN`, `SENTRY_ORG`, `SENTRY_PROJECT_BACKEND`, `SENTRY_PROJECT_MOBILE`, and `JARVIS_SENTRY_DSN`. When the token, org, and backend project are set, the API and worker image build uploads portable PDBs and source files for `github.sha`, and a following job associates that release with the git commits. The iOS workflow uploads the Dart symbol map, split debug info, and source context for `version+build`. Image builds stay green when those secrets are absent. The server `.env.production` still needs `SENTRY_DSN` before a running container reports anything. Link the GitHub repository in the Sentry project so stack traces open on the uploaded source.

Sentry **Logs** and **Issues** are separate: error logs are uploaded as logs, while
automatic issue creation from logging is disabled (`MinimumEventLevel = None`).
The shared agent run coordinator explicitly captures unexpected run exceptions as
issues, including failures that chat/channel handlers turn into user-facing error
messages. Captured events include the conversation ID and run kind, without message
content. Expected client errors and requested cancellation remain excluded. To inspect
chat failures, select the backend project and `production` environment; a mobile
project filter will not include these backend exceptions.

## GHCR images

```sh
export JARVIS_API_IMAGE=ghcr.io/<owner>/jarvis-ai/api:<sha>
export JARVIS_WORKER_IMAGE=ghcr.io/<owner>/jarvis-ai/worker:<sha>
scripts/deploy/remote-up.sh
```

## iOS distribution

Unsigned IPA for LiveContainer; AltStore feed published from self-hosted path (see root README).

## Hosted web client

The production API serves the Flutter web release at the root of the existing
Jarvis HTTPS domain (`https://jarvis.ykdbonte.dev/` on the owner's server). It uses
the same account, database and authenticated SignalR hub as the native client.
The existing Caddy/Cloudflare route to the API also serves web assets; the separate
`/altstore/*` host route continues to serve the IPA source and downloads.

Release tags start [release.yml](../../.github/workflows/release.yml). That
workflow builds the web client inside the backend image and builds the iOS IPA,
both without publishing. The `production` environment approval on **Approve
release** is the only review for that tag; approving it deploys the API (including
web) and publishes the AltStore source. Running **Deploy Jarvis backend and web**
or **Release iOS IPA** manually still uses `production` for that pipeline alone.
The web artifact is copied into `wwwroot` in the API image; production API builds
require that artifact. After API health passes, the deployment script
checks that the homepage and compiled JavaScript bundle are available before it
prunes older images. Rolling back the API image also rolls back the web client.

No new secrets or DNS entries are required. Browsers default to their current
origin for both HTTP and SignalR. An optional repository variable
`JARVIS_WEB_API_URL` overrides that for separate hosting; when using a different
origin, configure the API's `Cors:AllowedOrigins` as well. Native builds retain
their existing `JARVIS_API_URL` configuration.

The web build uses Flutter 3.44.4, packages rendering resources locally, disables
the generated service worker, and serves assets with `Cache-Control: no-cache`
so a refreshed browser picks up new releases. It does not cache private API data
for offline access. Existing browser microphone and file-picker support is reused;
native push notification registration and device biometric unlock are not provided
by this web release.

Source maps are uploaded privately to the existing frontend Sentry project when
the Sentry secrets are configured. Web events use `jarvis-web@<git SHA>` as the
release. Maps are stripped from the public artifact before it enters the API image.

For a local web distribution:

```sh
bash scripts/ci/build-web.sh
bash scripts/ci/stage-web.sh
```

To serve this build from a local API, copy `infra/web/dist/` into
`src/Jarvis.Api/wwwroot/` before starting the API. To run Flutter's own development
web server against a separate local API, continue passing
`--dart-define=JARVIS_API_URL=http://localhost:5082` explicitly.

## Production checklist (high level)

1. Set `Authentication:*` and `DataProtection:KeysDirectory`
2. `CODEX_HOME` with `codex login` on deploy user
3. Firebase/APNs for push (optional)
4. Pin container image digests
5. Disable `Authentication:AllowRegistration` after bootstrap
6. Never expose a local Aspire (Development) instance publicly

Detailed operator steps remain in [README.md](../../README.md#production-deployment).
