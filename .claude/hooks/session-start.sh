#!/bin/bash
# SessionStart hook for Claude Code on the web: installs the pinned .NET SDK
# (global.json) and Flutter stable, then restores backend and mobile deps so
# `dotnet test` and `flutter analyze/test` work straight away.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$REPO_ROOT"

DOTNET_ROOT="$HOME/.dotnet"
FLUTTER_ROOT="$HOME/flutter"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 FLUTTER_SUPPRESS_ANALYTICS=true
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$FLUTTER_ROOT/bin:$PATH"

# .NET SDK pinned by global.json (dotnet-install skips when already present).
DOTNET_VERSION="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json | head -1)"
if ! dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION} "; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir "$DOTNET_ROOT"
fi

# Flutter stable, matching CI (subosito/flutter-action channel: stable).
if [ ! -x "$FLUTTER_ROOT/bin/flutter" ]; then
  git clone --depth 1 -b stable https://github.com/flutter/flutter.git "$FLUTTER_ROOT"
fi
flutter --disable-analytics >/dev/null 2>&1 || true
dart --disable-analytics >/dev/null 2>&1 || true

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 FLUTTER_SUPPRESS_ANALYTICS=true"
    echo "export PATH=\"$DOTNET_ROOT:$DOTNET_ROOT/tools:$FLUTTER_ROOT/bin:\$PATH\""
  } >> "$CLAUDE_ENV_FILE"
fi

# Backend: tools (dotnet-ef) + restore. Build is left to the session.
dotnet tool restore
dotnet restore Jarvis.sln

# Mobile: pub dependencies (also warms the Flutter engine cache).
(cd apps/mobile && flutter pub get)
