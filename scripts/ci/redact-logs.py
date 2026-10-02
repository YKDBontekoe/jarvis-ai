#!/usr/bin/env python3
"""Remove credential-bearing fields and test account values before uploading service logs."""
import json
import re
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text(errors='replace')
fixture = Path('artifacts/verification/identity-fixture.json')
if fixture.exists():
    for value in json.loads(fixture.read_text()).values():
        if isinstance(value, str) and len(value) > 8: text = text.replace(value, '[REDACTED]')
text = re.sub(r'(?i)(bearer\s+)[\w.\-]+', r'\1[REDACTED]', text)
text = re.sub(r'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', '[REDACTED]', text)
text = re.sub(r'(?i)((?:password|secret|access[_-]?token|refresh[_-]?token|authorization|api[_-]?key)["\s]*[:=]\s*)[^,;\s]+', r'\1[REDACTED]', text)
path.write_text(text)
