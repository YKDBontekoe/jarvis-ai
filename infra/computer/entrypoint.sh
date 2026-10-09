#!/bin/sh
# Starts the display, window manager and live view, then hands over to the Node server, which owns Chromium and the
# Playwright MCP server (it restarts both on reset).
set -eu

mkdir -p "$HOME" /tmp/.X11-unix 2>/dev/null || true

Xvfb "$DISPLAY" -screen 0 "${SCREEN_WIDTH}x${SCREEN_HEIGHT}x24" -nolisten tcp -ac &
for _ in $(seq 1 50); do
    [ -e "/tmp/.X11-unix/X${DISPLAY#:}" ] && break
    sleep 0.1
done

openbox &
# VNC only on loopback; websockify is the one way in, and only the API reaches it.
x11vnc -display "$DISPLAY" -forever -shared -nopw -localhost -rfbport 5900 -quiet -xkb -noxdamage &
websockify --web /usr/share/novnc 0.0.0.0:6080 127.0.0.1:5900 >/dev/null 2>&1 &

exec node /app/src/server.mjs
