"""Creates disposable local identity fixtures without printing passwords."""
import json
import os
import secrets
from pathlib import Path

target = Path('artifacts/verification')
target.mkdir(parents=True, exist_ok=True)
password = secrets.token_urlsafe(24)
realm = {
    'realm': 'jarvis-verification', 'enabled': True, 'sslRequired': 'all',
    'accessTokenLifespan': 90,
    'revokeRefreshToken': True, 'refreshTokenMaxReuse': 0,
    'registrationAllowed': False,
    'roles': {'realm': [{'name': 'offline_access'}]},
    'clientScopes': [{'name': name, 'protocol': 'openid-connect',
                      'attributes': {'include.in.token.scope': 'true'}}
                     for name in ['profile', 'email', 'offline_access', 'jarvis-api']],
    'clients': [{
        'clientId': 'jarvis-web', 'enabled': True, 'publicClient': True,
        'standardFlowEnabled': True, 'directAccessGrantsEnabled': True,
        'defaultClientScopes': ['profile', 'email'],
        'optionalClientScopes': ['offline_access', 'jarvis-api'],
        'redirectUris': ['http://localhost:5137/'], 'webOrigins': ['http://localhost:5137'],
        'attributes': {'pkce.code.challenge.method': 'S256'},
        'protocolMappers': [{
            'name': 'subject', 'protocol': 'openid-connect', 'protocolMapper': 'oidc-sub-mapper',
            'config': {'id.token.claim': 'true', 'access.token.claim': 'true'},
        }, {
            'name': 'jarvis-api-audience', 'protocol': 'openid-connect',
            'protocolMapper': 'oidc-audience-mapper',
            'config': {'included.custom.audience': 'jarvis-api',
                       'id.token.claim': 'false', 'access.token.claim': 'true'},
        }],
    }, {
        'clientId': 'jarvis-wrong-audience', 'enabled': True, 'publicClient': True,
        'standardFlowEnabled': False, 'directAccessGrantsEnabled': True,
        'protocolMappers': [{
            'name': 'subject', 'protocol': 'openid-connect', 'protocolMapper': 'oidc-sub-mapper',
            'config': {'id.token.claim': 'true', 'access.token.claim': 'true'},
        }],
    }],
    'users': [{
        'username': name, 'enabled': True, 'emailVerified': True,
        'realmRoles': ['offline_access'],
        'email': name + '@example.invalid', 'firstName': 'Verification', 'lastName': name,
        'credentials': [{'type': 'password', 'value': password, 'temporary': False}],
    } for name in ['jarvis-test-a', 'jarvis-test-b']],
}
for name, value in [('realm.json', realm), ('identity-fixture.json', {
    'issuer': 'https://localhost:8443/realms/jarvis-verification',
    'clientId': 'jarvis-web', 'username': 'jarvis-test-a', 'password': password,
})]:
    path = target / name
    path.write_text(json.dumps(value, indent=2) + '\n')
    os.chmod(path, 0o600)
print('Created isolated identity fixtures (passwords excluded from output).')
