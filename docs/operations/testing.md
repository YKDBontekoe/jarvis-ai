# Testing

## Unit tests (.NET)

```sh
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
```

Covers domain rules, agents, endpoints helpers, compaction, MCP redaction, and related logic.

## Python unit tests (release/altstore, generated production Compose)

```sh
scripts/ci/run-python-unit-tests.sh
python3 -m unittest tests/unit/compose/test_production_compose.py   # needs a .NET 10 SDK (or Docker) and docker compose
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
only validates its dataset).

## CI expectations

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on every pull request to `main`: backend unit and integration tests (the backend job also publishes the AppHost and checks the generated production Compose with `tests/unit/compose/test_production_compose.py`), Python release/AltStore tests, Flutter analyze and widget tests, and API/worker image builds (no push). Release tags start `release.yml`, which checks `deploy-backend.yml` and `release-ios.yml` before the single production approval.

## Agent guidance

When fixing bugs, prefer adding or extending **unit tests** in the project that owns the logic. Use e2e scripts when the failure spans HTTP + SignalR + Temporal.
