#!/usr/bin/env bash
set -euo pipefail
name="$1"
image="jarvis-ci-${name}:test"
if [[ "$name" == whatsapp-bridge ]]; then
  container="$(docker run -d "$image")"
  trap 'docker rm -f "$container" >/dev/null' EXIT
  for _ in $(seq 1 30); do
    if docker exec "$container" node -e "fetch('http://localhost:3000/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"; then exit 0; fi
    sleep 1
  done
  exit 1
fi
docker run --rm --entrypoint node "$image" --version
docker run --rm --entrypoint github-mcp-server "$image" --version
if [[ "$name" == api ]]; then assembly=Jarvis.Api; else assembly=Jarvis.Worker; fi
docker run --rm --entrypoint sh "$image" -c "test -s /app/${assembly}.dll"
