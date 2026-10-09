# The computer-use sandbox: a small Linux desktop (Xvfb + openbox) with a visible Chromium, driven by Jarvis through
# two MCP servers (Playwright on 8931 for the browser, the desktop server on 8932) and watched live over noVNC (6080).
# Every port stays on the internal agents network; the API proxies the live view. See docs/backend/integrations-and-mcp.md.
FROM node:22-bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        chromium xvfb openbox x11vnc xdotool imagemagick novnc websockify \
        fonts-dejavu-core fonts-liberation fonts-noto-color-emoji \
        ca-certificates curl wget jq unzip procps python3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV NODE_ENV=production PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
COPY server/package.json server/package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY server/src ./src
COPY entrypoint.sh /usr/local/bin/computer-entrypoint
RUN chmod 0755 /usr/local/bin/computer-entrypoint

ENV DISPLAY=:99 SCREEN_WIDTH=1280 SCREEN_HEIGHT=800 HOME=/home/node
USER node
EXPOSE 6080 8931 8932
HEALTHCHECK --interval=10s --timeout=3s --retries=10 CMD node -e "fetch('http://127.0.0.1:8932/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
ENTRYPOINT ["/usr/local/bin/computer-entrypoint"]
