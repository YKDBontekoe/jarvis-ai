# Testing

## Unit tests (.NET)

```sh
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
```

Covers domain rules, agents, endpoints helpers, compaction, MCP redaction, and related logic.

## Python unit tests (release/compose/altstore)

```sh
scripts/ci/run-python-unit-tests.sh
```

## Integration tests

Requires Docker (Testcontainers + pgvector image):

```sh
dotnet test tests/integration/Jarvis.IntegrationTests/Jarvis.IntegrationTests.csproj
```

## Flutter

```sh
cd apps/mobile && flutter test
```

## End-to-end (Node)

Fixtures under `tests/e2e/`:

| Script | Covers |
|--------|--------|
| `local_fixture_flow.mjs` | Streaming, tools, approvals, memory, reminders, Temporal task |
| `channels_flow.mjs` | WhatsApp Cloud/Signal with `fake_channels.mjs` (QR linking is covered by unit and widget tests) |
| `platform_flow.mjs` | A2A, devices, voice options; `JARVIS_PLATFORM_CHAT=1` for `RenderUi` |

Uses `fake_codex_app_server.mjs` instead of real Codex:

```sh
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ConnectionStrings__jarvis="Host=localhost;Database=jarvis;Username=jarvis;Password=jarvis"
export Codex__ExecutablePath="$PWD/tests/e2e/fake_codex_app_server.mjs" Codex__EnableWebSearch=false
dotnet run --project src/Jarvis.Api --no-launch-profile -- --urls http://localhost:5082 &
dotnet run --project workers/Jarvis.Worker --no-launch-profile &
(cd tests/e2e && npm ci && node local_fixture_flow.mjs)
```

## Behavioral evals

Model-agnostic scenarios: `evals/jarvis-core-v1.jsonl` — see [evals/README.md](../../evals/README.md). Run against a disposable deployment through the public API.

## CI expectations

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on PRs, main pushes, merge queues and release calls. The required aggregate gate includes every job. Full coverage, compatibility, packaged runtime, security and recovery checks are described below and in [deployment-and-ci.md](deployment-and-ci.md).

## Agent guidance

When fixing bugs, prefer adding or extending **unit tests** in the project that owns the logic. Use e2e scripts when the failure spans HTTP + SignalR + Temporal.

## Reproducing the expanded gates

Install the Python gate dependencies in a virtualenv (`scripts/ci/requirements.txt`) and provide `shellcheck`, Docker, .NET from `global.json`, Node 22 and Flutter 3.44.4. Run `scripts/ci/check-policy.py`, `scripts/ci/lint.sh`, and `scripts/ci/run-python-unit-tests.sh` for configuration and gate regression checks.

Packaged E2E uses a dedicated Compose project and disposable volumes. Build or load `jarvis-ci-api:test`, `jarvis-ci-worker:test`, and `jarvis-ci-whatsapp-bridge:test`, then run:

```sh
scripts/ci/run-e2e.sh
# Full antivirus/file, fault, restore, and performance validation:
CI_EXTENDED=true scripts/ci/run-e2e.sh
```

If another stack occupies port 5082, set `CI_API_PORT=15082`. The fixture database uses localhost port 55432, and fake channels port 5198. Do not run two fixture suites concurrently. Cleanup removes only the `jarvis-ci` project and its volumes. Reports and redacted logs are written to `artifacts/ci/`; temporary credentials are removed and never uploaded.

Temporal histories under `tests/integration/Jarvis.IntegrationTests/Histories/` are synthetic compatibility fixtures. Preserve old histories when workflow logic changes; a replay failure requires a Temporal-compatible workflow change/versioning strategy. `JARVIS_REPLAY_DIRECTORY` selects newly captured E2E histories. Replay is separate from database integration tests in CI so neither suite can silently omit the other.

OpenAPI and SignalR baselines live in `tests/contracts/`. The HTTP gate reads the target branch baseline, so changing it in a PR cannot conceal an incompatible API change. Review intentional breaking changes with a migration/client plan and appropriate SemVer bump. Retrieval thresholds live in `scripts/ci/memory-thresholds.json` and apply to held-out fixtures.

See [deployment-and-ci.md](deployment-and-ci.md) for the complete gate matrix, nightly real-model configuration, GitHub ruleset setup and recovery procedure.
