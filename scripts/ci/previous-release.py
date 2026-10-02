#!/usr/bin/env python3
"""Select a reachable release strictly older than HEAD, without guessing a schema."""
import re
import subprocess
from pathlib import Path

def git(*args): return subprocess.check_output(['git', *args], text=True).strip()
tags = [tag for tag in git('tag', '--merged', 'HEAD', '--sort=-version:refname').splitlines()
        if re.fullmatch(r'v\d+\.\d+\.\d+', tag) and git('rev-list', '-n', '1', tag) != git('rev-parse', 'HEAD')]
if not tags: raise SystemExit('No previous reachable release found')
files = git('ls-tree', '-r', '--name-only', tags[0], 'src/Jarvis.Infrastructure/Persistence/Migrations').splitlines()
migrations = sorted(Path(path).stem for path in files if re.search(r'/\d+_\w+\.cs$', path))
if not migrations: raise SystemExit('Previous release has no migrations')
print('JARVIS_PREVIOUS_RELEASE=' + tags[0])
print('JARVIS_UPGRADE_MIGRATION=' + migrations[-1])
