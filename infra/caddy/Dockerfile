# Caddy with the layer4 plugin, so LiveKit's ICE-TCP media can share TCP 443 with HTTPS (see Caddyfile).
# caddy-l4 releases pin the Caddy version they build against; bump both together.
FROM caddy:2.11.4-builder-alpine AS build
RUN xcaddy build --with github.com/mholt/caddy-l4@v0.1.2

FROM caddy:2.11.4-alpine
COPY --from=build /usr/bin/caddy /usr/bin/caddy
