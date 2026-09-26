#!/usr/bin/env bash
# Runs the Docker daemon in the foreground so the Cloud Agent platform keeps it
# supervised for the life of the environment. Launched from the `terminals`
# section of .cursor/environment.json. Nested-container friendly (fuse-overlayfs).
set -euo pipefail

# Once the socket appears, relax its permissions so the agent user can reach the
# daemon without sudo. Runs in the background while dockerd holds the foreground.
(
  for _ in $(seq 1 60); do
    if [ -S /var/run/docker.sock ]; then
      sudo chmod 666 /var/run/docker.sock || true
      break
    fi
    sleep 1
  done
) &

exec sudo dockerd --storage-driver=fuse-overlayfs
