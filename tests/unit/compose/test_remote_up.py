"""Exercise deployment failure ordering without touching Docker or production data."""
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class RemoteUpTests(unittest.TestCase):
    def run_deploy(self, scenario):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            scripts = root / "scripts" / "deploy"
            scripts.mkdir(parents=True)
            (scripts / "remote-up.sh").write_text((ROOT / "scripts/deploy/remote-up.sh").read_text())
            (scripts / "prepare-host.sh").write_text("#!/bin/sh\nexit 0\n")
            (scripts / "prepare-host.sh").chmod(0o755)
            (scripts / "compose-env.sh").write_text('''
jarvis_publish_compose() { :; }
jarvis_compose() {
  echo "compose $*" >> "$TEST_CALLS"
  case "$1" in
    ps)
      case "${*: -1}" in
        temporal-namespace) [ "$TEST_SCENARIO" = missing ] || echo namespace-test ;;
        jarvis-api) echo api-test ;;
        jarvis-worker) echo worker-test ;;
      esac ;;
    up) if [[ "${*: -1}" = temporal-namespace && "$TEST_SCENARIO" = start-error ]]; then return 1; fi ;;
    logs) echo "namespace setup diagnostic" ;;
  esac
  return 0
}
# macOS Bash 3 lacks mapfile and treats empty arrays as unset with nounset enabled.
mapfile() { jarvis_image_tags=("unrelated/image:current"); }
''')
            bin_dir = root / "bin"
            bin_dir.mkdir()
            (bin_dir / "docker").write_text('''#!/bin/bash
echo "docker $*" >> "$TEST_CALLS"
case "$1" in
  wait)
    if [[ "$TEST_SCENARIO" = wait-error ]]; then exit 1; fi
    if [[ "$TEST_SCENARIO" = failed ]]; then echo 1; else echo 0; fi ;;
  inspect)
    case "$*" in
      *State.Health*) echo healthy ;;
      *State.Status*) echo running ;;
      *) echo sha256:previous ;;
    esac ;;
  image) if [[ "$2" = inspect ]]; then echo previous; fi ;;
esac
exit 0
''')
            (bin_dir / "flock").write_text("#!/bin/sh\nexit 0\n")
            for path in bin_dir.iterdir():
                path.chmod(0o755)
            calls = root / "calls"
            result = subprocess.run(["bash", str(scripts / "remote-up.sh")], cwd=root, text=True,
                                    capture_output=True, timeout=10, env={**os.environ,
                                        "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"],
                                        "TEST_CALLS": str(calls), "TEST_SCENARIO": scenario,
                                        "DEPLOY_LOCK_FILE": str(root / "deploy.lock"),
                                        "JARVIS_API_IMAGE": "ghcr.io/test/jarvis/api:new",
                                        "JARVIS_WORKER_IMAGE": "ghcr.io/test/jarvis/worker:new"})
            return result, calls.read_text()

    def test_namespace_failure_keeps_application_containers_and_reports_logs(self):
        result, calls = self.run_deploy("failed")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("docker wait namespace-test", calls)
        self.assertIn("namespace setup diagnostic", result.stderr)
        self.assertNotIn("--remove-orphans", calls)
        self.assertNotIn("docker image rm", calls)

    def test_namespace_start_failure_reports_logs_before_replacing_applications(self):
        result, calls = self.run_deploy("start-error")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("namespace setup diagnostic", result.stderr)
        self.assertNotIn("--remove-orphans", calls)

    def test_missing_namespace_container_does_not_replace_applications(self):
        result, calls = self.run_deploy("missing")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("container is missing", result.stderr)
        self.assertNotIn("docker wait", calls)
        self.assertNotIn("--remove-orphans", calls)

    def test_wait_error_reports_logs_without_replacing_applications(self):
        result, calls = self.run_deploy("wait-error")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Could not wait", result.stderr)
        self.assertIn("namespace setup diagnostic", result.stderr)
        self.assertNotIn("--remove-orphans", calls)

    def test_success_waits_for_namespace_before_replacing_applications(self):
        result, calls = self.run_deploy("success")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertLess(calls.index("docker wait namespace-test"), calls.index("--remove-orphans"))
        self.assertIn("Jarvis deployment is healthy", result.stdout)


if __name__ == "__main__":
    unittest.main()
