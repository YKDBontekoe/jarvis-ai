#!/usr/bin/env bash
set -euo pipefail
image_directory="$(cd "$1" && pwd)"
for name in api worker whatsapp-bridge; do
  skopeo copy --override-os linux --override-arch amd64 "oci-archive:${image_directory}/${name}.oci.tar" "docker-daemon:jarvis-ci-${name}:test"
done
