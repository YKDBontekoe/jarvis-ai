#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repo_root}"

python3 -m unittest \
  tests/unit/altstore/test_generate_source.py \
  tests/unit/compose/test_production_images.py \
  tests/unit/release/test_semver.py
