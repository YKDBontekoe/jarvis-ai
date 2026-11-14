"""Run the generated runner's mounts as its non-root user, without starting production services.

Only the image and entrypoint are replaced by a small Node probe. The AppHost's mounts, uid/gid, read-only
root, dropped capabilities, limits and network isolation are used unchanged. A native executable on the cache
must run in both containers; package files persist, while a synthetic session home must disappear.
"""
import json
import os
import shutil
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path

from tests.unit.compose.test_production_compose import REPO_ROOT, REQUIRED_ENV, _publish


PROBE = r"""
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
assert.notEqual(process.getuid(), 0, 'runner must remain non-root');
const mounts = fs.readFileSync('/proc/mounts', 'utf8').trim().split('\n').map(line => line.split(' '));
assert.equal(mounts.find(m => m[1] === '/tmp')[2], 'tmpfs');
assert.notEqual(mounts.find(m => m[1] === '/cache')[2], 'tmpfs', 'package cache must be disk-backed');
assert.throws(() => fs.writeFileSync('/root-write-probe', 'no'), e => ['EROFS', 'EACCES'].includes(e.code));
const native = '/cache/package-entrypoint';
const home = '/tmp/mcp-runner/test-session';
if (process.argv[2] === 'seed') {
  fs.copyFileSync(process.execPath, native);
  fs.chmodSync(native, 0o700);
  fs.mkdirSync(home, { recursive: true });
  fs.writeFileSync(home + '/test-session-marker', 'synthetic session data');
  fs.writeFileSync('/cache/package-marker', 'reusable package');
} else {
  assert(!fs.existsSync(home), 'previous session data must not survive container recreation');
  assert.equal(fs.readFileSync('/cache/package-marker', 'utf8'), 'reusable package');
}
assert.equal(execFileSync(native, ['--version'], { encoding: 'utf8' }).trim(), process.version);
console.log(JSON.stringify({ phase: process.argv[2], diskCache: true, executable: true, nonRoot: true,
  sessionHome: process.argv[2] === 'seed' ? 'temporary' : 'discarded' }));
"""


@unittest.skipUnless(shutil.which("docker"), "Docker is required")
class McpCacheTests(unittest.TestCase):
    def test_disk_cache_is_writable_executable_and_survives_only_package_files(self):
        compose_file = _publish(REPO_ROOT / "artifacts/compose-test/base", "")
        project = "jarvis-mcp-cache-test-" + uuid.uuid4().hex[:12]
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory) / "cache"
            cache.mkdir(mode=0o700)
            # CI runs as an unprivileged uid, matching the default chosen by prepare-host.sh.
            self.assertNotEqual(0, os.getuid(), "Run the probe as the non-root deployment user")
            values = {**REQUIRED_ENV, "MCP_RUNNER_CACHE_DIR": str(cache),
                      "JARVIS_UID": str(os.getuid()), "JARVIS_GID": str(os.getgid()),
                      "JARVIS_API_IMAGE": "node:24-slim"}
            env_file = Path(directory) / "fixture.env"
            env_file.write_text("".join(f"{k}={v}\n" for k, v in values.items()))
            compose = ["docker", "compose", "-p", project, "--project-directory", str(REPO_ROOT),
                       "--env-file", str(env_file), "-f", str(compose_file)]
            # The caller's production env must never override this disposable fixture.
            environment = {**os.environ, **values}
            try:
                for phase in ("seed", "reuse"):
                    result = subprocess.run(compose + ["run", "--rm", "--no-deps", "-T",
                                            "--entrypoint", "node", "mcp-runner", "-", phase], input=PROBE,
                                            cwd=REPO_ROOT, env=environment, capture_output=True, text=True, timeout=240)
                    self.assertEqual(0, result.returncode, result.stdout[-2000:] + result.stderr[-2000:])
                    evidence = json.loads(result.stdout.strip())
                    self.assertTrue(evidence["executable"])
                    if phase == "reuse":
                        self.assertEqual("discarded", evidence["sessionHome"])
                    print(result.stdout.strip())
            finally:
                cleanup = subprocess.run(compose + ["down", "--volumes", "--remove-orphans"], cwd=REPO_ROOT,
                                         env=environment, capture_output=True, text=True, timeout=90)
                self.assertEqual(0, cleanup.returncode, cleanup.stderr[-2000:])


if __name__ == "__main__":
    unittest.main()
