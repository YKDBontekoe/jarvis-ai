"""Creates disposable local account fixtures without printing passwords."""
import json
import os
import secrets
from pathlib import Path

target = Path('artifacts/verification')
target.mkdir(parents=True, exist_ok=True)
password = secrets.token_urlsafe(24) + 'Aa1'
fixture = {
    'email': 'jarvis-test-a@example.invalid',
    'otherEmail': 'jarvis-test-b@example.invalid',
    'username': 'jarvis-test-a',
    'password': password,
}
path = target / 'identity-fixture.json'
path.write_text(json.dumps(fixture, indent=2) + '\n')
os.chmod(path, 0o600)
print('Created isolated account fixtures (passwords excluded from output).')
