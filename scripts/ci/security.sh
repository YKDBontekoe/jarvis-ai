#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
scanner="$(python3 -c 'import json; print("zricethezav/gitleaks:v8.30.1@" + json.load(open("scripts/ci/images.lock.json"))["zricethezav/gitleaks:v8.30.1"])')"
docker run --rm -v "$PWD:/repo" "$scanner" git /repo --config /repo/.gitleaks.toml --redact --no-banner
# Dependency audit is parsed explicitly; command exit codes alone do not reliably gate vulnerabilities.
dotnet restore Jarvis.sln
dotnet package list --project Jarvis.sln --vulnerable --include-transitive --format json > "${RUNNER_TEMP:-/tmp}/jarvis-nuget-audit.json"
python3 - "${RUNNER_TEMP:-/tmp}/jarvis-nuget-audit.json" <<'PY'
import json, sys
report = json.load(open(sys.argv[1]))
issues = [(project['path'], package['id']) for project in report['projects']
          for framework in project.get('frameworks', [])
          for key in ('topLevelPackages', 'transitivePackages') for package in framework.get(key, [])
          if any(v.get('severity', '').lower() in ('high', 'critical') for v in package.get('vulnerabilities', []))]
if issues: raise SystemExit('High/critical NuGet vulnerabilities: ' + str(issues))
PY
(cd tests/e2e && npm audit --audit-level=high)
