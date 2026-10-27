"""Exercise the real AppHost browser and egress proxy without host ports or private sessions."""
import json
import shutil
import subprocess
import tempfile
import unittest
import uuid
from pathlib import Path

from tests.unit.compose.test_production_compose import REPO_ROOT, REQUIRED_ENV, ALL_FEATURES, _publish


PROBE = r"""
const assert = require('node:assert/strict');
const net = require('node:net');
async function proxy(method, target) {
  return await new Promise((resolve, reject) => {
    const socket = net.connect(3128, 'browser-egress-proxy');
    let data = '';
    socket.setTimeout(15_000, () => socket.destroy(new Error('proxy timeout')));
    const host = method === 'CONNECT' ? target : new URL(target).host;
    socket.on('connect', () => socket.write(`${method} ${target} HTTP/1.1\r\nHost: ${host}\r\nConnection: close\r\n\r\n`));
    socket.on('error', reject);
    socket.on('data', chunk => {
      data += chunk;
      if (!data.includes('\r\n')) return;
      const status = Number(data.split(' ')[1]);
      socket.destroy(); resolve(status);
    });
  });
}
(async () => {
  assert.equal(await proxy('GET', 'http://example.com/'), 200, 'public HTTP must work');
  assert.equal(await proxy('CONNECT', 'example.com:443'), 200, 'public HTTPS must work');
  for (const host of ['localhost', 'sub.localhost', 'printer.local', 'metadata.google.internal',
      '127.0.0.1', '10.0.0.1', '172.16.0.1', '192.168.1.1', '169.254.169.254', '100.64.0.1',
      '[::1]', '[::ffff:127.0.0.1]', '[fc00::1]']) {
    assert.equal(await proxy('GET', `http://${host}/`), 403, `deny HTTP ${host}`);
    assert.equal(await proxy('CONNECT', `${host}:443`), 403, `deny HTTPS ${host}`);
  }
  assert.equal(await proxy('CONNECT', 'example.com:22'), 403, 'deny unsafe ports');
  const endpoint = 'http://playwright-mcp:8931/mcp';
  let session, protocol;
  async function rpc(body) {
    const headers = { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' };
    if (session) headers['Mcp-Session-Id'] = session;
    if (protocol) headers['MCP-Protocol-Version'] = protocol;
    const response = await fetch(endpoint, { method: 'POST', headers, body: JSON.stringify(body),
      signal: AbortSignal.timeout(60_000) });
    assert(response.ok, `MCP HTTP ${response.status}`);
    session = response.headers.get('mcp-session-id') ?? session;
    const text = await response.text();
    if (!text) return null;
    const payload = text.startsWith('event:') || text.startsWith('data:')
      ? text.split('\n').filter(l => l.startsWith('data:')).map(l => JSON.parse(l.slice(5).trim())).find(x => x.id === body.id)
      : JSON.parse(text);
    assert(!payload.error, JSON.stringify(payload.error));
    return payload.result;
  }
  const initialized = await rpc({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {
    protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'jarvis-browser-smoke', version: '1.0' } } });
  protocol = initialized.protocolVersion;
  await rpc({ jsonrpc: '2.0', method: 'notifications/initialized' });
  const tools = await rpc({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
  assert(tools.tools.some(t => t.name === 'browser_navigate'));
  const page = await rpc({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: {
    name: 'browser_navigate', arguments: { url: 'https://example.com/' } } });
  assert(!page.isError, JSON.stringify(page));
  assert(page.content.some(c => c.text?.includes('Example Domain')), 'real browser must render the public page');
  const identity = await rpc({ jsonrpc: '2.0', id: 4, method: 'tools/call', params: {
    name: 'browser_evaluate', arguments: { function: '() => navigator.userAgent' } } });
  assert(!identity.isError, JSON.stringify(identity));
  const userAgent = identity.content.filter(c => c.text).map(c => c.text).join(' ');
  assert(userAgent.includes('Chrome/'), 'browser must expose a Chromium-compatible user-agent');
  assert(!userAgent.includes('HeadlessChrome'), 'user-agent must not carry the headless-only product token');
  console.log(JSON.stringify({ userAgent: 'chromium-compatible', mcp: 'connected', page: 'Example Domain', publicHttp: 'allowed',
    publicHttps: 'allowed', blockedTargets: 13, unsafePorts: 'blocked' }));
})().catch(error => { console.error(error); process.exitCode = 1; });
"""


@unittest.skipUnless(shutil.which("docker"), "Docker is required")
class BrowserBootstrapTests(unittest.TestCase):
    def test_browser_can_browse_public_web_but_proxy_blocks_private_targets(self):
        compose_file = _publish(REPO_ROOT / "artifacts/compose-test/all", ALL_FEATURES)
        project = "jarvis-browser-test-" + uuid.uuid4().hex[:12]
        with tempfile.TemporaryDirectory() as directory:
            env_file = Path(directory) / "fixture.env"
            env_file.write_text("".join(f"{key}={value}\n" for key, value in REQUIRED_ENV.items()))
            compose = ["docker", "compose", "-p", project, "--project-directory", str(REPO_ROOT),
                       "--env-file", str(env_file), "-f", str(compose_file)]

            def run(command, input_text=None, timeout=360):
                result = subprocess.run(command, input=input_text, cwd=REPO_ROOT, capture_output=True,
                                        text=True, timeout=timeout)
                self.assertEqual(0, result.returncode, result.stdout[-3000:] + result.stderr[-3000:])
                return result.stdout.strip()

            try:
                run(compose + ["up", "-d", "--no-build", "--wait", "--wait-timeout", "180",
                               "playwright-mcp", "browser-egress-proxy"])
                browser = run(compose + ["ps", "-q", "playwright-mcp"])
                container = json.loads(run(["docker", "inspect", browser]))[0]
                self.assertFalse(container["HostConfig"]["PortBindings"])
                self.assertTrue(container["HostConfig"]["ReadonlyRootfs"])
                self.assertEqual(1, len(container["NetworkSettings"]["Networks"]))
                output = run(["docker", "exec", "-i", browser, "node", "-"], PROBE, timeout=180)
                self.assertIn('"mcp":"connected"', output)
                print(output)
            finally:
                cleanup = subprocess.run(compose + ["down", "--volumes", "--remove-orphans"], cwd=REPO_ROOT,
                                         capture_output=True, text=True, timeout=120)
                self.assertEqual(0, cleanup.returncode, cleanup.stderr[-2000:])


if __name__ == "__main__":
    unittest.main()
