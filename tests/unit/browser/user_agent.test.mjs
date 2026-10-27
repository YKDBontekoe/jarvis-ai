import assert from 'node:assert/strict';
import test from 'node:test';
import { chromiumUserAgent, launchUserAgent } from '../../../infra/compose/browser/user-agent.mjs';

test('user-agent follows the installed Chromium major version on supported architectures', () => {
  for (const [version, arch, platform, major] of [
    ['Chromium 149.0.7751.0', 'x64', 'x86_64', 149],
    ['Chromium 150.1.2.3', 'arm64', 'aarch64', 150],
  ]) {
    const ua = chromiumUserAgent(version, arch);
    assert(ua.includes(`Linux ${platform}`));
    assert(ua.includes(`Chrome/${major}.0.0.0`));
    assert(!ua.includes('HeadlessChrome'));
  }
});

test('unknown versions and unsupported architectures fail explicitly', () => {
  assert.throws(() => chromiumUserAgent('missing', 'x64'));
  assert.throws(() => chromiumUserAgent('149.0.0.0', 'unknown'));
});

test('operator override is explicit and refuses multiple header lines', () => {
  const previous = process.env.JARVIS_BROWSER_USER_AGENT;
  try {
    process.env.JARVIS_BROWSER_USER_AGENT = 'ExampleBrowser/1.0';
    assert.equal(launchUserAgent(), 'ExampleBrowser/1.0');
    process.env.JARVIS_BROWSER_USER_AGENT = 'ExampleBrowser/1.0\r\nHost: localhost';
    assert.throws(() => launchUserAgent());
  } finally {
    if (previous === undefined) delete process.env.JARVIS_BROWSER_USER_AGENT;
    else process.env.JARVIS_BROWSER_USER_AGENT = previous;
  }
});
