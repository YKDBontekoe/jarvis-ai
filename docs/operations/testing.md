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
| `channels_flow.mjs` | WhatsApp/Signal with `fake_channels.mjs` |
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

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on every pull request to `main`: backend unit and integration tests, Python release/compose/AltStore tests, Flutter analyze and widget tests, and API/worker image builds (no push). Release tags still trigger `release-ios.yml` and `deploy-backend.yml`.

## Agent guidance

When fixing bugs, prefer adding or extending **unit tests** in the project that owns the logic. Use e2e scripts when the failure spans HTTP + SignalR + Temporal.
