#!/usr/bin/env python3
import re
import sys
from pathlib import Path
report = Path(sys.argv[1]).read_text()
count = re.search(r'^# tests (\d+)$', report, re.M)
if count is None or int(count[1]) == 0: raise SystemExit('No Node tests executed')
for key in ('fail', 'cancelled', 'skipped'):
    value = re.search(r'^# ' + key + r' (\d+)$', report, re.M)
    if value is None or int(value[1]) != 0: raise SystemExit('Node tests failed or skipped')
print(f'Validated {count[1]} Node tests')
