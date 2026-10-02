#!/usr/bin/env python3
"""Promote verified OCI artifacts, preserving their digests and attestations."""
import hashlib
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path
spec = importlib.util.spec_from_file_location('image_manifest', Path(__file__).with_name('image-manifest.py'))
images = importlib.util.module_from_spec(spec)
spec.loader.exec_module(images)
directory = Path(sys.argv[1])
repository = os.environ['GITHUB_REPOSITORY'].lower()
prefix = 'ghcr.io/' + repository
sha = os.environ['GITHUB_SHA']
tags = [sha]
if os.environ.get('GITHUB_REF_TYPE') == 'tag': tags.append(os.environ['GITHUB_REF_NAME'])
if os.environ.get('EXTRA_IMAGE_TAG'): tags.append(os.environ['EXTRA_IMAGE_TAG'])
for tag in tags:
    if not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}', tag): raise SystemExit('Invalid image tag')
release = {'sha': sha, 'runId': os.environ['GITHUB_RUN_ID'], 'images': {}}
for name in ('api', 'worker', 'whatsapp-bridge'):
    archive = directory / f'{name}.oci.tar'
    manifest = json.loads((directory / f'{name}.json').read_text())
    images.validate(archive, manifest)
    if manifest['sha'] != sha or manifest['name'] != name: raise SystemExit('Image source does not match release')
    for tag in tags:
        reference = f'{prefix}/{name}:{tag}'
        subprocess.run(['skopeo', 'copy', '--all', '--preserve-digests', f'oci-archive:{archive}', 'docker://' + reference], check=True)
        raw = subprocess.check_output(['skopeo', 'inspect', '--raw', 'docker://' + reference])
        if 'sha256:' + hashlib.sha256(raw).hexdigest() != manifest['digest']:
            raise SystemExit('Published image digest differs from tested image')
    release['images'][name] = {**manifest, 'reference': f'{prefix}/{name}@{manifest["digest"]}'}
(directory / 'release-manifest.json').write_text(json.dumps(release, indent=2) + '\n')
