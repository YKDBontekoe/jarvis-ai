# Testing

## Unit tests (.NET)

```sh
dotnet test --project tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
```

Covers domain rules, agents, endpoints helpers, compaction, MCP redaction, and related logic.

The .NET suites use xunit.v3 on Microsoft.Testing.Platform (opted in through `global.json`), so pass the project with
`--project`; the old positional path form is rejected. xUnit1051 (use `TestContext.Current.CancellationToken`) is
advisory in `tests/.editorconfig` until the existing call sites are migrated; new tests should follow it.

## Python unit tests (release/altstore, generated production Compose)

```sh
scripts/ci/run-python-unit-tests.sh
python3 -m unittest tests/unit/compose/test_production_compose.py   # needs a .NET 10 SDK (or Docker) and docker compose
```

## Integration tests

Requires Docker (Testcontainers + pgvector image):

```sh
dotnet test --project tests/integration/Jarvis.IntegrationTests/Jarvis.IntegrationTests.csproj
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

Quickest path (also in cloud sessions): `scripts/dev/smoke.sh` starts the AppHost with `verification`, waits for
`/health`, and runs `local_fixture_flow.mjs` (`up`, `test [flow.mjs …]` and `down` run the steps separately; logs in
`artifacts/smoke/`). In Claude Code cloud sessions it skips the WhatsApp bridge image, whose `npm ci` cannot run inside
`docker build` behind the sandbox proxy.

With Aspire, turn on the `verification` feature to run the whole stack on the fake Codex app server and fake MCP
server, then run the scripts against `http://localhost:5082`:

```sh
JARVIS_FEATURES=verification dotnet run --project src/Jarvis.AppHost &
(cd tests/e2e && npm ci && node local_fixture_flow.mjs)
```

Without Aspire, run the API and worker directly on `fake_codex_app_server.mjs` instead of real Codex:

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

Component evals for memory (no deployment needed): `tests/eval/Jarvis.MemoryEval` scores retrieval against a scratch
PostgreSQL database, and `tests/eval/Jarvis.ExtractionEval` scores memory extraction against a real model (`--dry-run`
only validates its dataset). `tests/eval/Jarvis.FileEval` scores file search (chunking, keyword and hybrid retrieval)
against a scratch database.

## CI expectations

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on every pull request to `main`: backend unit and integration tests (the backend job also publishes the AppHost and checks the generated production Compose with `tests/unit/compose/test_production_compose.py`), Python release/AltStore tests, Flutter analyze and widget tests, and API/worker image builds (no push). Release tags start `release.yml`, which checks `deploy-backend.yml` and `release-ios.yml` before the single production approval.

## Agent guidance

When fixing bugs, prefer adding or extending **unit tests** in the project that owns the logic. Use e2e scripts when the failure spans HTTP + SignalR + Temporal.
