#!/usr/bin/env python3
"""Validate all supported Compose combinations using synthetic configuration."""
import importlib.util
import os
import subprocess
from pathlib import Path
root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('compose_test', root / 'tests/unit/compose/test_production_images.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)
env = {**os.environ, **fixtures.REQUIRED_ENV, 'S3_ACCESS_KEY': 'fixture', 'S3_SECRET_KEY': 'fixture',
       'HOME_ASSISTANT_MCP_URL': 'https://home.example.invalid/api/mcp', 'HOME_ASSISTANT_TOKEN': 'fixture', 'CODEX_AUTH_FILE': '/dev/null', 'CODING_REPO_PATH': '/tmp/jarvis-ci-repository', 'VOICE_WORKER_SECRET': 'jarvis-ci-fixture-voice-secret'}
base = root / 'infra/compose'
for path in sorted(base.glob('docker-compose*.yml')):
    if path.name in ('docker-compose.yml', 'docker-compose.production.yml'): files = [path]
    elif '.production.' in path.name: files = [base / 'docker-compose.production.yml', path]
    else: files = [base / 'docker-compose.yml', path]
    subprocess.run(['docker', 'compose', *[arg for p in files for arg in ('-f', str(p))], 'config', '--quiet'], env=env, check=True)
subprocess.run(['docker', 'compose', '-f', str(root / 'tests/e2e/docker-compose.ci.yml'), 'config', '--quiet'], check=True)
subprocess.run(['docker', 'compose', '-f', str(root / 'tests/e2e/docker-compose.ci.yml'), '-f', str(root / 'tests/e2e/docker-compose.models.yml'), 'config', '--quiet'], env={**env, 'CI_CODEX_MODEL': 'fixture'}, check=True)
print('Compose configurations validated')
