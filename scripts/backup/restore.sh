#!/usr/bin/env bash
# Restores a backup written by infra/backup/backup.sh into the production Compose stack.
#
#   scripts/backup/restore.sh /path/to/backups/jarvis-20261002T030000Z
#
# Stops the API, worker and Temporal, replaces both databases and the data protection key ring with the
# backup's contents, then starts the stack again. The current key ring is kept beside the original as
# <dir>.before-restore-<timestamp>. Set BACKUP_PASSPHRASE for encrypted backups and
# JARVIS_RESTORE_CONFIRM=yes to skip the prompt.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}"

backup="${1:?Usage: scripts/backup/restore.sh <backup directory>}"
backup="$(cd "${backup}" && pwd)"

ENV_FILE="${ENV_FILE:-infra/compose/.env.production}"
if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Production env file not found: ${ENV_FILE}" >&2
  exit 1
fi

# shellcheck source=scripts/deploy/compose-env.sh
source scripts/deploy/compose-env.sh
if [[ ! -s "${JARVIS_COMPOSE_DIR}/docker-compose.yaml" ]]; then jarvis_publish_compose; fi
compose() { jarvis_compose "$@"; }

env_value() {
  # Reads KEY=value from the env file without sourcing it.
  sed -n "s/^$1=//p" "${ENV_FILE}" | tail -n1
}

echo "Verifying checksums in ${backup}."
(cd "${backup}" && sha256sum --check --quiet SHA256SUMS)

work="$(mktemp -d)"
chmod 700 "${work}"
trap 'rm -rf "${work}"' EXIT

for file in "${backup}"/*; do
  name="$(basename "${file}")"
  [[ "${name}" == SHA256SUMS ]] && continue
  if [[ "${name}" == *.enc ]]; then
    : "${BACKUP_PASSPHRASE:?This backup is encrypted; set BACKUP_PASSPHRASE}"
    openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass env:BACKUP_PASSPHRASE \
      -in "${file}" -out "${work}/${name%.enc}"
  else
    cp "${file}" "${work}/${name}"
  fi
done

[[ -f "${work}/jarvis.dump" ]] || { echo "The backup has no jarvis.dump." >&2; exit 1; }

if [[ "${JARVIS_RESTORE_CONFIRM:-}" != yes ]]; then
  echo "This replaces the Jarvis database, the Temporal database, and the key ring with ${backup}."
  read -r -p "Type 'restore' to continue: " answer
  [[ "${answer}" == restore ]] || { echo "Cancelled."; exit 1; }
fi

echo "Stopping the API, worker, Temporal and the backup schedule."
compose stop jarvis-api jarvis-worker temporal temporal-ui backup
compose up -d --no-build --wait postgres temporal-postgres

restore_database() {
  local service=$1 user=$2 name=$3 dump=$4
  echo "Restoring ${name}."
  compose exec -T "${service}" pg_restore --username "${user}" --dbname "${name}" \
    --clean --if-exists --no-owner --single-transaction --exit-on-error < "${dump}"
}

restore_database postgres jarvis jarvis "${work}/jarvis.dump"
if [[ -f "${work}/temporal.dump" ]]; then
  restore_database temporal-postgres temporal temporal "${work}/temporal.dump"
fi

if [[ -f "${work}/data-protection-keys.tar.gz" ]]; then
  keys_dir="$(env_value JARVIS_DATA_PROTECTION_KEYS_DIR)"
  if [[ -z "${keys_dir}" ]]; then
    echo "JARVIS_DATA_PROTECTION_KEYS_DIR is not set in ${ENV_FILE}." >&2
    exit 1
  fi
  if [[ -d "${keys_dir}" ]]; then
    previous="${keys_dir}.before-restore-$(date -u +%Y%m%dT%H%M%SZ)"
    cp -a "${keys_dir}" "${previous}"
    echo "Kept the current key ring at ${previous}."
    find "${keys_dir}" -mindepth 1 -delete
  else
    mkdir -p "${keys_dir}"
  fi
  tar -xzf "${work}/data-protection-keys.tar.gz" -C "${keys_dir}"
  uid="$(env_value JARVIS_UID)"
  gid="$(env_value JARVIS_GID)"
  if [[ -n "${uid}" && -n "${gid}" ]]; then chown -R "${uid}:${gid}" "${keys_dir}" 2>/dev/null || true; fi
  chmod 700 "${keys_dir}"
fi

echo "Starting Jarvis."
compose up -d --no-build
echo "Restore finished. Check the API health and sign in to confirm your data."
