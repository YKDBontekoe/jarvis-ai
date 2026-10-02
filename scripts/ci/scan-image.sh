#!/usr/bin/env bash
set -euo pipefail
image="$1"
report="$(python3 -c 'import os,sys;print(os.path.abspath(sys.argv[1]))' "$2")"
scanner="$(python3 -c 'import json; print("aquasec/trivy:latest@" + json.load(open("scripts/ci/images.lock.json"))["aquasec/trivy:latest"])')"
mkdir -p "$(dirname "$report")"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$(dirname "$report"):/reports" -v jarvis-trivy-cache:/root/.cache/trivy "$scanner" image \
  --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --format json --output "/reports/$(basename "$report")" "$image"
