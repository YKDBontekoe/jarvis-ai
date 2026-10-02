#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
if [[ -z "${CODEX_AUTH_JSON:-}" || -z "${CI_CODEX_MODEL:-}" ]]; then
  echo 'Real-model evaluation requires a dedicated Codex account credential and an explicit model ID.' >&2
  exit 1
fi
mkdir -p artifacts/ci artifacts/verification
umask 077
printf '%s' "$CODEX_AUTH_JSON" > artifacts/verification/codex-auth.private.json
unset CODEX_AUTH_JSON
compose=(docker compose -p jarvis-ci -f tests/e2e/docker-compose.ci.yml -f tests/e2e/docker-compose.models.yml)
cleanup() {
  local result=$?
  "${compose[@]}" logs --no-color > artifacts/ci/services.log 2>&1 || true
  python3 scripts/ci/redact-logs.py artifacts/ci/services.log
  "${compose[@]}" --profile full down --volumes --remove-orphans || true
  rm -f artifacts/verification/codex-auth.private.json artifacts/verification/access-token.txt artifacts/verification/identity-fixture.json
  return "$result"
}
trap cleanup EXIT
"${compose[@]}" up -d --wait --wait-timeout 180 postgres temporal storage fake-channels
storage_ready=false
for _ in $(seq 1 30); do
  if printf 's3.bucket.create -name jarvis-files\n' | "${compose[@]}" exec -T storage weed shell -master=storage:9333; then
    storage_ready=true
    break
  fi
  sleep 2
done
[[ "$storage_ready" == true ]] || { echo 'Object storage did not become ready.' >&2; exit 1; }
"${compose[@]}" run --rm --no-deps jarvis-api Jarvis.Api.dll migrate
"${compose[@]}" --profile full up -d --wait --wait-timeout 600 clamav jarvis-worker jarvis-api
npm ci --prefix tests/e2e
python3 tests/e2e/create_identity_fixture.py
python3 tests/e2e/authentication_flow.py
export JARVIS_E2E_ACCESS_TOKEN
JARVIS_E2E_ACCESS_TOKEN="$(cat artifacts/verification/access-token.txt)"
node tests/e2e/model_catalog.mjs
node tests/e2e/behavior_evaluations.mjs
cp artifacts/verification/model-catalog.json artifacts/ci/
cp artifacts/verification/behavior-evaluations.json artifacts/ci/
