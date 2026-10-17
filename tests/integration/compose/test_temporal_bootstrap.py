"""Run the AppHost's production Temporal services on both real container networks."""
import json
import shutil
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path

from tests.unit.compose.test_production_compose import REPO_ROOT, REQUIRED_ENV, _publish


@unittest.skipUnless(shutil.which("docker"), "Docker is required")
class TemporalBootstrapTests(unittest.TestCase):
    def test_namespace_and_frontend_are_reachable_on_the_application_network(self):
        compose_file = _publish(REPO_ROOT / "artifacts/compose-test/base", "")
        project = "jarvis-temporal-test-" + uuid.uuid4().hex[:12]
        with tempfile.TemporaryDirectory() as directory:
            env_file = Path(directory) / "fixture.env"
            env_file.write_text("".join(f"{key}={value}\n" for key, value in REQUIRED_ENV.items()))
            compose = ["docker", "compose", "-p", project, "--project-directory", str(REPO_ROOT),
                       "--env-file", str(env_file), "-f", str(compose_file)]

            def run(command, timeout=360):
                result = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, timeout=timeout)
                self.assertEqual(0, result.returncode, result.stdout[-2000:] + result.stderr[-2000:])
                return result.stdout.strip()

            try:
                # Fresh database first, then an existing namespace: both bootstrap paths must complete.
                for _ in range(2):
                    run(compose + ["up", "-d", "--no-build", "temporal-namespace"])
                    namespace = run(compose + ["ps", "-a", "-q", "temporal-namespace"])
                    self.assertTrue(namespace)
                    exit_code = run(["docker", "wait", namespace])
                    if exit_code != "0":
                        logs = run(compose + ["logs", "--tail=30", "temporal", "temporal-namespace"])
                        self.fail("Namespace bootstrap failed: " + logs[-4000:])
                server = run(compose + ["ps", "-q", "temporal"])
                container = json.loads(run(["docker", "inspect", server]))[0]
                self.assertEqual(2, len(container["NetworkSettings"]["Networks"]))
                self.assertFalse(container["HostConfig"]["PortBindings"])
                health = run(compose + ["run", "--rm", "--no-deps", "--entrypoint", "temporal",
                                       "temporal-namespace", "--address", "temporal:7233",
                                       "operator", "cluster", "health"])
                self.assertIn("SERVING", health)
            finally:
                cleanup = subprocess.run(compose + ["down", "--volumes", "--remove-orphans"], cwd=REPO_ROOT,
                                         capture_output=True, text=True, timeout=120)
                self.assertEqual(0, cleanup.returncode, cleanup.stderr[-2000:])


if __name__ == "__main__":
    unittest.main()
