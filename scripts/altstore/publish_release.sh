#!/usr/bin/env bash
# Publish the unsigned IPA and AltStore source.json on the Jarvis server.
set -euo pipefail

: "${VERSION:?VERSION is required}"
: "${BUILD_VERSION:?BUILD_VERSION is required}"
: "${ALTSTORE_CONTENT_DIR:?ALTSTORE_CONTENT_DIR is required}"
: "${SOURCE_BASE_URL:?SOURCE_BASE_URL is required}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
IPA_PATH="${IPA_PATH:-${ROOT}/dist/Jarvis.ipa}"
WEBSITE="${WEBSITE:-https://jarvis.ykdbonte.dev}"

if [[ ! -f "${IPA_PATH}" ]]; then
  echo "IPA was not found at ${IPA_PATH}" >&2
  exit 1
fi

mkdir -p "${ALTSTORE_CONTENT_DIR}"
chmod 0755 "${ALTSTORE_CONTENT_DIR}"
IPA_NAME="Jarvis-${VERSION}.ipa"
install -m 0644 "${IPA_PATH}" "${ALTSTORE_CONTENT_DIR}/${IPA_NAME}.tmp"
mv "${ALTSTORE_CONTENT_DIR}/${IPA_NAME}.tmp" "${ALTSTORE_CONTENT_DIR}/${IPA_NAME}"
install -m 0644 "${ROOT}/altstore/icon.png" "${ALTSTORE_CONTENT_DIR}/icon.png.tmp"
mv "${ALTSTORE_CONTENT_DIR}/icon.png.tmp" "${ALTSTORE_CONTENT_DIR}/icon.png"
python3 "${ROOT}/scripts/altstore/generate_source.py" \
  --ipa "${ALTSTORE_CONTENT_DIR}/${IPA_NAME}" \
  --download-url "${SOURCE_BASE_URL}/${IPA_NAME}" \
  --icon-url "${SOURCE_BASE_URL}/icon.png" \
  --website "${WEBSITE}" \
  --version "${VERSION}" \
  --build-version "${BUILD_VERSION}" \
  --changelog "Jarvis ${VERSION}" \
  --previous-source "${ALTSTORE_CONTENT_DIR}/source.json" \
  --output "${ALTSTORE_CONTENT_DIR}/source.json.tmp"
chmod 0644 "${ALTSTORE_CONTENT_DIR}/source.json.tmp"
mv "${ALTSTORE_CONTENT_DIR}/source.json.tmp" "${ALTSTORE_CONTENT_DIR}/source.json"
echo "Published ${SOURCE_BASE_URL}/source.json"
