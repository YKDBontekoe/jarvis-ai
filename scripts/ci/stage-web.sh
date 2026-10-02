#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source_dir="${ROOT}/apps/mobile/build/web"
destination="${ROOT}/infra/web/dist"
for file in index.html main.dart.js flutter_bootstrap.js; do
  test -s "${source_dir}/${file}"
done
mkdir -p "${destination}"
find "${destination}" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
cp -R "${source_dir}/." "${destination}/"
find "${destination}" -type f -name '*.map' -delete
