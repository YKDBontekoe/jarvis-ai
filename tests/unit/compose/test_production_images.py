#!/usr/bin/env python3
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
COMPOSE_FILE = REPO_ROOT / "infra" / "compose" / "docker-compose.production.yml"

IMAGE_DEFAULTS = {
    "JARVIS_API_IMAGE": "ghcr.io/ykdbontekoe/jarvis-ai/api:latest",
    "JARVIS_WORKER_IMAGE": "ghcr.io/ykdbontekoe/jarvis-ai/worker:latest",
    "JARVIS_VOICE_WORKER_IMAGE": "ghcr.io/ykdbontekoe/jarvis-ai/voice-worker:latest",
}

REQUIRED_ENV = {
    "POSTGRES_PASSWORD": "postgres-test-secret",
    "TEMPORAL_PASSWORD": "temporal-test-secret",
    "S3_ACCESS_KEY": "jarvis_object_store",
    "S3_SECRET_KEY": "s3-test-secret",
    "GARAGE_CONFIG_FILE": "/tmp/garage.toml",
    "JARVIS_UID": "1000",
    "JARVIS_GID": "1000",
    "CODEX_AUTH_FILE": "/tmp/codex-auth.json",
    "JARVIS_DATA_PROTECTION_KEYS_DIR": "/tmp/jarvis-data-protection-keys",
    "AUTH_ISSUER": "https://jarvis.example.com",
    "AUTH_AUDIENCE": "jarvis-api",
    "AUTH_SIGNING_KEY": "production-test-signing-key-32bytes!",
    "JARVIS_DOMAIN": "jarvis.example.com",
    "LIVEKIT_DOMAIN": "voice.example.com",
    "JARVIS_WEB_ORIGIN": "https://jarvis.example.com",
    "LIVEKIT_API_KEY": "devkey",
    "LIVEKIT_API_SECRET": "jarvis-local-livekit-development-secret",
    "VOICE_WORKER_SECRET": "voice-test-secret",
}

_IMAGE_LINE = re.compile(
    r"^\s+image:\s+\$\{([A-Z0-9_]+):-([^}]+)\}\s*$",
    re.MULTILINE,
)


class ProductionComposeImageTests(unittest.TestCase):
    def test_compose_declares_ghcr_image_vars(self) -> None:
        text = COMPOSE_FILE.read_text(encoding="utf-8")
        found = dict(_IMAGE_LINE.findall(text))
        self.assertEqual(found, IMAGE_DEFAULTS)
        self.assertIn("dockerfile: infra/compose/Dockerfile", text)
        self.assertIn("dockerfile: workers/voice/Dockerfile", text)

    def test_image_vars_override_defaults(self) -> None:
        env = {
            "JARVIS_API_IMAGE": "ghcr.io/example/jarvis-ai/api:deadbeef",
            "JARVIS_WORKER_IMAGE": "ghcr.io/example/jarvis-ai/worker:deadbeef",
            "JARVIS_VOICE_WORKER_IMAGE": "ghcr.io/example/jarvis-ai/voice-worker:deadbeef",
        }
        rendered = _IMAGE_LINE.sub(
            lambda match: f"    image: {env.get(match.group(1), match.group(2))}",
            COMPOSE_FILE.read_text(encoding="utf-8"),
        )
        self.assertIn("image: ghcr.io/example/jarvis-ai/api:deadbeef", rendered)
        self.assertIn("image: ghcr.io/example/jarvis-ai/worker:deadbeef", rendered)
        self.assertIn("image: ghcr.io/example/jarvis-ai/voice-worker:deadbeef", rendered)

    @unittest.skipUnless(shutil.which("docker"), "docker is not installed")
    def test_docker_compose_config_interpolates_images(self) -> None:
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
            self.assertIn(
                "ghcr.io/example/jarvis-ai/voice-worker:deadbeef", rendered
            )


if __name__ == "__main__":
    unittest.main()
