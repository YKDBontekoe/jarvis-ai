#!/usr/bin/env bash
# Nightly Jarvis backup, run by the `backup` service in docker-compose.production.yml.
#
# Each run writes one directory under $BACKUP_DIR:
#   jarvis-YYYYMMDDTHHMMSSZ/
#     jarvis.dump                 pg_dump custom format of the application database
#     temporal.dump               pg_dump custom format of the Temporal database
#     data-protection-keys.tar.gz ASP.NET Core key ring; without it stored credentials cannot be decrypted
#     SHA256SUMS
# When BACKUP_PASSPHRASE is set, every file is encrypted with AES-256 (openssl, PBKDF2) and gets a .enc suffix.
#
# Usage: backup.sh            run forever, once a day at BACKUP_HOUR_UTC
#        backup.sh --once     take one backup now and exit
set -euo pipefail

: "${BACKUP_DIR:=/backups}"
: "${BACKUP_HOUR_UTC:=3}"
: "${BACKUP_RETENTION_DAYS:=14}"
: "${KEYS_DIR:=/data-protection-keys}"
: "${JARVIS_DB_HOST:=postgres}"
: "${JARVIS_DB_NAME:=jarvis}"
: "${JARVIS_DB_USER:=jarvis}"
: "${TEMPORAL_DB_HOST:=temporal-postgres}"
: "${TEMPORAL_DB_NAME:=temporal}"
: "${TEMPORAL_DB_USER:=temporal}"

log() { printf '%s backup: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

dump() {
  local host=$1 name=$2 user=$3 password=$4 target=$5
  PGPASSWORD=$password pg_dump --host "$host" --username "$user" --dbname "$name" \
    --format custom --compress 6 --no-owner --file "$target"
}

encrypt_all() {
  local dir=$1
  [[ -n "${BACKUP_PASSPHRASE:-}" ]] || return 0
  local file
  for file in "$dir"/*; do
    [[ "$file" == *.enc || "$(basename "$file")" == SHA256SUMS ]] && continue
    openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass env:BACKUP_PASSPHRASE \
      -in "$file" -out "$file.enc"
    rm -f "$file"
  done
}

take_backup() {
  local stamp target partial
  stamp=$(date -u +%Y%m%dT%H%M%SZ)
  target="$BACKUP_DIR/jarvis-$stamp"
  partial="$target.partial"
  # Leftovers from a failed run are never valid backups.
  find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d -name 'jarvis-*.partial' -exec rm -rf {} +
  mkdir -p "$partial"
  chmod 700 "$partial"

  log "dumping $JARVIS_DB_NAME"
  dump "$JARVIS_DB_HOST" "$JARVIS_DB_NAME" "$JARVIS_DB_USER" "${JARVIS_DB_PASSWORD:?Set JARVIS_DB_PASSWORD}" \
    "$partial/jarvis.dump"

  if [[ -n "${TEMPORAL_DB_PASSWORD:-}" ]]; then
    log "dumping $TEMPORAL_DB_NAME"
    dump "$TEMPORAL_DB_HOST" "$TEMPORAL_DB_NAME" "$TEMPORAL_DB_USER" "$TEMPORAL_DB_PASSWORD" "$partial/temporal.dump"
  fi

  if [[ -d "$KEYS_DIR" ]]; then
    log "archiving the data protection key ring"
    tar -czf "$partial/data-protection-keys.tar.gz" -C "$KEYS_DIR" .
  else
    log "warning: $KEYS_DIR is missing; encrypted integration credentials will not be restorable"
  fi

  encrypt_all "$partial"
  (cd "$partial" && sha256sum -- * > SHA256SUMS)
  chmod 600 "$partial"/*
  mv "$partial" "$target"
  log "wrote $target"

  prune
}

prune() {
  find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d -name 'jarvis-*' \
    -mtime +"$BACKUP_RETENTION_DAYS" -print -exec rm -rf {} + | while read -r removed; do
      log "removed expired $removed"
    done
}

seconds_until_next_run() {
  local now next
  now=$(date -u +%s)
  next=$(date -u -d "today ${BACKUP_HOUR_UTC}:00" +%s)
  (( next <= now )) && next=$(date -u -d "tomorrow ${BACKUP_HOUR_UTC}:00" +%s)
  echo $(( next - now ))
}

umask 077
mkdir -p "$BACKUP_DIR"

if [[ "${1:-}" == "--once" ]]; then
  take_backup
  exit 0
fi

log "scheduled daily at ${BACKUP_HOUR_UTC}:00 UTC, keeping ${BACKUP_RETENTION_DAYS} days"
while true; do
  sleep "$(seconds_until_next_run)"
  # Each run is a separate process so `set -e` stops it at the first failed step. A failed run is logged and
  # retried the next day rather than stopping the service; the partial directory never replaces a good one.
  bash "$0" --once || log "error: backup failed; the next attempt is in 24 hours"
done
