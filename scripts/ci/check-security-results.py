#!/usr/bin/env python3
"""Fail on high/critical static security findings, beyond uploading SARIF."""
import json
import sys
from pathlib import Path
reports = list(Path(sys.argv[1]).rglob('*.sarif'))
if not reports: raise SystemExit('Missing static security analysis reports')
findings = []
for report in reports:
    for run in json.loads(report.read_text())['runs']:
        rules = {rule['id']: rule for tool in [run['tool']['driver'], *run['tool'].get('extensions', [])] for rule in tool.get('rules', [])}
        for result in run.get('results', []):
            if any(value.get('status') == 'accepted' for value in result.get('suppressions', [])): continue
            rule = rules.get(result['ruleId'], {})
            severity = float(rule.get('properties', {}).get('security-severity', '0'))
            if severity >= 7: findings.append(result['ruleId'])
if findings: raise SystemExit('High/critical static security findings: ' + ', '.join(sorted(set(findings))))
print(f'Validated {len(reports)} static security reports')
