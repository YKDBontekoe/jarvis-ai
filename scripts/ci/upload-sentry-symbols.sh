#!/usr/bin/env bash
set -euo pipefail
npm ci --prefix scripts/ci/sentry
scripts/ci/load-images.sh artifacts/images
symbol_directory="${RUNNER_TEMP}/jarvis-sentry-symbols"
mkdir -p "$symbol_directory"
for name in api worker; do
  container="$(docker create "jarvis-ci-${name}:test")"
  docker cp "$container:/app" "$symbol_directory/$name"
  docker rm "$container" >/dev/null
done
scripts/ci/sentry/node_modules/.bin/sentry-cli debug-files upload --include-sources "$symbol_directory"
