#!/usr/bin/env python3
"""Exercise the previous API image against the upgraded disposable database."""
import json
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path
origin = os.environ['JARVIS_API_URL']
current_image = os.environ.get('CI_API_IMAGE', 'jarvis-ci-api:test')
old_image = os.environ['CI_PREVIOUS_API_IMAGE']
compose = ['docker', 'compose', '-p', 'jarvis-ci', '-f', 'tests/e2e/docker-compose.ci.yml']
fixture = json.loads(Path('artifacts/verification/identity-fixture.json').read_text())

def request(path, body=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(origin + '/api/v1' + path, data=json.dumps(body).encode() if body else None, headers=headers)
    with urllib.request.urlopen(req, timeout=15) as response: return json.load(response)

def switch(image):
    subprocess.run([*compose, 'up', '-d', '--wait', '--wait-timeout', '180', 'jarvis-api'],
                   env={**os.environ, 'CI_API_IMAGE': image, 'CI_ENVIRONMENT': 'Production'}, check=True)

try:
    switch(old_image)
    token = request('/auth/login', {'email': fixture['email'], 'password': fixture['password']})['accessToken']
    before = request('/conversations', token=token)
    item = request('/conversations', {'title': 'Previous API compatibility fixture'}, token=token)
    assert any(value['id'] == item['id'] for value in request('/conversations', token=token))
    assert request('/conversations/' + item['id'], token=token)['title'] == item['title']
    Path('artifacts/ci/rollback-compatibility.json').write_text(json.dumps({'previousApiImage': old_image,
        'sha': os.environ.get('GITHUB_SHA'), 'existingConversationsRead': len(before), 'writeAndReadVerified': True}, indent=2))
finally:
    switch(current_image)
