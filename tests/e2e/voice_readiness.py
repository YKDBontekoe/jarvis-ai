"""Verify unavailable Codex audio does not produce a falsely usable voice session."""
import json
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

fixture = json.loads(Path('artifacts/verification/identity-fixture.json').read_text())
context = ssl.create_default_context(cafile='artifacts/verification/certs/localhost.pem')
grant = urllib.parse.urlencode(dict(grant_type='password', client_id=fixture['clientId'],
    username=fixture['username'], password=fixture['password'], scope='openid profile')).encode()
with urllib.request.urlopen(urllib.request.Request(fixture['issuer'] + '/protocol/openid-connect/token',
    data=grant), context=context) as response:
    access = json.load(response)['access_token']

def request(method, path, data=None):
    headers = {'Authorization': 'Bearer ' + access, 'Content-Type': 'application/json'}
    req = urllib.request.Request('http://localhost:5082/api/v1' + path, method=method,
        headers=headers, data=json.dumps(data).encode() if data is not None else None)
    try:
        response = urllib.request.urlopen(req, timeout=70)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read()
    return response.code, json.loads(body) if body else None

code, conversation = request('POST', '/conversations', {'title': 'Voice readiness verification'})
assert code == 201
started = time.monotonic()
try:
    code, body = request('POST', '/voice/session', {'conversationId': conversation['id']})
    duration = round(time.monotonic() - started, 3)
    assert code == 503, (code, 'Unexpected voice availability response')
    assert 'token' not in body
    assert body['detail'] == 'Voice service is temporarily unavailable.'
    report = {'voiceFunctionalStatus': 'unavailable', 'readinessFailureHandling': 'passed',
        'httpStatus': code, 'sessionTokenIssued': False, 'durationSeconds': duration,
        'reason': 'Codex CLI OAuth realtime compatibility failure; see voice-protocol.log'}
    Path('artifacts/verification/voice-readiness.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))
finally:
    request('DELETE', '/conversations/' + conversation['id'])
