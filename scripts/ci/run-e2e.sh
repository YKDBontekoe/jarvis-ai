#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
mkdir -p artifacts/ci artifacts/verification
export JARVIS_API_URL="http://localhost:${CI_API_PORT:-5082}"
export JARVIS_BASE_URL="$JARVIS_API_URL" JARVIS_ORIGIN="$JARVIS_API_URL"
export JARVIS_API="$JARVIS_API_URL/api/v1"
compose=(docker compose -p jarvis-ci -f tests/e2e/docker-compose.ci.yml)
cleanup() {
  local result=$?
  # Reports are uploaded separately; credentials and token fixtures are never artifacts.
  "${compose[@]}" logs --no-color > artifacts/ci/services.log 2>&1 || true
  python3 scripts/ci/redact-logs.py artifacts/ci/services.log
  "${compose[@]}" --profile full down --volumes --remove-orphans || true
  rm -f artifacts/verification/access-token.txt artifacts/verification/identity-fixture.json
  return "${result}"
}
trap cleanup EXIT
"${compose[@]}" up -d --wait --wait-timeout 180 postgres temporal storage fake-channels
printf 's3.bucket.create -name jarvis-files\n' | "${compose[@]}" exec -T storage weed shell -master=storage:9333
"${compose[@]}" run --rm --no-deps jarvis-api Jarvis.Api.dll migrate
"${compose[@]}" up -d --wait --wait-timeout 180 jarvis-worker jarvis-api
npm ci --prefix tests/e2e
node tests/e2e/local_fixture_flow.mjs
node tests/e2e/channels_flow.mjs
JARVIS_PLATFORM_CHAT=1 node tests/e2e/platform_flow.mjs
node tests/e2e/automations_flow.mjs
curl --fail --silent "$JARVIS_API_URL/openapi/v1.json" > artifacts/ci/openapi.json
python3 scripts/ci/check-contracts.py tests/contracts/openapi.json artifacts/ci/openapi.json
# Production startup requires explicit migrations; do not turn on development-only startup migrations.
CI_ENVIRONMENT=Production "${compose[@]}" up -d --wait --wait-timeout 180 jarvis-api
python3 tests/e2e/create_identity_fixture.py
python3 tests/e2e/authentication_flow.py
node tests/e2e/security_flow.mjs
python3 tests/e2e/voice_readiness.py
"${compose[@]}" exec -T jarvis-api dotnet Jarvis.Api.dll deployment-probe
python3 scripts/ci/capture-histories.py
MEMORY_EVAL_DB="Host=localhost;Port=55432;Database=jarvis;Username=jarvis;Password=jarvis-ci-fixture" \
  dotnet run --project tests/eval/Jarvis.MemoryEval --configuration Release -- ci --held-out \
    --thresholds scripts/ci/memory-thresholds.json --report artifacts/ci/memory-eval.json
if [[ "${CI_EXTENDED:-false}" == true ]]; then
  "${compose[@]}" --profile full up -d --wait --wait-timeout 600 clamav
  export JARVIS_E2E_ACCESS_TOKEN
  JARVIS_E2E_ACCESS_TOKEN="$(cat artifacts/verification/access-token.txt)"
  python3 tests/e2e/deployed_flow.py --url "$JARVIS_API_URL" --report artifacts/ci/deployed-flow.json
  unset JARVIS_E2E_ACCESS_TOKEN
  if [[ -n "${CI_PREVIOUS_API_IMAGE:-}" ]]; then python3 scripts/ci/check-previous-app.py; fi
  python3 tests/e2e/resilience_flow.py
fi
cp artifacts/verification/authentication-flow.json artifacts/ci/
