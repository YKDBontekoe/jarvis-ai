#!/usr/bin/env python3
"""Validate source/image identity before exporting deployment configuration."""
import json
import os
import re
import sys
from pathlib import Path
manifest = json.loads(Path(sys.argv[1]).read_text())
if manifest['sha'] != os.environ['GIT_SHA']: raise SystemExit('Deployment source SHA mismatch')
for name, variable in [('api', 'JARVIS_API_IMAGE'), ('worker', 'JARVIS_WORKER_IMAGE'), ('whatsapp-bridge', 'JARVIS_WHATSAPP_BRIDGE_IMAGE')]:
    image = manifest['images'][name]
    if image['sha'] != manifest['sha']: raise SystemExit('Image source SHA mismatch')
    if not re.fullmatch(r'ghcr\.io/[a-z0-9._/-]+@sha256:[a-f0-9]{64}', image['reference']): raise SystemExit('Deployment requires immutable image digests')
    print(variable + '=' + image['reference'])
