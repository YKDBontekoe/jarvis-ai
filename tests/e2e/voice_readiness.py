"""Verify unavailable Codex audio does not produce a falsely usable voice session."""
import os
import json
import time
import urllib.error
import urllib.request
from pathlib import Path

fixture = json.loads(Path('artifacts/verification/identity-fixture.json').read_text())
login = json.dumps({'email': fixture['email'], 'password': fixture['password']}).encode()
login_request = urllib.request.Request(os.environ.get('JARVIS_API_URL', 'http://localhost:5082') + '/api/v1/auth/login', data=login,
    headers={'Content-Type': 'application/json'})
try:
    with urllib.request.urlopen(login_request) as response:
        access = json.load(response)['accessToken']
except urllib.error.HTTPError as error:
    assert error.code == 401, error.code
    register = urllib.request.Request(os.environ.get('JARVIS_API_URL', 'http://localhost:5082') + '/api/v1/auth/register', data=login,
        headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(register) as response:
        access = json.load(response)['accessToken']

def request(method, path, data=None):
    headers = {'Authorization': 'Bearer ' + access, 'Content-Type': 'application/json'}
    req = urllib.request.Request(os.environ.get('JARVIS_API_URL', 'http://localhost:5082') + '/api/v1' + path, method=method,
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
    assert body['code'] == 'dependency_unavailable'
    assert body['detail'] == 'An unexpected error occurred.'  # Production redacts 5xx details.
    report = {'voiceFunctionalStatus': 'unavailable', 'readinessFailureHandling': 'passed',
        'httpStatus': code, 'sessionTokenIssued': False, 'durationSeconds': duration,
        'reason': 'Synthetic voice dependency outage; no usable session issued'}
    Path('artifacts/verification/voice-readiness.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))
finally:
    request('DELETE', '/conversations/' + conversation['id'])
