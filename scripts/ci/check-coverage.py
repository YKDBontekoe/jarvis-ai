#!/usr/bin/env python3
"""Require coverage of changed executable core lines; keep full reports for review."""
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
root = Path.cwd()
reports = list(Path(sys.argv[1]).rglob('coverage.cobertura.xml'))
if not reports: raise SystemExit('Missing backend coverage report')
lines = {}
for report in reports:
    for cls in ET.parse(report).findall('.//class'):
        filename = cls.get('filename', '').replace('\\', '/')
        for line in cls.findall('./lines/line'):
            key = (filename, int(line.get('number')))
            lines[key] = lines.get(key, False) or int(line.get('hits', '0')) > 0
base = os.environ.get('BASE_SHA')
if not base or set(base) == {'0'}: base = 'HEAD^'
diff = subprocess.check_output(['git', 'diff', '--unified=0', base, '--', 'src'], text=True)
filename = ''
changed = set()
for line in diff.splitlines():
    if line.startswith('+++ b/'): filename = line[6:]
    if not re.match(r'src/Jarvis\.(Domain|Application|Memory|Agents)/.*\.cs$', filename): continue
    if filename.endswith('DependencyInjection.cs'): continue
    match = re.match(r'@@ .*\+(\d+)(?:,(\d+))? @@', line)
    if match:
        start, count = int(match[1]), int(match[2] or '1')
        changed.update((filename, number) for number in range(start, start + count))
covered = []
for filename, number in changed:
    hits = [hit for (path, n), hit in lines.items() if n == number and (path.endswith(filename) or filename.endswith(path))]
    if hits: covered.append(any(hits))  # interfaces/comments have no executable sequence point
if covered:
    percentage = 100 * sum(covered) / len(covered)
    print(f'Changed core line coverage: {percentage:.1f}% ({sum(covered)}/{len(covered)})')
    if percentage < 80: raise SystemExit('Changed core line coverage must be at least 80%')
else:
    print('No changed executable core lines; full coverage report retained')
