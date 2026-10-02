#!/usr/bin/env python3
import plistlib
import re
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    if archive.testzip(): raise SystemExit('IPA archive is corrupt')
    plist = plistlib.loads(archive.read('Payload/Runner.app/Info.plist'))
    for key in ('CFBundleIdentifier', 'CFBundleExecutable', 'CFBundleShortVersionString', 'CFBundleVersion'):
        if not plist.get(key): raise SystemExit(f'Missing IPA metadata: {key}')
    if not re.fullmatch(r'\d+\.\d+\.\d+', plist['CFBundleShortVersionString']): raise SystemExit('Invalid marketing version')
    binary = archive.read('Payload/Runner.app/' + plist['CFBundleExecutable'])
    if len(binary) < 1024 or binary[:4] not in (b'\xcf\xfa\xed\xfe', b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf'):
        raise SystemExit('IPA has no valid Mach-O executable')
print('IPA archive, metadata and executable verified')
