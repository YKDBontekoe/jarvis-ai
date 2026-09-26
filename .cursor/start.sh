#!/usr/bin/env bash
# Per-boot startup for the Jarvis Cloud Agent environment.
# Starts the Docker daemon (nested-container friendly) so Aspire, Testcontainers,
# and Docker Compose work. Idempotent and safe to re-run.
set -euo pipefail

if docker info >/dev/null 2>&1; then
  echo "==> Docker daemon already running"
  exit 0
fi

echo "==> Starting dockerd (fuse-overlayfs storage driver)"
sudo mkdir -p /var/log
sudo bash -c 'nohup dockerd --storage-driver=fuse-overlayfs >/var/log/dockerd.log 2>&1 &'

echo "==> Waiting for the Docker socket"
for i in $(seq 1 30); do
  if sudo docker info >/dev/null 2>&1; then
    # Allow the unprivileged agent user to use the socket without sudo.
    sudo chmod 666 /var/run/docker.sock || true
    echo "==> Docker is ready after ${i}s"
    exit 0
  fi
  sleep 1
done

echo "!! Docker did not become ready in time; see /var/log/dockerd.log" >&2
sudo tail -20 /var/log/dockerd.log >&2 || true
exit 1
