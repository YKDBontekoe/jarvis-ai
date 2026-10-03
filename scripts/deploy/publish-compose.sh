#!/usr/bin/env bash
# Generates the production Docker Compose file from the Aspire AppHost (src/Jarvis.AppHost).
# The AppHost is the only definition of the deployment; never edit the generated file.
#
#   scripts/deploy/publish-compose.sh [output-dir]      default: artifacts/compose
#
# JARVIS_FEATURES (comma- or space-separated) turns on optional parts: browser github home-assistant coding tunnel.
# Uses a local .NET 10 SDK when there is one, otherwise the pinned SDK container, so a server only needs Docker.
# Run the result with the repository root as the Compose project directory:
#   docker compose -p jarvis --project-directory . --env-file <env> -f artifacts/compose/docker-compose.yaml up -d
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}"

out="${1:-artifacts/compose}"
mkdir -p "${out}"
out="$(cd "${out}" && pwd)"
rm -f "${out}/docker-compose.yaml"

sdk_image="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0.302}"
args=(--operation publish --step publish --output-path "${out}" "--Jarvis:Features=${JARVIS_FEATURES:-}")

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  dotnet run --project src/Jarvis.AppHost --configuration Release -- "${args[@]}"
else
  case "${out}" in
    "${ROOT}"/*) container_out="/src/${out#"${ROOT}"/}" ;;
    *) echo "The output directory must be inside the repository when publishing with Docker." >&2; exit 1 ;;
  esac
  args[5]="${container_out}"
  mkdir -p artifacts/nuget
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e NUGET_PACKAGES=/nuget \
    -v "${ROOT}/artifacts/nuget:/nuget" \
    -v "${ROOT}:/src" -w /src \
    "${sdk_image}" \
    dotnet run --project src/Jarvis.AppHost --configuration Release -- "${args[@]}"
fi

if [[ ! -s "${out}/docker-compose.yaml" ]]; then
  echo "The AppHost did not write ${out}/docker-compose.yaml." >&2
  exit 1
fi
echo "Wrote ${out}/docker-compose.yaml"
