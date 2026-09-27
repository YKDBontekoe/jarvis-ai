"""Production account authentication checks against the disposable Jarvis API."""
import base64
import datetime as dt
import hashlib
import hmac
import json
import os
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

root = Path('artifacts/verification')
fixture = json.loads((root / 'identity-fixture.json').read_text())
issuer = 'https://jarvis.local'
audience = 'jarvis-api'
signing_key = 'verification-signing-key-at-least-32b'
local_opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
checks = []


def call(url, method='GET', data=None, token=None, expected=(200,), opener=None):
    headers = {}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if data is not None:
        headers['Content-Type'] = 'application/json'
        data = json.dumps(data).encode()
    request = urllib.request.Request(url, data, headers, method=method)
    try:
        response = (opener or local_opener).open(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read().decode()
    assert response.status in expected, f'{method} {urllib.parse.urlparse(url).path}: {response.status} {body}'
    return json.loads(body) if body and 'json' in response.headers.get('Content-Type', '') else body


def api(path, method='GET', data=None, token=None, expected=(200,)):
    return call('http://localhost:5082/api/v1' + path, method, data, token, expected=expected)


def b64(data):
    return base64.urlsafe_b64encode(data).decode().rstrip('=')


def signed_token(subject, aud=audience):
    header = b64(json.dumps({'alg': 'HS256', 'typ': 'JWT'}, separators=(',', ':')).encode())
    payload = {
        'sub': subject,
        'iss': issuer,
        'aud': aud,
        'exp': int((dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=5)).timestamp()),
    }
    body = b64(json.dumps(payload, separators=(',', ':')).encode())
    signing_input = f'{header}.{body}'.encode()
    signature = hmac.new(signing_key.encode(), signing_input, hashlib.sha256).digest()
    return f'{header}.{body}.{b64(signature)}'


def register(email):
    existing = api('/auth/login', 'POST', {'email': email, 'password': fixture['password']}, expected=(200, 401))
    if isinstance(existing, dict) and existing.get('accessToken'):
        return existing
    return api('/auth/register', 'POST', {'email': email, 'password': fixture['password']}, expected=(200,))


def record(name, action):
    try:
        evidence = action()
        checks.append({'name': name, 'status': 'passed', 'evidence': evidence})
    except Exception as error:
        checks.append({'name': name, 'status': 'failed', 'error': str(error)})
    (root / 'authentication-flow.json').write_text(json.dumps({'issuer': issuer, 'checks': checks}, indent=2) + '\n')
    print(checks[-1]['status'].upper() + ': ' + name, flush=True)


owner = register(fixture['email'])
other = register(fixture['otherEmail'])
access, other_access = owner['accessToken'], other['accessToken']


def boundaries():
    for path in ['/conversations', '/tasks', '/memory', '/files', '/approvals', '/integrations/credentials', '/audit']:
        api(path, expected=(401,))
    api('/conversations', token='invalid-bearer', expected=(401,))
    components = access.split('.')
    payload = json.loads(base64.urlsafe_b64decode(components[1] + '=' * (-len(components[1]) % 4)))
    payload['sub'] = 'attacker'
    forged = components[0] + '.' + b64(json.dumps(payload).encode()) + '.' + components[2]
    api('/conversations', token=forged, expected=(401,))
    api('/conversations', token=signed_token(payload.get('sub', 'owner'), aud='other-api'), expected=(401,))
    duplicate = api('/auth/register', 'POST', {'email': fixture['email'], 'password': fixture['password']}, expected=(409,))
    assert 'already exists' in duplicate['message']
    return {'unauthenticatedRoutes': 7, 'invalidBearerRejected': True, 'tamperingRejected': True,
            'wrongAudienceRejected': True, 'duplicateRegistrationRejected': True}


def isolation():
    conversation = api('/conversations', 'POST', {'title': 'Production ownership verification'}, access, (201,))
    memory = api('/memory', 'POST', {'kind': 'fact', 'content': 'Owner A synthetic isolation fact.'}, access, (201,))
    reminder = api('/reminders', 'POST', {'title': 'Owner A reminder',
        'dueAt': (dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=1)).isoformat()}, access, (201,))
    try:
        for collection, item in [('conversations', conversation), ('memory', memory), ('reminders', reminder)]:
            assert not any(value['id'] == item['id'] for value in api('/' + collection, token=other_access))
            api('/' + collection + '/' + item['id'], token=other_access, expected=(404,))
            api('/' + collection + '/' + item['id'], 'DELETE', token=other_access, expected=(404,))
        return {'owners': 2, 'resources': ['conversation', 'memory', 'reminder'], 'readsAndDeletesIsolated': True}
    finally:
        api('/conversations/' + conversation['id'], 'DELETE', token=access, expected=(204,))
        api('/memory/' + memory['id'], 'DELETE', token=access, expected=(204,))
        api('/reminders/' + reminder['id'], 'DELETE', token=access)


def refresh():
    original = api('/auth/login', 'POST', {'email': fixture['email'], 'password': fixture['password']})
    refreshed = api('/auth/refresh', 'POST', {'refreshToken': original['refreshToken']})
    assert api('/conversations', token=refreshed['accessToken']) is not None
    assert refreshed['refreshToken'] != original['refreshToken']
    replay = api('/auth/refresh', 'POST', {'refreshToken': original['refreshToken']}, expected=(401,))
    assert replay['error'] == 'invalid_grant'
    revoked = api('/auth/refresh', 'POST', {'refreshToken': refreshed['refreshToken']}, expected=(401,))
    assert revoked['error'] == 'invalid_grant'
    return {'refreshAccepted': True, 'tokenRotated': True, 'replayRejected': True, 'familyRevoked': True}


record('production-authentication-boundaries', boundaries)
record('owner-isolation-over-http', isolation)
record('account-refresh-and-replay', refresh)
# Protected short-lived test token for subsequent deployed checks, never a report field.
path = root / 'access-token.txt'
path.write_text(api('/auth/login', 'POST', {'email': fixture['email'], 'password': fixture['password']})['accessToken'])
os.chmod(path, 0o600)
raise SystemExit(1 if any(value['status'] == 'failed' for value in checks) else 0)
