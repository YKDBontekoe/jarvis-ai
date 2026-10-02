#!/usr/bin/env bash
set -euo pipefail
image="$1"
report="$(python3 -c 'import os,sys;print(os.path.abspath(sys.argv[1]))' "$2")"
scanner="$(python3 -c 'import json; print("aquasec/trivy:latest@" + json.load(open("scripts/ci/images.lock.json"))["aquasec/trivy:latest"])')"
mkdir -p "$(dirname "$report")"
status=0
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$(dirname "$report"):/reports" -v jarvis-trivy-cache:/root/.cache/trivy "$scanner" image \
  --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --format json --output "/reports/$(basename "$report")" "$image" || status=$?

if [[ -s "$report" ]]; then
  python3 - "$report" <<'PY_REPORT'
import json, sys
for result in json.load(open(sys.argv[1])).get('Results', []):
    for finding in result.get('Vulnerabilities', []):
        print(f"{result['Target']}: {finding['VulnerabilityID']} {finding['Severity']} "
              f"{finding['PkgName']} {finding['InstalledVersion']} -> {finding.get('FixedVersion', 'unfixed')}")
PY_REPORT
fi
exit "$status"
