#!/usr/bin/env python3
"""Validate workflow gates, immutable dependencies, timeouts, and project layering."""
import re
from pathlib import Path
import xml.etree.ElementTree as ET
import yaml


def errors(root):
    problems = []
    for path in sorted((root / '.github/workflows').glob('*.yml')):
        text = path.read_text()
        workflow = yaml.safe_load(text)
        for action in re.findall(r'uses:\s*(\S+)', text):
            if not action.startswith('./') and not re.search(r'@[a-f0-9]{40}$', action):
                problems.append(f'{path.name}: action is not pinned: {action}')
        for name, job in workflow.get('jobs', {}).items():
            if 'runs-on' in job and not job.get('timeout-minutes'):
                problems.append(f'{path.name}/{name}: missing timeout')
        if path.name == 'ci.yml':
            jobs = workflow['jobs']
            required = jobs['required']
            expected = set(jobs) - {'required'}
            if set(required['needs']) != expected:
                problems.append('CI gate must depend on every verification job')
            if required.get('if') != 'always()':
                problems.append('CI gate must run after failures and cancellations')
            if 'toJSON(needs)' not in str(required['steps']):
                problems.append('CI gate must verify every dependency result')
    for folder in ('infra/compose', 'workers/whatsapp-bridge'):
        for path in (root / folder).glob('*'):
            if path.name != 'Dockerfile' and not path.name.startswith('docker-compose'): continue
            for line in path.read_text().splitlines():
                match = re.match(r'\s*(?:FROM|image:)\s+(\S+)', line)
                if not match: continue
                image = match[1]
                if image.startswith('${JARVIS_'): continue  # releases enforce image digests before deployment
                if not re.search(r'@sha256:[a-f0-9]{64}', image):
                    problems.append(f'{path}: unpinned image: {image}')
    allowed = {'Jarvis.Domain': set(), 'Jarvis.Application': {'Jarvis.Domain'},
               'Jarvis.Workflows': {'Jarvis.Application'}, 'Jarvis.Agents': {'Jarvis.Application', 'Jarvis.Mcp', 'Jarvis.Memory', 'Jarvis.Workflows'}}
    for project, references in allowed.items():
        path = root / 'src' / project / f'{project}.csproj'
        actual = {Path(node.attrib['Include']).stem for node in ET.parse(path).findall('.//ProjectReference')}
        if not actual <= references: problems.append(f'{project}: forbidden references: {actual - references}')
    for path in (root / 'src/Jarvis.Agents').rglob('*.cs'):
        if {'bin', 'obj'} & set(path.parts): continue
        if re.search(r'\busing\s+(?:Microsoft\.EntityFrameworkCore|Jarvis\.Infrastructure)', path.read_text()):
            problems.append(f'{path}: agents must use Application ports')
    return problems


if __name__ == '__main__':
    issues = errors(Path(__file__).resolve().parents[2])
    for issue in issues: print(issue)
    raise SystemExit(bool(issues))
