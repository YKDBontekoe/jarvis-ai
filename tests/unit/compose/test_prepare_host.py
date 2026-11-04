"""prepare-host.sh writes a production .env and Garage config that start on any host."""
import os
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


if __name__ == "__main__":
    unittest.main()
