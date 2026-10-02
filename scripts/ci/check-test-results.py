#!/usr/bin/env python3
"""Fail when a test process exits successfully without actually running tests."""
import sys
from pathlib import Path
import xml.etree.ElementTree as ET
paths = list(Path(sys.argv[1]).rglob('*.trx'))
if not paths: raise SystemExit('Missing TRX reports')
for path in paths:
    tree = ET.parse(path)
    counters = tree.find('.//{*}Counters')
    if counters is None or int(counters.get('executed', '0')) == 0:
        raise SystemExit(f'No tests executed: {path.name}')
    if any(int(counters.get(key, '0')) for key in ('failed', 'error', 'aborted', 'timeout', 'notExecuted')):
        raise SystemExit(f'Failed or skipped tests: {path.name}')
print(f'Validated {len(paths)} test reports')
