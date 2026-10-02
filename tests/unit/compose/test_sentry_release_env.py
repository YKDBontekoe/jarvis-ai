#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SCRIPT = REPO_ROOT / "scripts" / "deploy" / "sentry_release_env.py"


def _load():
    spec = importlib.util.spec_from_file_location("sentry_release_env", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class SentryReleaseEnvTests(unittest.TestCase):
    def test_writes_the_deployed_sha_and_leaves_the_dsn(self) -> None:
        module = _load()
        updated = module.upsert_release(
            "SENTRY_DSN=https://public@example.ingest.sentry.io/1\nSENTRY_RELEASE=\n",
            "abc123",
        )
        self.assertIn("SENTRY_DSN=https://public@example.ingest.sentry.io/1\n", updated)
        self.assertIn("SENTRY_RELEASE=abc123\n", updated)
        self.assertNotIn("SENTRY_RELEASE=\n", updated)

    def test_replaces_a_previous_release(self) -> None:
        module = _load()
        updated = module.upsert_release("SENTRY_RELEASE=old\n", "newsha")
        self.assertEqual(updated, "SENTRY_RELEASE=newsha\n")

    def test_appends_when_the_key_is_missing(self) -> None:
        module = _load()
        updated = module.upsert_release("POSTGRES_PASSWORD=secret\n", "abc123")
        self.assertEqual(updated, "POSTGRES_PASSWORD=secret\nSENTRY_RELEASE=abc123\n")


if __name__ == "__main__":
    unittest.main()
