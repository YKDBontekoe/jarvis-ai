import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Match the installed Chromium's normal reduced user-agent, without a stale hard-coded browser version.
export function chromiumUserAgent(version, arch = process.arch) {
  const match = String(version).match(/\b(\d+)\.\d+\.\d+\.\d+\b/);
  const platform = { x64: 'x86_64', arm64: 'aarch64' }[arch];
  if (!match || !platform) throw new Error('Unable to determine the installed Chromium user-agent.');
  return `Mozilla/5.0 (X11; Linux ${platform}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${match[1]}.0.0.0 Safari/537.36`;
}

export function launchUserAgent() {
  const override = process.env.JARVIS_BROWSER_USER_AGENT?.trim();
  if (override) {
    if (override.length > 512 || /[^\x20-\x7e]/.test(override))
      throw new Error('The configured browser user-agent must be one printable ASCII header value.');
    return override;
  }
  const require = createRequire('/app/package.json');
  const { chromium } = require('playwright');
  const version = execFileSync(chromium.executablePath(), ['--version'], {
    encoding: 'utf8', timeout: 10_000, maxBuffer: 8192,
  });
  return chromiumUserAgent(version);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  process.stdout.write(launchUserAgent());
