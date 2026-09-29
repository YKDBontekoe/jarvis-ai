# Deployment and CI

## Production topology

File: `infra/compose/docker-compose.production.yml`

- **Caddy** edge TLS
- **Jarvis API + Worker** (GHCR images in CI)
- **PostgreSQL** (app + Temporal DB)
- **Garage** S3-compatible storage
- **ClamAV**, **LiveKit**, private **signal-cli**
- Only edge/media ports published; databases stay on private networks

Env template: `infra/compose/.env.production.example` → `.env.production` (mode `0600`).

Bootstrap and deploy scripts: `scripts/deploy/remote-up.sh`.

## SemVer releases

- Tags `vMAJOR.MINOR.PATCH` on merge to `main` via [create-release-tag.yml](../../.github/workflows/create-release-tag.yml) and PR template checkboxes.
- Validate: `python3 scripts/release/semver.py --validate 1.2.3`
- Align `apps/mobile/pubspec.yaml` marketing version before release.

## Workflows

| Workflow | Purpose |
|----------|---------|
| `create-release-tag.yml` | Tag on merge |
| `release-ios.yml` | Unsigned IPA + AltStore source |
| `deploy-backend.yml` | Build/push `api` and `worker` to GHCR; deploy via self-hosted runner |

Repository secrets: `DEPLOY_PATH`, optional `DEPLOY_COMPOSE_FILES` for tunnel/proxy overlays.

## GHCR images

```sh
export JARVIS_API_IMAGE=ghcr.io/<owner>/jarvis-ai/api:<sha>
export JARVIS_WORKER_IMAGE=ghcr.io/<owner>/jarvis-ai/worker:<sha>
scripts/deploy/remote-up.sh
```

## iOS distribution

Unsigned IPA for LiveContainer; AltStore feed published from self-hosted path (see root README).

## Production checklist (high level)

1. Set `Authentication:*` and `DataProtection:KeysDirectory`
2. `CODEX_HOME` with `codex login` on deploy user
3. Firebase/APNs for push (optional)
4. Pin container image digests
5. Disable `Authentication:AllowRegistration` after bootstrap
6. Never expose development Compose profile publicly

Detailed operator steps remain in [README.md](../../README.md#production-compose).
