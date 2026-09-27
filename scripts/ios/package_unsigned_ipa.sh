#!/usr/bin/env bash
# Package a Flutter iOS build (built with --no-codesign) into an unsigned .ipa
# suitable for LiveContainer or manual sideload workflows.
set -euo pipefail

APP_PATH="${1:?Usage: package_unsigned_ipa.sh path/to/Runner.app [output.ipa]}"
OUTPUT="${2:-Jarvis.ipa}"
OUTPUT_NAME="$(basename "${OUTPUT}")"

if [[ ! -d "${APP_PATH}" ]]; then
  echo "App bundle not found: ${APP_PATH}" >&2
  exit 1
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "${STAGE}"' EXIT

mkdir -p "${STAGE}/Payload"
cp -R "${APP_PATH}" "${STAGE}/Payload/"

(
  cd "${STAGE}"
  zip -qr "${OUTPUT_NAME}" Payload
)

mkdir -p "$(dirname "${OUTPUT}")"
mv "${STAGE}/${OUTPUT_NAME}" "${OUTPUT}"
echo "Wrote unsigned IPA: ${OUTPUT}"
