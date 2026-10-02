#!/usr/bin/env bash
# Apply a built Jarvis release on the self-hosted deploy runner.
# Images are already in GHCR. SOURCE_BUNDLE is a git bundle of the release commit.
set -euo pipefail

: "${DEPLOY_PATH:?DEPLOY_PATH is required}"
: "${GHCR_TOKEN:?GHCR_TOKEN is required}"
: "${GHCR_USER:?GHCR_USER is required}"
: "${GIT_SHA:?GIT_SHA is required}"
: "${SOURCE_BUNDLE:?SOURCE_BUNDLE is required}"
: "${JARVIS_API_IMAGE:?JARVIS_API_IMAGE is required}"
: "${JARVIS_WORKER_IMAGE:?JARVIS_WORKER_IMAGE is required}"

cd "${DEPLOY_PATH}"
if [[ ! -d .git ]]; then
  git init
fi
git fetch --force "${SOURCE_BUNDLE}" HEAD
git checkout --detach --force "${GIT_SHA}"
echo "${GHCR_TOKEN}" | docker login ghcr.io -u "${GHCR_USER}" --password-stdin
python3 scripts/deploy/sentry_release_env.py \
  --env-file "${ENV_FILE:-infra/compose/.env.production}" \
  --release "${GIT_SHA}"
chmod +x scripts/deploy/remote-up.sh
scripts/deploy/remote-up.sh
