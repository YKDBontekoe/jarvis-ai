#!/usr/bin/env bash
# Idempotent Cloud Agent bootstrap for the Jarvis .NET monolith.
# Installs the pinned .NET SDK, Docker (for Aspire/Testcontainers/Compose),
# the dotnet-ef local tool, then restores and builds the whole solution.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET_VERSION="10.0.302"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> Ensuring .NET SDK ${DOTNET_VERSION}"
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION} "; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_ROOT"
fi
sudo ln -sf "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet --version

echo "==> Ensuring Docker engine (for Aspire, Testcontainers, and Compose)"
if ! command -v dockerd >/dev/null 2>&1; then
  # Prevent maintainer scripts from starting services (no systemd in the VM),
  # and force non-interactive conffile handling to avoid an install-time prompt.
  echo -e '#!/bin/sh\nexit 101' | sudo tee /usr/sbin/policy-rc.d >/dev/null
  sudo chmod +x /usr/sbin/policy-rc.d
  sudo apt-get update -qq
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq \
    -o Dpkg::Options::=--force-confold -o Dpkg::Options::=--force-confdef \
    docker.io docker-compose-v2 fuse-overlayfs uidmap
  sudo rm -f /usr/sbin/policy-rc.d
fi
sudo groupadd -f docker
sudo usermod -aG docker "$(id -un)" || true

echo "==> Preparing isolated Python CI tooling"
if ! python3 -m venv .venv-ci; then
  sudo apt-get update -qq
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq python3-venv
  python3 -m venv .venv-ci
fi
.venv-ci/bin/python3 -m pip install -r scripts/ci/requirements.txt

echo "==> Restoring dotnet local tools (dotnet-ef)"
dotnet tool restore

echo "==> Restoring and building the solution"
dotnet build Jarvis.sln -c Debug

echo "==> Install complete"
