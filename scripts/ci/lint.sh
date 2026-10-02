#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
image() { python3 - "$1" <<'PY'
import json, sys
ref = sys.argv[1]
print(ref + '@' + json.load(open('scripts/ci/images.lock.json'))[ref])
PY
}
docker run --rm -v "$PWD:/repo" -w /repo "$(image rhysd/actionlint:1.7.11)" -shellcheck=''
shellcheck scripts/ci/*.sh scripts/deploy/*.sh scripts/ios/*.sh
for file in infra/compose/Dockerfile workers/whatsapp-bridge/Dockerfile; do
  docker run --rm -i --entrypoint /bin/hadolint "$(image hadolint/hadolint:v2.14.0)" --ignore DL3008 --ignore DL3016 --ignore DL3018 - < "$file"
done
python3 scripts/ci/check-compose.py
