"""Production JWT and OIDC checks against the disposable HTTPS identity service."""
import base64
import datetime as dt
import hashlib
import http.cookiejar
import json
import os
import secrets
import ssl
import urllib.error
import urllib.parse
import urllib.request
from html.parser import HTMLParser
from pathlib import Path

root = Path('artifacts/verification')
fixture = json.loads((root / 'identity-fixture.json').read_text())
issuer, client = fixture['issuer'], fixture['clientId']
context = ssl.create_default_context(cafile=str(root / 'certs/localhost.pem'))
local_opener = urllib.request.build_opener(urllib.request.ProxyHandler({}),
    urllib.request.HTTPSHandler(context=context))
checks = []


def call(url, method='GET', data=None, token=None, form=False, expected=(200,), opener=None):
    headers = {}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if data is not None:
        headers['Content-Type'] = 'application/x-www-form-urlencoded' if form else 'application/json'
        data = (urllib.parse.urlencode(data) if form else json.dumps(data)).encode()
    request = urllib.request.Request(url, data, headers, method=method)
    try:
        response = (opener or local_opener).open(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read().decode()
    assert response.status in expected, f'{method} {urllib.parse.urlparse(url).path}: {response.status}'
    return json.loads(body) if body and 'json' in response.headers.get('Content-Type', '') else body


def api(path, method='GET', data=None, token=None, expected=(200,)):
    return call('http://localhost:5082/api/v1' + path, method, data, token, expected=expected)


def grant(username='jarvis-test-a', grant_client=None):
    return call(issuer + '/protocol/openid-connect/token', 'POST', {
        'grant_type': 'password', 'client_id': grant_client or client, 'username': username,
        'password': fixture['password'], 'scope': 'openid' if grant_client else 'openid profile',
    }, form=True)


def record(name, action):
    try:
        evidence = action()
        checks.append({'name': name, 'status': 'passed', 'evidence': evidence})
    except Exception as error:
        checks.append({'name': name, 'status': 'failed', 'error': str(error)})
    (root / 'authentication-flow.json').write_text(json.dumps({'issuer': issuer, 'checks': checks}, indent=2) + '\n')
    print(checks[-1]['status'].upper() + ': ' + name, flush=True)


class LoginForm(HTMLParser):
    action = None

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag == 'form' and attrs.get('id') == 'kc-form-login':
            self.action = attrs.get('action')


class CaptureCallback(urllib.request.HTTPRedirectHandler):
    callback = None

    def redirect_request(self, request, fp, code, msg, headers, newurl):
        if newurl.startswith('http://localhost:5137/'):
            self.callback = newurl
            return None
        return super().redirect_request(request, fp, code, msg, headers, newurl)


def authorization_code(wrong_verifier=False):
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip('=')
    state = secrets.token_urlsafe(32)
    redirect = CaptureCallback()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context),
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), redirect)
    url = issuer + '/protocol/openid-connect/auth?' + urllib.parse.urlencode({
        'client_id': client, 'redirect_uri': 'http://localhost:5137/', 'response_type': 'code',
        'scope': 'openid profile offline_access jarvis-api', 'state': state,
        'code_challenge': challenge, 'code_challenge_method': 'S256',
    })
    html = call(url, opener=opener)
    parser = LoginForm()
    parser.feed(html)
    assert parser.action, 'OIDC login form was not returned'
    call(parser.action, 'POST', {'username': fixture['username'], 'password': fixture['password']},
         form=True, opener=opener, expected=(302,))
    parameters = urllib.parse.parse_qs(urllib.parse.urlparse(redirect.callback).query)
    assert parameters['state'] == [state]
    response = call(issuer + '/protocol/openid-connect/token', 'POST', {
        'client_id': client, 'grant_type': 'authorization_code', 'code': parameters['code'][0],
        'redirect_uri': 'http://localhost:5137/', 'code_verifier': secrets.token_urlsafe(64) if wrong_verifier else verifier,
    }, form=True, expected=(400,) if wrong_verifier else (200,))
    if wrong_verifier:
        assert response['error'] == 'invalid_grant'
        return {'challenge': 'S256', 'wrongVerifierRejected': True}
    assert api('/conversations', token=response['access_token']) is not None
    replay = call(issuer + '/protocol/openid-connect/token', 'POST', {
        'client_id': client, 'grant_type': 'authorization_code', 'code': parameters['code'][0],
        'redirect_uri': 'http://localhost:5137/', 'code_verifier': verifier,
    }, form=True, expected=(400,))
    assert replay['error'] == 'invalid_grant'
    return {'challenge': 'S256', 'statePreserved': True, 'apiAcceptedToken': True, 'codeReplayRejected': True}


tokens = grant()
other = grant('jarvis-test-b')
access, other_access = tokens['access_token'], other['access_token']


def boundaries():
    for path in ['/conversations', '/tasks', '/memory', '/files', '/approvals', '/integrations/credentials', '/audit']:
        api(path, expected=(401,))
    api('/conversations', token='invalid-bearer', expected=(401,))
    components = access.split('.')
    payload = json.loads(base64.urlsafe_b64decode(components[1] + '=' * (-len(components[1]) % 4)))
    payload['sub'] = 'attacker'
    forged = components[0] + '.' + base64.urlsafe_b64encode(json.dumps(payload).encode()).decode().rstrip('=') + '.' + components[2]
    api('/conversations', token=forged, expected=(401,))
    wrong = grant(grant_client='jarvis-wrong-audience')['access_token']
    api('/conversations', token=wrong, expected=(401,))
    return {'unauthenticatedRoutes': 7, 'invalidBearerRejected': True, 'tamperingRejected': True, 'wrongAudienceRejected': True}


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
    original = grant()
    refreshed = call(issuer + '/protocol/openid-connect/token', 'POST', {
        'client_id': client, 'grant_type': 'refresh_token', 'refresh_token': original['refresh_token'],
    }, form=True)
    assert api('/conversations', token=refreshed['access_token']) is not None
    assert refreshed['refresh_token'] != original['refresh_token']
    replay = call(issuer + '/protocol/openid-connect/token', 'POST', {
        'client_id': client, 'grant_type': 'refresh_token', 'refresh_token': original['refresh_token'],
    }, form=True, expected=(400,))
    assert replay['error'] == 'invalid_grant'
    return {'refreshAccepted': True, 'tokenRotated': True, 'replayRejected': True}


record('production-authentication-boundaries', boundaries)
record('owner-isolation-over-http', isolation)
record('oidc-authorization-code-pkce', authorization_code)
record('oidc-pkce-wrong-verifier', lambda: authorization_code(True))
record('oidc-refresh-and-replay', refresh)
# Protected short-lived test token for subsequent deployed checks, never a report field.
path = root / 'access-token.txt'
path.write_text(grant()['access_token'])
os.chmod(path, 0o600)
raise SystemExit(1 if any(value['status'] == 'failed' for value in checks) else 0)
