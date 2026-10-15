#!/bin/bash
# Smoke test the whole stack locally: start the AppHost on the fake Codex app
# server and fake MCP server (JARVIS_FEATURES=verification), wait for the API,
# then run the e2e fixture flows against it.
#
#   scripts/dev/smoke.sh up                 # start (background) and wait until healthy
#   scripts/dev/smoke.sh test [flow.mjs …]  # run e2e flows (default: local_fixture_flow.mjs)
#   scripts/dev/smoke.sh down               # stop the AppHost and its containers
#   scripts/dev/smoke.sh                    # up + test (leaves the stack running)
#
# Logs: artifacts/smoke/apphost.log. API: http://localhost:5082.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"
OUT="$REPO_ROOT/artifacts/smoke"
PID_FILE="$OUT/apphost.pid"
API_URL="${JARVIS_API_URL:-http://localhost:5082}"
mkdir -p "$OUT"

healthy() { [ "$(curl -s -m 4 -o /dev/null -w '%{http_code}' "$API_URL/health" || true)" = 200 ]; }

up() {
  if healthy; then echo "API already healthy at $API_URL"; return; fi
  if ! docker info >/dev/null 2>&1; then
    echo "Docker is not running (cloud sessions start it from .claude/hooks/session-start.sh)" >&2
    exit 1
  fi
  if (exec 3<>/dev/tcp/127.0.0.1/5082) 2>/dev/null; then
    echo "Port 5082 is in use by an unhealthy process (a stale API?); run '$0 down' or stop it first" >&2
    exit 1
  fi
  export JARVIS_FEATURES="${JARVIS_FEATURES:-verification}"
  # The WhatsApp bridge image runs `npm ci` inside `docker build`, which cannot
  # reach the registry through the cloud sandbox proxy; point at a stub URL so
  # the AppHost skips building it.
  if [ "${CLAUDE_CODE_REMOTE:-}" = "true" ]; then
    export Channels__WhatsAppBridge__BaseUrl="${Channels__WhatsAppBridge__BaseUrl:-http://localhost:3000}"
  fi
  echo "==> Starting AppHost (JARVIS_FEATURES=$JARVIS_FEATURES), log: $OUT/apphost.log"
  setsid dotnet run --project src/Jarvis.AppHost >"$OUT/apphost.log" 2>&1 </dev/null &
  echo $! >"$PID_FILE"
  for _ in $(seq 1 120); do
    if healthy; then echo "==> API healthy at $API_URL"; return; fi
    if ! kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
      echo "AppHost exited; see $OUT/apphost.log" >&2
      exit 1
    fi
    sleep 5
  done
  echo "API not healthy after 10 minutes; see $OUT/apphost.log" >&2
  exit 1
}

test_flows() {
  local flows=("$@")
  [ ${#flows[@]} -eq 0 ] && flows=(local_fixture_flow.mjs)
  (cd tests/e2e && { [ -d node_modules ] || npm ci --silent; })
  for flow in "${flows[@]}"; do
    echo "==> $flow"
    (cd tests/e2e && JARVIS_API_URL="$API_URL" node "$flow") 2>&1 | tee "$OUT/${flow%.mjs}.log"
  done
}

down() {
  if [ -f "$PID_FILE" ]; then
    # setsid made the AppHost a process-group leader; stop the whole group.
    kill -- "-$(cat "$PID_FILE")" 2>/dev/null || true
    rm -f "$PID_FILE"
  fi
  sleep 3
  # Aspire's orchestrator (dcp) runs detached; stop it so it cannot restart anything.
  pkill -f 'aspire\.hosting\.orchestration\..*/dcp' 2>/dev/null || true
  # Aspire-managed containers carry its session label; leave other containers alone.
  ids="$(docker ps -aq --filter label=com.microsoft.developer.usvc-dev.persistent=false 2>/dev/null || true)"
  [ -n "$ids" ] && docker rm -f $ids >/dev/null
  echo "==> Stopped"
}

case "${1:-all}" in
  up) up ;;
  test) shift; test_flows "$@" ;;
  down) down ;;
  all) up; test_flows ;;
  *) echo "usage: $0 [up|test [flow.mjs …]|down]" >&2; exit 2 ;;
esac
