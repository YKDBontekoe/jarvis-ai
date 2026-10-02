#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}/apps/mobile"

args=(--release --source-maps --pwa-strategy=none --no-web-resources-cdn
  --dart-define=SENTRY_ENVIRONMENT=production)
if [[ -n "${JARVIS_WEB_API_URL:-}" ]]; then
  args+=("--dart-define=JARVIS_API_URL=${JARVIS_WEB_API_URL}")
fi
if [[ -n "${JARVIS_SENTRY_DSN:-}" ]]; then
  args+=("--dart-define=JARVIS_SENTRY_DSN=${JARVIS_SENTRY_DSN}")
fi
if [[ -n "${SENTRY_RELEASE:-}" ]]; then
  args+=("--dart-define=SENTRY_RELEASE=${SENTRY_RELEASE}")
fi

flutter pub get
flutter build web "${args[@]}"

# Maps belong in Sentry, not in the public distribution. The release job
# uploads them before staging the deployable artifact.
