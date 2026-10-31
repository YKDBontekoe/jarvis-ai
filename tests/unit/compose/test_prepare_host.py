"""Exercise first deploy and upgrades in a disposable host directory, without Docker or real secrets."""
import hashlib
import os
import shutil
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class PrepareHostTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        scripts = self.root / "scripts/deploy"
        scripts.mkdir(parents=True)
        shutil.copy(ROOT / "scripts/deploy/prepare-host.sh", scripts)
        compose = self.root / "infra/compose"
        compose.mkdir(parents=True)
        shutil.copy(ROOT / "infra/compose/.env.production.example", compose)
        self.env_file = compose / ".env.production"
        self.data = self.root / "data"
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        docker = bin_dir / "docker"
        docker.write_text("#!/bin/sh\nexit 1\n")
        docker.chmod(0o755)
        self.env = {**os.environ, "JARVIS_DOMAIN": "jarvis.test", "JARVIS_DATA_DIR": str(self.data),
                    "ENV_FILE": str(self.env_file), "FINANCE_QUOTES_API_KEY": "",
                    "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"]}

    def prepare(self):
        result = subprocess.run(["bash", "scripts/deploy/prepare-host.sh"], cwd=self.root, env=self.env,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stderr)
        return dict(line.split("=", 1) for line in self.env_file.read_text().splitlines()
                    if line and not line.startswith("#") and "=" in line)

    def test_first_deploy_creates_private_writable_cache_and_env(self):
        values = self.prepare()
        cache = Path(values["MCP_RUNNER_CACHE_DIR"])
        self.assertEqual(self.data / "mcp-cache", cache)
        self.assertEqual(0o700, stat.S_IMODE(cache.stat().st_mode))
        self.assertEqual(int(values["JARVIS_UID"]), cache.stat().st_uid)
        self.assertEqual(int(values["JARVIS_GID"]), cache.stat().st_gid)
        self.assertEqual(0o600, stat.S_IMODE(self.env_file.stat().st_mode))
        (cache / "package").write_text("cached package")

    def test_upgrade_adds_cache_without_rotating_settings_or_deleting_packages(self):
        before = self.prepare()
        self.env_file.write_text("".join(line + "\n" for line in self.env_file.read_text().splitlines()
                                        if not line.startswith("MCP_RUNNER_CACHE_DIR=")))
        after = self.prepare()
        self.assertEqual(set(before), set(after))
        for key in before:
            self.assertEqual(hashlib.sha256(before[key].encode()).digest(),
                             hashlib.sha256(after[key].encode()).digest(), key)
        package = self.data / "mcp-cache/package"
        package.write_text("keep me")
        self.prepare()
        self.assertEqual("keep me", package.read_text())

    def test_operator_cache_path_is_preserved_and_created_with_private_permissions(self):
        custom = self.root / "custom-cache"
        self.env_file.write_text(f"JARVIS_DOMAIN=jarvis.test\nMCP_RUNNER_CACHE_DIR={custom}\n")
        values = self.prepare()
        self.assertEqual(str(custom), values["MCP_RUNNER_CACHE_DIR"])
        self.assertEqual(0o700, stat.S_IMODE(custom.stat().st_mode))
        self.assertFalse((self.data / "mcp-cache").exists())
