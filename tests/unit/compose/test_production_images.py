#!/usr/bin/env python3
from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
COMPOSE_FILE = REPO_ROOT / "infra" / "compose" / "docker-compose.production.yml"

REQUIRED_ENV = {
    "POSTGRES_PASSWORD": "postgres-test-secret",
    "TEMPORAL_PASSWORD": "temporal-test-secret",
    "MINIO_ROOT_USER": "jarvis_minio_admin",
    "MINIO_ROOT_PASSWORD": "minio-test-secret",
    "S3_ACCESS_KEY": "jarvis_object_store",
    "S3_SECRET_KEY": "s3-test-secret",
    "JARVIS_UID": "1000",
    "JARVIS_GID": "1000",
    "CODEX_AUTH_FILE": "/tmp/codex-auth.json",
    "JARVIS_DATA_PROTECTION_KEYS_DIR": "/tmp/jarvis-data-protection-keys",
    "OIDC_AUTHORITY": "https://identity.example.com/application/o/jarvis/",
    "OIDC_AUDIENCE": "jarvis-api",
    "JARVIS_DOMAIN": "jarvis.example.com",
    "LIVEKIT_DOMAIN": "voice.example.com",
    "JARVIS_WEB_ORIGIN": "https://jarvis.example.com",
    "LIVEKIT_API_KEY": "devkey",
    "LIVEKIT_API_SECRET": "jarvis-local-livekit-development-secret",
    "VOICE_WORKER_SECRET": "voice-test-secret",
}


class ProductionComposeImageTests(unittest.TestCase):
    def test_image_vars_interpolate(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = Path(tmp) / ".env.production"
            lines = [f"{key}={value}" for key, value in REQUIRED_ENV.items()]
            lines.extend(
                [
                    "JARVIS_API_IMAGE=ghcr.io/example/jarvis-ai/api:deadbeef",
                    "JARVIS_WORKER_IMAGE=ghcr.io/example/jarvis-ai/worker:deadbeef",
                    "JARVIS_VOICE_WORKER_IMAGE=ghcr.io/example/jarvis-ai/voice-worker:deadbeef",
                ]
            )
            env_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            completed = subprocess.run(
                [
                    "docker",
                    "compose",
                    "--env-file",
                    str(env_path),
                    "-f",
                    str(COMPOSE_FILE),
                    "config",
                ],
                check=True,
                capture_output=True,
                text=True,
                cwd=REPO_ROOT,
                env={**os.environ, "COMPOSE_PROJECT_NAME": "jarvis-compose-test"},
            )
            rendered = completed.stdout
            self.assertIn("ghcr.io/example/jarvis-ai/api:deadbeef", rendered)
            self.assertIn("ghcr.io/example/jarvis-ai/worker:deadbeef", rendered)
            self.assertIn("ghcr.io/example/jarvis-ai/voice-worker:deadbeef", rendered)
            self.assertIn("infra/compose/Dockerfile", rendered)
            self.assertIn("workers/voice/Dockerfile", rendered)


if __name__ == "__main__":
    unittest.main()
