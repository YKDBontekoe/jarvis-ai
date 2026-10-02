# Deployment and CI

## Verification and release gates

[`ci.yml`](../../.github/workflows/ci.yml) runs on PRs, pushes to `main`, merge queues, manual dispatch, and reusable release calls. **All checks passed** fails if any prerequisite fails, is cancelled, or is skipped. Require this exact status in the `main` ruleset; workflow code alone cannot prevent an administrator from merging.

| Check | Evidence and gate |
|---|---|
| Policy | SHA-pinned Actions, digest-pinned service images, job timeouts, architecture references, actionlint, shellcheck, hadolint, all supported Compose overlays |
| Release selection | Exactly one PR SemVer checkbox; explicit versions must advance the latest release |
| Backend | Release build, pending EF model changes, unit tests in UTC and Europe/Amsterdam, TRX rejects empty/skipped tests, at least 80% changed executable core line coverage |
| Database | Real pgvector integration tests and an upgrade from the previous reachable release migration, preserving owner data |
| Clients | Flutter locked dependencies, analyze, tests and coverage; native unsigned iOS build and IPA structure/Mach-O checks; bridge tests and dependency audit |
| Packaged services | API, worker and bridge OCI archives, runtime smoke tests, SBOM/provenance, fixable high/critical vulnerability gate |
| End to end | Disposable PostgreSQL, Temporal and S3; deterministic Codex/channel fixtures; streaming, tools, approvals, automation, production authentication and cross-owner HTTP/SignalR boundaries |
| Compatibility | Versioned OpenAPI and SignalR contracts; committed and newly recorded synthetic Temporal histories replayed with current workflows |
| Security | Git history secret scan, NuGet/npm dependency audits, C#/JavaScript CodeQL high/critical finding gate |
| Memory | Held-out retrieval evaluation with versioned quality, noise and latency thresholds |

The PR template and `.github/CODEOWNERS` make release selection and changes to gates/contracts visible in review. Enable required CODEOWNER review, dismissal of stale approvals, and protection of `main` and `v*` tags in repository settings. CodeQL scanning must be supported/enabled for the repository (private repositories require the appropriate GitHub security entitlement). These settings are external to this checkout.

## Release identity and production topology

[`create-release-tag.yml`](../../.github/workflows/create-release-tag.yml) runs only after successful `main` CI for the same SHA, rejects an obsolete branch head, derives the SemVer from the merged PR, and explicitly dispatches both release workflows. Both workflows call the entire CI gate again for the selected release. Manual backend releases accept `main` or a valid SemVer tag.

Backend releases promote **the tested OCI archives**, preserving manifest digests and attestations; they do not rebuild after testing. All three application images (`api`, `worker`, `whatsapp-bridge`) are published to GHCR. The release manifest records source SHA, OCI digest and archive checksum, and is retained for 90 days. Deployment consumes immutable references from this manifest and checks its source SHA.

Production uses `infra/compose/docker-compose.production.yml`: Caddy, API/worker/bridge, PostgreSQL, durable PostgreSQL-backed Temporal, Garage, local embeddings, ClamAV, LiveKit and private signal-cli. Third-party images are digest pinned. Only edge/media ports are public. Configuration: `.env.production.example` → `.env.production` with mode `0600`.

The server requires Docker/Compose, Python, Git, `flock`, and a Linux x64 runner labelled `jarvis-deploy`. Configure secret `DEPLOY_PATH` and optional variable `DEPLOY_COMPOSE_FILES`. Link all three GHCR packages to the repository for short-lived `GITHUB_TOKEN` access. The `production` environment can require deployment reviewers. No production credentials are passed into PR tests.

For an operator deployment, obtain references from a verified release manifest:

```sh
export GIT_SHA=<manifest-source-sha>
export RELEASE_MANIFEST=/private/path/release-manifest.json
python3 scripts/deploy/manifest-env.py "$RELEASE_MANIFEST" > /private/path/images.env
set -a
. /private/path/images.env
set +a
scripts/deploy/remote-up.sh
```

`remote-up.sh` serializes deployments with a host lock, retains the previous app images/configuration, creates a private PostgreSQL dump before migration, runs the advisory-locked migration command, starts services, checks dependency readiness, and requires a side-effect-free Temporal workflow to complete. `/alive` checks process liveness; `/health` requires PostgreSQL, object storage and recent workflow pollers. Successful deployment is recorded only after the workflow probe passes.

The migration command applies all missing migrations through the latest migration in the image. Migration timestamps can arrive out of order when branches merge; targeting the last pending migration could revert newer migrations already applied. Reapplying a migration cannot recover data removed by an earlier downgrade; restore that data from a database backup separately.

## Recovery

Private recovery state is stored under `.jarvis/deploy/`; database dumps under `.jarvis/backups/`. Both are excluded from Git, build contexts and CI uploads. Restrict access and arrange off-host backup/retention according to operational needs; a same-host dump alone cannot survive host loss. Failed deployment retains the previous release and does not automatically downgrade a database.

Nightly validation exercises the previous API against the upgraded schema, a stopped/restarted worker and duplicate delivery checks, database outage readiness, backup restoration with every public table fingerprint compared, full antivirus/file flow and an API latency budget. Review that compatibility evidence before reverting an application release:

```sh
ROLLBACK_SCHEMA_COMPATIBLE=true scripts/deploy/rollback.sh
```

Rollback restores the previous rendered Compose configuration and env file while preserving the migrated schema. Verify authenticated reads and task execution afterward. Incompatible migrations require a planned recovery procedure, not an automatic schema downgrade. Database restoration is tested only in a separate disposable database in CI.

## Extended and model validation

[`nightly.yml`](../../.github/workflows/nightly.yml) runs daily at 02:17 UTC and on demand, reuses CI, then runs fault/recovery and previous-release compatibility checks. Set repository variable `JARVIS_REAL_MODEL_EVALS=true`, an explicit `JARVIS_EVAL_MODEL`, and environment `model-evaluations` secret `JARVIS_EVAL_CODEX_AUTH_JSON` to enable real Codex behavior evaluations. Use a dedicated evaluation account; the credential is mounted privately into disposable services, removed on exit, and excluded from reports. External provider credentials, APNs/Firebase delivery and live production configuration still require operator verification.

## Sentry and iOS

Optional Sentry secrets: `SENTRY_AUTH_TOKEN`, `SENTRY_ORG`, `SENTRY_PROJECT_BACKEND`, `SENTRY_PROJECT_MOBILE`, `JARVIS_SENTRY_DSN`. Backend symbol upload extracts portable PDBs with embedded source from the verified images, then associates `github.sha` with commits. This keeps release images identical to the tested images. The server env file still requires `SENTRY_DSN` for event reporting.

The iOS release builds with the pinned Flutter version on `macos-15`, validates the IPA before publishing, uploads optional Dart symbol/source context, and publishes the unsigned IPA and AltStore feed. Production Dart defines are applied during release; PR builds validate the native build without exposing release secrets. See the [README](../../README.md#ios-ipa-unsigned-livecontainer) for distribution setup.

Sentry Logs and Issues are separate: automatic issue creation from logging is disabled (`MinimumEventLevel = None`). The shared agent run coordinator explicitly captures unexpected run exceptions, including chat/channel failures, with conversation ID and run kind but no message content. Expected client errors and requested cancellation are excluded. Inspect the backend project and production environment for these issues.
