#!/usr/bin/env python3
"""Record only synthetic CI workflows; never fetch production histories."""
import json
import subprocess
from pathlib import Path
compose = ['docker', 'compose', '-p', 'jarvis-ci', '-f', 'tests/e2e/docker-compose.ci.yml', 'exec', '-T', 'temporal', 'temporal']
listing = json.loads(subprocess.check_output(compose + ['workflow', 'list', '--query', 'ExecutionStatus="Completed"', '--output', 'json']))
destination = Path('artifacts/ci/histories')
destination.mkdir(parents=True, exist_ok=True)
seen = set()
for execution in listing:
    workflow_type = execution['type']['name']
    if workflow_type in seen: continue
    execution_id = execution['execution']['workflowId']
    history = subprocess.check_output(compose + ['workflow', 'show', '--workflow-id', execution_id, '--output', 'json'])
    (destination / f'{workflow_type}.json').write_bytes(history)
    seen.add(workflow_type)
if not {'JarvisTaskWorkflow', 'DeploymentProbeWorkflow'} <= seen:
    raise SystemExit(f'Missing completed workflow histories: {seen}')
print(f'Captured {len(seen)} synthetic workflow histories')
