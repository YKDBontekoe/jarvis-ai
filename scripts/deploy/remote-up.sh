#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}"

ENV_FILE="${ENV_FILE:-infra/compose/.env.production}"
if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Production env file not found: ${ENV_FILE}" >&2
  exit 1
fi

compose_files=(-f infra/compose/docker-compose.production.yml)
if [[ -n "${DEPLOY_COMPOSE_FILES:-}" ]]; then
  # shellcheck disable=SC2206
  extra=(${DEPLOY_COMPOSE_FILES})
  for file in "${extra[@]}"; do
    compose_files+=(-f "${file}")
  done
fi

required_images=(
  "${JARVIS_API_IMAGE:?Set JARVIS_API_IMAGE to the GHCR api image}"
  "${JARVIS_WORKER_IMAGE:?Set JARVIS_WORKER_IMAGE to the GHCR worker image}"
  "${JARVIS_VOICE_WORKER_IMAGE:?Set JARVIS_VOICE_WORKER_IMAGE to the GHCR voice-worker image}"
)

echo "Pulling Jarvis images:"
printf '  %s\n' "${required_images[@]}"

docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" pull \
  jarvis-api jarvis-worker jarvis-voice-worker

docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" up \
  -d --no-build --remove-orphans
