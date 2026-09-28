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

# Tag the currently deployed image IDs before pulling. This preserves the complete
# previous release for rollback even when the deployment tag is reused (for example,
# "latest"). The prior rollback tags are removed after the next healthy deployment.
image_prefix="${required_images[0]%/api:*}"
previous_images=()
services=(jarvis-api jarvis-worker jarvis-voice-worker)
service_images=(api worker voice-worker)
for index in "${!services[@]}"; do
  service="${services[$index]}"
  image_name="${service_images[$index]}"
  container_id="$(docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" ps -q "${service}" | head -n1)"
  if [[ -z "${container_id}" ]]; then
    continue
  fi

  image_id="$(docker inspect --format '{{.Image}}' "${container_id}")"
  revision="$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "${image_id}" 2>/dev/null || true)"
  if [[ -z "${revision}" || "${revision}" == "<no value>" ]]; then
    revision="${image_id#sha256:}"
  fi
  rollback_image="${image_prefix}/${image_name}:rollback-${revision}"
  docker image tag "${image_id}" "${rollback_image}"
  previous_images+=("${rollback_image}")
  echo "Preserving ${service}'s previous image as ${rollback_image}."
done

echo "Pulling Jarvis images:"
printf '  %s\n' "${required_images[@]}"

docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" pull \
  jarvis-api jarvis-worker jarvis-voice-worker garage

docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" up \
  -d --no-build --remove-orphans

# Do not discard rollback images until the new API is healthy and both workers
# are running. A failed deployment leaves the previous images available.
api_container=""
api_healthy=false
for attempt in $(seq 1 48); do
  api_container="$(docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" ps -q jarvis-api | head -n1)"
  if [[ -n "${api_container}" ]]; then
    api_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${api_container}" 2>/dev/null || true)"
    if [[ "${api_health}" == "healthy" ]]; then
      api_healthy=true
      break
    fi
    if [[ "${api_health}" == "unhealthy" ]]; then
      echo "Jarvis API is unhealthy; retaining previous images." >&2
      docker logs --tail=100 "${api_container}" >&2 || true
      exit 1
    fi
  fi
  sleep 5
done

if [[ "${api_healthy}" != true ]]; then
  echo "Jarvis API did not become healthy within 4 minutes; retaining previous images." >&2
  if [[ -n "${api_container}" ]]; then docker logs --tail=100 "${api_container}" >&2 || true; fi
  exit 1
fi

for service in jarvis-worker jarvis-voice-worker; do
  container_id="$(docker compose --env-file "${ENV_FILE}" "${compose_files[@]}" ps -q "${service}" | head -n1)"
  state=""
  if [[ -n "${container_id}" ]]; then
    state="$(docker inspect --format '{{.State.Status}}' "${container_id}" 2>/dev/null || true)"
  fi
  if [[ "${state}" != "running" ]]; then
    echo "${service} is not running; retaining previous images." >&2
    exit 1
  fi
done

keep_images=("${required_images[@]}" "${previous_images[@]}")
mapfile -t jarvis_image_tags < <(docker image ls --format '{{.Repository}}:{{.Tag}}' | sort -u)
for image in "${jarvis_image_tags[@]}"; do
  repository="${image%:*}"
  case "${repository}" in
    "${image_prefix}/api"|"${image_prefix}/worker"|"${image_prefix}/voice-worker") ;;
    *) continue ;;
  esac

  keep=false
  for retained_image in "${keep_images[@]}"; do
    if [[ "${image}" == "${retained_image}" ]]; then
      keep=true
      break
    fi
  done
  if [[ "${keep}" == true ]]; then
    continue
  fi

  echo "Removing superseded Jarvis image ${image}."
  if ! docker image rm "${image}"; then
    echo "Could not remove ${image}; a remaining container may still reference it." >&2
  fi
done

echo "Jarvis deployment is healthy; current and previous release images are retained."
