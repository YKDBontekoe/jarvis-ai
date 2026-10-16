#!/bin/bash
# Environment setup script for Claude Code on the web (cloud environment
# settings → Setup script). Installs the heavy toolchains once per container so
# the SessionStart hook (.claude/hooks/session-start.sh) only has to restore
# dependencies: the pinned .NET SDK, Flutter stable with its Linux/web
# artifacts, and the container images the integration tests (Testcontainers)
# and the local smoke stack (scripts/dev/smoke.sh) start.
#
# Self-contained on purpose: paste it into the environment's Setup script
# field as-is. It reads global.json when run from the repo root and falls back
# to the pinned SDK version otherwise. Safe to re-run.
set -euo pipefail

DOTNET_VERSION_FALLBACK="10.0.402"
DOTNET_ROOT="$HOME/.dotnet"
FLUTTER_ROOT="$HOME/flutter"
# Keep in sync with src/Jarvis.AppHost/LocalDevelopment.cs.
IMAGES=(
  pgvector/pgvector:pg18
  temporalio/temporal:1.9.1
  chrislusf/seaweedfs:4.47
  livekit/livekit-server:latest
  clamav/clamav:stable
  bbernhard/signal-cli-rest-api:latest
)

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 FLUTTER_SUPPRESS_ANALYTICS=true
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$FLUTTER_ROOT/bin:$PATH"

SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

echo "==> .NET SDK"
DOTNET_VERSION=""
if [ -f global.json ]; then
  DOTNET_VERSION="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json | head -1)"
fi
DOTNET_VERSION="${DOTNET_VERSION:-$DOTNET_VERSION_FALLBACK}"
if ! dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION} "; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_ROOT"
fi
$SUDO ln -sf "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet
dotnet --version

echo "==> Flutter (stable)"
if [ ! -x "$FLUTTER_ROOT/bin/flutter" ]; then
  git clone --depth 1 -b stable https://github.com/flutter/flutter.git "$FLUTTER_ROOT"
fi
flutter --disable-analytics >/dev/null 2>&1 || true
dart --disable-analytics >/dev/null 2>&1 || true
flutter precache --linux --web
$SUDO ln -sf "$FLUTTER_ROOT/bin/flutter" /usr/local/bin/flutter
$SUDO ln -sf "$FLUTTER_ROOT/bin/dart" /usr/local/bin/dart
flutter --version

echo "==> Docker images for integration tests and the smoke stack (best effort)"
if command -v dockerd >/dev/null 2>&1; then
  started_dockerd=""
  if ! docker info >/dev/null 2>&1; then
    $SUDO sh -c 'nohup dockerd >/var/log/dockerd.log 2>&1 &'
    started_dockerd="yes"
    for _ in $(seq 1 30); do
      docker info >/dev/null 2>&1 && break
      sleep 1
    done
  fi
  if docker info >/dev/null 2>&1; then
    for image in "${IMAGES[@]}"; do
      docker pull -q "$image" || echo "warning: could not pre-pull $image"
    done
  else
    echo "warning: Docker daemon did not start; integration tests will need it"
  fi
  # Leave the daemon to the SessionStart hook so it is not tied to this script.
  if [ -n "$started_dockerd" ]; then
    $SUDO pkill -x dockerd || true
  fi
else
  echo "warning: dockerd not installed; integration tests need Docker"
fi

echo "==> Setup complete"
