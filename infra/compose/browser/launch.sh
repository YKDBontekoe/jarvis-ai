#!/bin/sh
set -eu

user_agent="$(node /etc/jarvis-browser/user-agent.mjs)"
exec node /app/cli.js --headless --browser chromium --no-sandbox --user-agent "$user_agent" "$@"
