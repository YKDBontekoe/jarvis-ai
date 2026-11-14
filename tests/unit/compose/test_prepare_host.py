"""prepare-host.sh writes a production .env and Garage config that start on any host."""
import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]


class PrepareHostTests(unittest.TestCase):
    def prepare(self, root: Path) -> tuple[str, str]:
        env = {**os.environ, "ENV_FILE": str(root / ".env.production"), "JARVIS_DATA_DIR": str(root / "data"),
               "JARVIS_DOMAIN": "jarvis.example.org"}
        result = subprocess.run(["bash", "scripts/deploy/prepare-host.sh"], cwd=REPO_ROOT, env=env, text=True,
                                capture_output=True, timeout=120)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return (root / ".env.production").read_text(), (root / "data" / "garage.toml").read_text()

    def test_garage_binds_ipv4_so_it_starts_on_hosts_without_ipv6(self):
        # "[::]" fails with "Address family not supported" where IPv6 is off, and file storage never starts.
        with tempfile.TemporaryDirectory() as tmp:
            _, garage = self.prepare(Path(tmp))
        self.assertNotIn("[::]", garage)
        for line in ('rpc_bind_addr = "0.0.0.0:3901"', 'api_bind_addr = "0.0.0.0:3900"',
                     'api_bind_addr = "0.0.0.0:3903"', 's3_region = "us-east-1"'):
            self.assertIn(line, garage)

    def test_registration_closes_after_the_first_account_by_default(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, _ = self.prepare(Path(tmp))
        self.assertIn("AUTH_ALLOW_REGISTRATION=first", env)
        self.assertIn("JARVIS_DOMAIN=jarvis.example.org", env)

    def test_existing_install_gets_a_private_cache_and_keeps_it_on_redeploy(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            env_file = root / ".env.production"
            env_file.write_text("JARVIS_DOMAIN=existing.example.org\n")
            env, _ = self.prepare(root)
            cache = root / "data" / "mcp-cache"
            self.assertIn(f"MCP_RUNNER_CACHE_DIR={cache}\n", env)
            self.assertIn("JARVIS_DOMAIN=existing.example.org\n", env)
            self.assertEqual(0o700, stat.S_IMODE(cache.stat().st_mode))
            self.assertEqual((os.getuid(), os.getgid()), (cache.stat().st_uid, cache.stat().st_gid))
            package = cache / "package-marker"
            package.write_text("cached package")
            self.prepare(root)
            self.assertEqual("cached package", package.read_text())

    def test_custom_cache_path_is_preserved(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            cache = root / "custom-cache"
            (root / ".env.production").write_text(f"MCP_RUNNER_CACHE_DIR={cache}\n")
            env, _ = self.prepare(root)
            self.assertIn(f"MCP_RUNNER_CACHE_DIR={cache}\n", env)
            self.assertEqual(0o700, stat.S_IMODE(cache.stat().st_mode))
            self.assertFalse((root / "data" / "mcp-cache").exists())


if __name__ == "__main__":
    unittest.main()
