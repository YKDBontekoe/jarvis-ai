#!/usr/bin/env python3
"""Bind an OCI archive to its source SHA, manifest digest and archive checksum."""
import hashlib
import json
import os
import re
import sys
import tarfile
from pathlib import Path

def digest_stream(stream):
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b''): digest.update(chunk)
    return digest.hexdigest()


def validate(path, manifest):
    if not re.fullmatch(r'sha256:[a-f0-9]{64}', manifest['digest']): raise ValueError('Invalid image digest')
    if not re.fullmatch(r'[a-f0-9]{40}', manifest['sha']): raise ValueError('Invalid source SHA')
    with path.open('rb') as stream:
        checksum = digest_stream(stream)
    if checksum != manifest['archiveSha256']: raise ValueError('Image archive checksum mismatch')
    with tarfile.open(path) as archive:
        index = json.load(archive.extractfile('index.json'))
    if manifest['digest'] not in {entry['digest'] for entry in index['manifests']}:
        raise ValueError('Build digest does not match OCI archive')

if __name__ == '__main__':
    name, digest, directory = sys.argv[1:4]
    archive = Path(directory) / f'{name}.oci.tar'
    with archive.open('rb') as stream:
        checksum = digest_stream(stream)
    manifest = {'name': name, 'sha': os.environ['GITHUB_SHA'], 'digest': digest, 'archiveSha256': checksum}
    validate(archive, manifest)
    (Path(directory) / f'{name}.json').write_text(json.dumps(manifest, indent=2) + '\n')
