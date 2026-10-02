#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source_dir="${ROOT}/apps/mobile/build/web"
destination="${ROOT}/infra/web/dist"
python3 "${ROOT}/scripts/ci/web_release.py" stage \
  --source "${source_dir}" \
  --destination "${destination}"
