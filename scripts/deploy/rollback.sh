#!/usr/bin/env bash
# Restore application images/configuration without attempting a destructive schema downgrade.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
if [[ "${ROLLBACK_SCHEMA_COMPATIBLE:-false}" != true ]]; then
  echo 'Rollback requires evidence that the previous application supports the migrated database; set ROLLBACK_SCHEMA_COMPATIBLE=true after reviewing the upgrade/rollback tests.' >&2
  exit 1
fi
exec 9>"${DEPLOY_LOCK_FILE:-/tmp/jarvis-deploy.lock}"
flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
state_directory="${DEPLOY_STATE_DIRECTORY:-.jarvis/deploy}"
configuration="$state_directory/previous-compose.private.yml"
test -s "$configuration"
test -s "$state_directory/previous-env.private"
project="${DEPLOY_PROJECT_NAME:-jarvis}"
docker compose -p "$project" -f "$configuration" up -d --no-build --remove-orphans --wait --wait-timeout 240
cp "$state_directory/previous-env.private" "${ENV_FILE:-infra/compose/.env.production}"
chmod 0600 "${ENV_FILE:-infra/compose/.env.production}"
cp "$configuration" "$state_directory/current-compose.private.yml"
echo 'Previous application configuration restored. The database was preserved; verify authenticated reads and task execution before accepting recovery.'
