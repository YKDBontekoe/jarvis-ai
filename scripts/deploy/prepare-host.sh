#!/usr/bin/env bash
# Prepares this server for Jarvis without hand configuration. remote-up.sh runs it on every deploy; it is safe to
# run any time.
#
# It creates ENV_FILE when missing and fills in every setting Jarvis manages itself: database, object storage,
# account, LiveKit, voice and MCP runner secrets; the service account ids; the data directories; and the Garage
# config. Generated values already in the file are preserved (a database password only applies when the database
# is first created). An explicitly supplied quotes key is updated from the deployment secret.
#
# The required input is the public hostname: JARVIS_DOMAIN in the environment (the deploy workflow passes the
# repository variable) or already in the env file. Optional FINANCE_QUOTES_API_KEY is supplied by the deployment
# secret and replaces the saved quotes key when non-empty. Other settings derive from the hostname or are generated.
#
#   JARVIS_DATA_DIR   where generated state lives (default: $HOME/jarvis-data)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}"

ENV_FILE="${ENV_FILE:-infra/compose/.env.production}"
DATA_DIR="${JARVIS_DATA_DIR:-${HOME}/jarvis-data}"
generated=()

umask 077

if [[ ! -f "${ENV_FILE}" ]]; then
  mkdir -p "$(dirname "${ENV_FILE}")"
  cp infra/compose/.env.production.example "${ENV_FILE}"
  echo "Created ${ENV_FILE}."
fi
chmod 600 "${ENV_FILE}"

# Reads KEY from the env file. Template placeholders count as unset.
env_get() {
  local value
  value="$(sed -n "s/^$1=//p" "${ENV_FILE}" | tail -n1)"
  case "${value}" in
    # Exactly the old template's placeholder values, so a real domain such as myexample.com is never touched.
    replace_with*|/absolute/path/*|jarvis.example.com|voice.example.com|https://jarvis.example.com) value="" ;;
  esac
  printf '%s' "${value}"
}

# Writes KEY=value, replacing an existing (empty or placeholder) line or appending one.
env_set() {
  local key=$1 value=$2 tmp
  tmp="$(mktemp "${ENV_FILE}.XXXXXX")"
  if grep -q "^${key}=" "${ENV_FILE}"; then
    awk -v key="${key}" -v value="${value}" -F= \
      '$1 == key && !done { print key "=" value; done = 1; next } { print }' "${ENV_FILE}" > "${tmp}"
  else
    cat "${ENV_FILE}" > "${tmp}"
    printf '%s=%s\n' "${key}" "${value}" >> "${tmp}"
  fi
  chmod 600 "${tmp}"
  mv "${tmp}" "${ENV_FILE}"
}

# Sets KEY to the given value only when the file has no real value for it yet.
ensure() {
  local key=$1 value=$2
  if [[ -z "$(env_get "${key}")" ]]; then
    env_set "${key}" "${value}"
    generated+=("${key}")
  fi
}

random_hex() {
  if command -v openssl >/dev/null 2>&1; then openssl rand -hex "$1"
  else head -c "$1" /dev/urandom | od -An -tx1 | tr -d ' \n'; fi
}

ensure_secret() { ensure "$1" "$(random_hex "${2:-32}")"; }

# For values a data volume was initialized with (database passwords, storage keys): a template placeholder that is
# already in use must stay, or Jarvis would lock itself out of its own data.
ensure_persisted() {
  local key=$1 volume=$2 value=$3 raw
  raw="$(sed -n "s/^${key}=//p" "${ENV_FILE}" | tail -n1)"
  if [[ -n "${raw}" && -z "$(env_get "${key}")" ]] && command -v docker >/dev/null 2>&1 &&
     docker volume inspect "${volume}" >/dev/null 2>&1; then
    echo "Keeping the placeholder ${key}: volume ${volume} was created with it. Rotate it by hand if you want to." >&2
    return
  fi
  ensure "${key}" "${value}"
}

ensure_directory() {
  local key=$1 path=$2 current
  ensure "${key}" "${path}"
  current="$(env_get "${key}")"
  if [[ ! -d "${current}" ]]; then
    mkdir -p "${current}"
    chmod 700 "${current}"
    echo "Created ${current}."
  fi
}

# Optional Finnhub key from the approved deployment. Persist it so manual Compose operations use the same key.
# An absent repository secret preserves an existing host key. Validate before writing unquoted dotenv syntax.
if [[ -n "${FINANCE_QUOTES_API_KEY:-}" ]]; then
  if [[ ! "${FINANCE_QUOTES_API_KEY}" =~ ^[A-Za-z0-9]+$ ]]; then
    echo "FINANCE_QUOTES_API_KEY must be an alphanumeric Finnhub key." >&2
    exit 1
  fi
  env_set FINANCE_QUOTES_API_KEY "${FINANCE_QUOTES_API_KEY}"
fi

# The public hostname is the one thing Jarvis cannot pick for you.
domain="$(env_get JARVIS_DOMAIN)"
if [[ -z "${domain}" ]]; then
  domain="${JARVIS_DOMAIN:-}"
  if [[ -z "${domain}" ]]; then
    echo "Set JARVIS_DOMAIN (repository variable, or in the environment) to the public hostname, such as jarvis.example.org." >&2
    exit 1
  fi
  env_set JARVIS_DOMAIN "${domain}"
  generated+=(JARVIS_DOMAIN)
fi
ensure LIVEKIT_DOMAIN "${domain}"
ensure JARVIS_WEB_ORIGIN "https://${domain}"
ensure AUTH_ISSUER "https://${domain}"
ensure AUTH_AUDIENCE "jarvis-api"

ensure JARVIS_UID "$(id -u)"
ensure JARVIS_GID "$(id -g)"

ensure_persisted POSTGRES_PASSWORD jarvis_postgres-data "$(random_hex 32)"
ensure_persisted TEMPORAL_PASSWORD jarvis_temporal-postgres-data "$(random_hex 32)"
ensure_secret AUTH_SIGNING_KEY
ensure_secret VOICE_WORKER_SECRET
ensure_secret MCP_RUNNER_TOKEN
ensure_secret LIVEKIT_API_SECRET
ensure LIVEKIT_API_KEY "jarvis$(random_hex 6)"
# Garage access key ids are GK followed by 24 hex characters.
ensure_persisted S3_ACCESS_KEY jarvis_garage-meta "GK$(random_hex 12)"
ensure_persisted S3_SECRET_KEY jarvis_garage-meta "$(random_hex 32)"

mkdir -p "${DATA_DIR}"
chmod 700 "${DATA_DIR}"
ensure_directory CODEX_HOME_DIR "${DATA_DIR}/codex-home"
ensure_directory JARVIS_DATA_PROTECTION_KEYS_DIR "${DATA_DIR}/data-protection-keys"
ensure_directory JARVIS_BACKUP_DIR "${DATA_DIR}/backups"

ensure GARAGE_CONFIG_FILE "${DATA_DIR}/garage.toml"
garage_config="$(env_get GARAGE_CONFIG_FILE)"
if [[ ! -f "${garage_config}" ]]; then
  # Single-node Garage. The region must match ObjectStorage__Region (us-east-1) for request signatures.
  cat > "${garage_config}" <<EOF
metadata_dir = "/var/lib/garage/meta"
data_dir = "/var/lib/garage/data"
db_engine = "sqlite"
replication_factor = 1

rpc_bind_addr = "[::]:3901"
rpc_public_addr = "127.0.0.1:3901"
rpc_secret = "$(random_hex 32)"

[s3_api]
s3_region = "us-east-1"
api_bind_addr = "[::]:3900"
root_domain = ".s3.garage.localhost"

[admin]
api_bind_addr = "[::]:3903"
admin_token = "$(random_hex 32)"
metrics_token = "$(random_hex 32)"
EOF
  chmod 600 "${garage_config}"
  echo "Created ${garage_config}."
fi

if [[ ${#generated[@]} -gt 0 ]]; then
  echo "Filled in ${ENV_FILE}: ${generated[*]}."
else
  echo "${ENV_FILE} already has every managed setting."
fi
if [[ ! -s "$(env_get CODEX_HOME_DIR)/auth.json" ]]; then
  echo "Codex is not signed in yet. After the deploy, open Jarvis → Settings → Models → Sign in to ChatGPT."
fi
