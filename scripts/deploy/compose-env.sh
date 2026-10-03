#!/usr/bin/env bash
# Shared by remote-up.sh and the backup restore script: generates the production Compose file from the Aspire
# AppHost and runs docker compose against it. Source it from the repository root after setting ENV_FILE.
#
#   JARVIS_FEATURES        optional parts to include: browser github home-assistant coding tunnel
#   DEPLOY_COMPOSE_FILES   legacy list of overlay files; each is mapped to its feature name
#   JARVIS_COMPOSE_DIR     where the generated file goes (default artifacts/compose)

JARVIS_COMPOSE_DIR="${JARVIS_COMPOSE_DIR:-artifacts/compose}"

jarvis_features() {
  local features="${JARVIS_FEATURES:-}" file
  # Deployments configured before the AppHost owned the stack list overlay files; keep them working.
  for file in ${DEPLOY_COMPOSE_FILES:-}; do
    case "${file}" in
      *browser*) features+=" browser" ;;
      *github*) features+=" github" ;;
      *home-assistant*) features+=" home-assistant" ;;
      *coding*) features+=" coding" ;;
      *tunnel*) features+=" tunnel" ;;
      *) echo "Ignoring unknown DEPLOY_COMPOSE_FILES entry ${file}; set JARVIS_FEATURES instead." >&2 ;;
    esac
  done
  echo "${features}" | xargs
}

jarvis_publish_compose() {
  local features
  features="$(jarvis_features)"
  echo "Generating the Compose file from the AppHost (features: ${features:-none})."
  JARVIS_FEATURES="${features}" scripts/deploy/publish-compose.sh "${JARVIS_COMPOSE_DIR}"
}

jarvis_compose() (
  # Actions exports an empty value when vars.JARVIS_DOMAIN is unset. Compose
  # gives shell variables precedence over --env-file, including empty values.
  # Let existing deployments use their saved hostname without changing the caller.
  if [[ -z "${JARVIS_DOMAIN:-}" ]]; then
    unset JARVIS_DOMAIN
  fi
  docker compose -p jarvis --project-directory . --env-file "${ENV_FILE}" \
    -f "${JARVIS_COMPOSE_DIR}/docker-compose.yaml" "$@"
)
